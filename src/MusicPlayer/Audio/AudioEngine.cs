using System;
using System.Collections.Generic;
using System.Threading;
using System.Timers;
using NAudio.CoreAudioApi;
using NAudio.Wasapi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MusicPlayer.DSP;
using MusicPlayer.Models;
using MusicPlayer.Utilities;
using Timer = System.Timers.Timer;

namespace MusicPlayer.Audio;

public enum AudioShutdownOutcome
{
    Completed,
    Quarantined
}

/// <summary>
/// Result of a shutdown attempt. Quarantined resources are intentionally kept
/// alive because device retirement could not be confirmed before the deadline.
/// </summary>
public sealed record AudioShutdownResult(
    AudioShutdownOutcome Outcome,
    int QuarantinedReaders,
    int QuarantinedDevices,
    string? QuarantineReason);

public class AudioEngine : IDisposable
{
    private IWavePlayer? _outputDevice;
    private AudioFileReader? _audioFile;
    private EqualizerSampleProvider? _equalizer;
    private ReverbSampleProvider? _reverb;
    private LoudnessSampleProvider? _loudness;
    private SoundEffectProcessor? _soundEffects;
    private SpeedPitchSampleProvider? _speedPitch;
    private VolumeSampleProvider? _volumeProvider;
    private AudioAnalysisCaptureProvider? _analysisCapture;

    private readonly List<MusicTrack> _queue = new();
    private int _currentIndex = -1;
    private PlayMode _playMode = PlayMode.LoopAll;
    private bool _shuffleEnabled = false;
    private readonly List<int> _shuffleOrder = new();

    private float _volume = 0.8f;
    private float _balance = 0.0f;
    private float _speed = 1.0f;
    private float _pitch = 0f;
    private int _deviceNumber = -1;

    // Serializes every operation that can attach or detach an output device or
    // replace the reader. A device is never replaced while an older consumer of
    // the same source may still be using it.
    private readonly object _transitionLock = new();

    // EQ & Effects state
    private bool _eqEnabled = true;
    private float[] _eqBandGains = new float[10];
    private bool _bassBoostEnabled = false;
    private float _bassBoostStrength = 0f;
    private bool _virtualizerEnabled = false;
    private float _virtualizerStrength = 0f;
    private SoundEffectType _activeSoundEffect = SoundEffectType.UserDefined;
    private ReverbPreset _reverbPreset = ReverbPreset.None;
    private bool _reverbEnabled;
    private bool _loudnessEnabled;
    private float _loudnessGain;

    // Playback settings
    public bool GaplessPlayback { get; set; } = true;
    public int CrossfadeDuration { get; set; } = 3000;
    public bool VolumeFade { get; set; }
    public bool ReplaySong { get; set; }

    private readonly Timer _positionTimer;
    private volatile bool _isPlaying = false;
    private int _advanceInProgress = 0; // 0=idle, 1=in-progress (Interlocked for thread safety)
    private volatile bool _loadingTrack = false; // true while LoadTrack is executing

    private double _lastPositionSeconds = -1;
    private int _stuckNearEndCount = 0;
    private int _idleRecoveryTicks = 0;

    public event EventHandler<MusicTrack>? TrackChanged;
    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackPaused;
    public event EventHandler? PlaybackStopped;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? PlaybackFinished;

    private MusicTrack? _currentTrack;
    private volatile bool _loadFailedForCurrentTrack;
    public MusicTrack? CurrentTrack => _currentTrack ?? (_currentIndex >= 0 && _currentIndex < _queue.Count 
        ? _queue[_currentIndex] : null);
    public bool IsPlaying => _isPlaying;
    public TimeSpan Position => _audioFile?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _audioFile?.TotalTime ?? TimeSpan.Zero;

    public static List<(int Id, string Name)> GetOutputDevices()
    {
        var devices = new List<(int, string)>();
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            devices.Add((i, caps.ProductName));
        }
        return devices;
    }

    public int DeviceNumber
    {
        get => Volatile.Read(ref _deviceNumber);
        set
        {
            if (_disposed) return;
            if (Volatile.Read(ref _engineState) >= StateStopping) return;

            lock (_transitionLock)
            {
                if (_disposed || Volatile.Read(ref _engineState) >= StateStopping) return;
                if (_deviceNumber == value) return;
                _deviceNumber = value;

                var oldDevice = _outputDevice;
                if (oldDevice == null) return;

                // Detach the old device before any new consumer can be created.
                _outputDevice = null;
                DetachPlaybackStopped(oldDevice);

                bool wasPlaying = _isPlaying;
                var reader = _audioFile;
                var pos = reader?.CurrentTime;

                var retirement = WaitForRetirement(RetireDeviceAsync(oldDevice));
                if (retirement != RetirementOutcome.Confirmed)
                {
                    // The old device may still be using this reader. Never attach
                    // a replacement consumer to the same source in this state.
                    _audioFile = null;
                    _isPlaying = false;
                    try { _positionTimer.Stop(); } catch { }
                    try { AudioAnalyzer.Instance.Attach(null); } catch { }
                    if (retirement == RetirementOutcome.TimedOut)
                    {
                        ScheduleReaderCleanup(reader, RetireDeviceAsync(oldDevice), oldDevice);
                    }
                    else
                    {
                        RecordUnconfirmedDevice(oldDevice, "DeviceNumber");
                        RetainUnconfirmedReader(reader, "DeviceNumber:retire-failed");
                    }
                    PlaybackStopped?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (CurrentTrack == null || reader == null || CurrentPlaybackSource == null)
                {
                    return;
                }

                try
                {
                    InitOutputDeviceWithFallback(CurrentPlaybackSource!);
                    _outputDevice!.PlaybackStopped += OnOutputDevicePlaybackStopped;
                    if (pos.HasValue) reader.CurrentTime = pos.Value;
                    if (wasPlaying) _outputDevice!.Play();
                }
                catch
                {
                    TryFallbackToDevice(0);
                }
            }
        }
    }

    protected virtual IWavePlayer CreateOutputDevice()
    {
        if (_deviceNumber >= 0)
        {
            return new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 6, DeviceNumber = _deviceNumber };
        }

        try
        {
            // WASAPI Shared Mode: Windows native audio session.
            // 120ms buffer provides rock-solid stability against heavy desktop contention (Chrome, multitasking)
            // with zero dropouts, while remaining responsive and in sync with the visualizer.
            return new WasapiOut(AudioClientShareMode.Shared, 120);
        }
        catch (Exception ex)
        {
            LogHelper.LogError("WasapiOut creation failed, fallback to WaveOutEvent", ex);
            return new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 6, DeviceNumber = -1 };
        }
    }

    /// <summary>
    /// Creates the reader that owns the source file. Kept virtual so tests can
    /// observe real disposal of the reader without touching a physical device.
    /// </summary>
    protected virtual AudioFileReader CreateAudioFileReader(string filePath)
    {
        return new AudioFileReader(filePath);
    }

    private void TryFallbackToDevice(int deviceNumber)
    {
        lock (_transitionLock)
        {
            if (_disposed || Volatile.Read(ref _engineState) >= StateStopping) return;

            var oldDevice = _outputDevice;
            _outputDevice = null;
            if (oldDevice != null)
            {
                DetachPlaybackStopped(oldDevice);
                var retirement = WaitForRetirement(RetireDeviceAsync(oldDevice));
                if (retirement != RetirementOutcome.Confirmed)
                {
                    // Do not hand the existing reader to a fallback device while
                    // the failed device may still be using it.
                    var unsafeReader = _audioFile;
                    _audioFile = null;
                    _isPlaying = false;
                    try { _positionTimer.Stop(); } catch { }
                    try { AudioAnalyzer.Instance.Attach(null); } catch { }
                    if (retirement == RetirementOutcome.TimedOut)
                    {
                        ScheduleReaderCleanup(unsafeReader, RetireDeviceAsync(oldDevice), oldDevice);
                    }
                    else
                    {
                        RecordUnconfirmedDevice(oldDevice, "TryFallbackToDevice");
                        RetainUnconfirmedReader(unsafeReader, "TryFallbackToDevice:retire-failed");
                    }
                    PlaybackStopped?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }

            var track = CurrentTrack;
            var source = CurrentPlaybackSource;
            if (track == null || _audioFile == null || source == null)
            {
                LogHelper.LogError("TryFallbackToDevice: track/audioFile/source is null", new InvalidOperationException());
                _isPlaying = false;
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
                return;
            }

            try
            {
                _deviceNumber = deviceNumber;
                InitOutputDeviceWithFallback(source, deviceNumber);
                _outputDevice!.PlaybackStopped += OnOutputDevicePlaybackStopped;
                _outputDevice!.Play();
                _isPlaying = true;
                _positionTimer.Start();
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("TryFallbackToDevice", ex);
                var failed = _outputDevice;
                _outputDevice = null;
                if (failed != null)
                {
                    DetachPlaybackStopped(failed);
                    var retirement = WaitForRetirement(RetireDeviceAsync(failed));
                    if (retirement != RetirementOutcome.Confirmed)
                    {
                        var unsafeReader = _audioFile;
                        _audioFile = null;
                        if (retirement == RetirementOutcome.TimedOut)
                        {
                            ScheduleReaderCleanup(unsafeReader, RetireDeviceAsync(failed), failed);
                        }
                        else
                        {
                            RecordUnconfirmedDevice(failed, "TryFallbackToDevice:attach");
                            RetainUnconfirmedReader(unsafeReader, "TryFallbackToDevice:attach");
                        }
                    }
                }
                _isPlaying = false;
                try { _positionTimer.Stop(); } catch { }
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Retires a finished (or broken) output device on a background thread so a
    /// hung NAudio Stop/Dispose can never block the load pipeline. The new device
    /// is created independently; if the old device number is momentarily still
    /// allocated, the Init retry loops below wait for it to free up.
    /// Returns true only when both Stop and Dispose completed without error.
    /// </summary>
    protected virtual Task<bool> RetireDeviceAsync(IWavePlayer oldDevice)
    {
        if (oldDevice == null) return Task.FromResult(true);

        lock (_retireLock)
        {
            // Retire each device at most once; concurrent callers share the one
            // authoritative retirement task instead of racing Stop/Dispose.
            if (_retirementTasks.TryGetValue(oldDevice, out Task<bool>? existing))
            {
                return existing;
            }

            var t = Task.Run(() =>
            {
                bool ok = true;
                try { oldDevice.Stop(); }
                catch (Exception ex)
                {
                    ok = false;
                    try { LogHelper.LogError("RetireDevice.Stop", ex); } catch { }
                }
                try { oldDevice.Dispose(); }
                catch (Exception ex)
                {
                    ok = false;
                    try { LogHelper.LogError("RetireDevice.Dispose", ex); } catch { }
                }
                return ok;
            });

            _retirementTasks[oldDevice] = t;
            _retiredDeviceTasks.Add(t);
            return t;
        }
    }

    /// <summary>
    /// Creates and initialises the preferred output device. On failure, retries and
    /// finally falls back to a WaveOut device with retries (handles the transient
    /// "AlreadyAllocated" that can occur right after retiring the previous device).
    /// </summary>
    private void InitOutputDeviceWithFallback(ISampleProvider source, int fallbackDeviceNumber = 0)
    {
        try
        {
            _outputDevice = CreateOutputDevice();
            _outputDevice.Init(source);
        }
        catch (Exception exInit)
        {
            LogHelper.LogError("OutputDevice.Init failed, attempting fallback device", exInit);
            _outputDevice?.Dispose();
            _outputDevice = null;
            if (!TryInitWaveOutDevice(fallbackDeviceNumber, source, 25))
            {
                throw;
            }
        }
    }

    private bool TryInitWaveOutDevice(int deviceNumber, ISampleProvider source, int attempts)
    {
        Exception? last = null;
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                _outputDevice = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 6, DeviceNumber = deviceNumber };
                _outputDevice.Init(source);
                return true;
            }
            catch (Exception ex)
            {
                last = ex;
                try { _outputDevice?.Dispose(); } catch { }
                _outputDevice = null;
                if (i < attempts - 1) Thread.Sleep(60);
            }
        }
        LogHelper.LogError($"WaveOut device {deviceNumber} init failed after {attempts} attempts", last ?? new InvalidOperationException());
        return false;
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_volumeProvider != null) _volumeProvider.Volume = _volume;
        }
    }

    public float Balance
    {
        get => _balance;
        set
        {
            _balance = Math.Clamp(value, -1f, 1f);
            if (_equalizer != null) _equalizer.Balance = _balance;
        }
    }

    public float Speed
    {
        get => _speed;
        set
        {
            _speed = Math.Clamp(value, 0.5f, 2.0f);
            if (_speedPitch != null)
            {
                _speedPitch.Speed = _speed;
                // Live change is now smooth: next grain uses new ratio.
                // Do NOT Reset() — that discards buffered audio and causes gap/silence.
            }
        }
    }

    public float Pitch
    {
        get => _pitch;
        set
        {
            _pitch = Math.Clamp(value, -12f, 12f);
            if (_speedPitch != null)
            {
                _speedPitch.Pitch = _pitch;
            }
        }
    }

    public bool EqualizerEnabled
    {
        get => _eqEnabled;
        set
        {
            _eqEnabled = value;
            if (_equalizer != null) _equalizer.IsEnabled = value;
        }
    }

    public bool BassBoostEnabled
    {
        get => _bassBoostEnabled;
        set
        {
            _bassBoostEnabled = value;
            if (_equalizer != null) _equalizer.BassBoostEnabled = value;
        }
    }

    public float BassBoostStrength
    {
        get => _bassBoostStrength;
        set
        {
            _bassBoostStrength = value;
            if (_equalizer != null) _equalizer.BassBoostStrength = value;
        }
    }

    public bool VirtualizerEnabled
    {
        get => _virtualizerEnabled;
        set
        {
            _virtualizerEnabled = value;
            if (_equalizer != null) _equalizer.VirtualizerEnabled = value;
        }
    }

    public float VirtualizerStrength
    {
        get => _virtualizerStrength;
        set
        {
            _virtualizerStrength = value;
            if (_equalizer != null) _equalizer.VirtualizerStrength = value;
        }
    }

    public ReverbPreset ReverbPreset
    {
        get => _reverbPreset;
        set { _reverbPreset = value; if (_reverb != null) _reverb.Preset = value; }
    }
    public bool ReverbEnabled
    {
        get => _reverbEnabled;
        set { _reverbEnabled = value; if (_reverb != null) _reverb.IsEnabled = value; }
    }
    public bool LoudnessEnabled
    {
        get => _loudnessEnabled;
        set { _loudnessEnabled = value; if (_loudness != null) _loudness.IsEnabled = value; }
    }
    public float LoudnessGain
    {
        get => _loudnessGain;
        set { _loudnessGain = Math.Clamp(value, -12f, 12f); if (_loudness != null) _loudness.GainDb = _loudnessGain; }
    }

    public SoundEffectType ActiveSoundEffect
    {
        get => _activeSoundEffect;
        set
        {
            _activeSoundEffect = value;
            if (_soundEffects != null) _soundEffects.ActiveEffect = value;
        }
    }

    public PlayMode PlayMode { get => _playMode; set => _playMode = value; }
    public bool ShuffleEnabled
    {
        get => _shuffleEnabled;
        set
        {
            _shuffleEnabled = value;
            if (value) GenerateShuffleOrder();
        }
    }

    public IReadOnlyList<MusicTrack> Queue => _queue.AsReadOnly();
    public int CurrentIndex => _currentIndex;

    public AudioEngine()
    {
        _positionTimer = new Timer(250);
        _positionTimer.Elapsed += OnPositionTimerElapsed;
    }

    public void SetBandGain(int band, float gainDb)
    {
        if (band >= 0 && band < 10)
        {
            _eqBandGains[band] = gainDb;
            _equalizer?.SetBandGain(band, gainDb);
        }
    }

    public void SetAllBands(float[] gains)
    {
        if (gains != null && gains.Length >= 10)
        {
            Array.Copy(gains, _eqBandGains, 10);
            _equalizer?.SetAllBands(gains);
        }
    }

    public void LoadTrack(MusicTrack track)
    {
        if (track == null) return;

        lock (_transitionLock)
        {
            if (_disposed || Volatile.Read(ref _engineState) >= StateStopping)
            {
                _loadingTrack = false;
                return;
            }

            _isPlaying = false;
            _loadingTrack = true;
            try { _positionTimer.Stop(); } catch { }

            var oldDevice = _outputDevice;
            _outputDevice = null;
            var oldReader = _audioFile;
            _audioFile = null;
            if (oldDevice != null) DetachPlaybackStopped(oldDevice);

            // Ownership: the old reader is released only after the old device's
            // retirement is confirmed. Otherwise it is deferred or quarantined.
            RetireDeviceAndReleaseReader(oldDevice, oldReader, "LoadTrack");
            _loadFailedForCurrentTrack = false;

            try
            {
                if (_disposed || Volatile.Read(ref _engineState) >= StateStopping) return;

                _currentTrack = track;
                var idx = _queue.FindIndex(t => t.Id == track.Id);
                if (idx >= 0) _currentIndex = idx;

                long w = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                int threadId = Environment.CurrentManagedThreadId;
                bool isThreadPool = Thread.CurrentThread.IsThreadPoolThread;
                LogHelper.LogInfo($"[PLAYBACK] Thread={threadId} (ThreadPool={isThreadPool}): LoadTrack start for '{track.Title}' ({track.FilePath})");

                long tReader0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _audioFile = CreateAudioFileReader(track.FilePath);
                long tReader = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - tReader0;
                LogHelper.LogInfo($"[PLAYBACK] Thread={threadId}: AudioFileReader={tReader}ms");

                // Audio DSP Graph:
                // AudioFileReader -> Equalizer -> Reverb -> Loudness -> SoundEffects
                //   -> SpeedPitch -> Volume -> [AnalysisCapture] -> WaveOutEvent
                long tChain0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _equalizer = new EqualizerSampleProvider(_audioFile)
                {
                    IsEnabled = _eqEnabled,
                    Balance = _balance,
                    BassBoostEnabled = _bassBoostEnabled,
                    BassBoostStrength = _bassBoostStrength,
                    VirtualizerEnabled = _virtualizerEnabled,
                    VirtualizerStrength = _virtualizerStrength
                };
                _equalizer.SetAllBands(_eqBandGains);
                long tEq = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - tChain0;

                long tRev0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _reverb = new ReverbSampleProvider(_equalizer) { Preset = _reverbPreset, IsEnabled = _reverbEnabled };
                long tReverb = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - tRev0;

                long tLoud0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _loudness = new LoudnessSampleProvider(_reverb) { IsEnabled = _loudnessEnabled, GainDb = _loudnessGain };
                long tLoud = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - tLoud0;

                long tFx0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _soundEffects = new SoundEffectProcessor(_loudness)
                {
                    ActiveEffect = _activeSoundEffect
                };
                long tSoundEffect = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - tFx0;

                long tSp0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _speedPitch = new SpeedPitchSampleProvider(_soundEffects)
                {
                    Speed = _speed,
                    Pitch = _pitch
                };
                long tSpeedPitch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - tSp0;

                long tVol0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _volumeProvider = new VolumeSampleProvider(_speedPitch)
                {
                    Volume = _volume
                };

                // Visualizer tap sits at the END of the DSP chain, right before the
                // output device, so the captured signal matches exactly what the user
                // hears (post EQ/effects/speed-pitch/volume) with no extra latency.
                _analysisCapture = new AudioAnalysisCaptureProvider(_volumeProvider);
                AudioAnalyzer.Instance.Attach(_analysisCapture);
                long tVolume = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - tVol0;
                long tChain = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - tChain0;

                LogHelper.LogInfo($"[PLAYBACK] Thread={threadId}: DSP chain={tChain}ms (Equalizer={tEq}ms, Reverb={tReverb}ms, Loudness={tLoud}ms, SoundEffect={tSoundEffect}ms, SpeedPitch={tSpeedPitch}ms, Volume={tVolume}ms)");

                if (_disposed || Volatile.Read(ref _engineState) >= StateStopping)
                {
                    AbandonInFlightLoad("LoadTrack:stopping-before-init");
                    return;
                }

                long s = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                LogHelper.LogInfo($"[PLAYBACK] Thread={threadId}: OutputDevice.Init start");
                try
                {
                    InitOutputDeviceWithFallback(_analysisCapture);
                }
                catch (Exception exInit)
                {
                    LogHelper.LogError("OutputDevice init failed (all attempts)", exInit);
                    var failedDevice = _outputDevice;
                    _outputDevice = null;
                    AbandonInFlightLoad("LoadTrack:init-failed", failedDevice);
                    throw;
                }
                long tInit = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - s;
                LogHelper.LogInfo($"[PLAYBACK] Thread={threadId}: OutputDevice.Init end ({tInit}ms)");

                if (_disposed || Volatile.Read(ref _engineState) >= StateStopping)
                {
                    AbandonInFlightLoad("LoadTrack:stopping-after-init");
                    return;
                }

                _outputDevice!.PlaybackStopped += OnOutputDevicePlaybackStopped;

                long total = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - w;
                TrackChanged?.Invoke(this, track);
                LogHelper.LogInfo($"[PLAYBACK] LoadTrack total={total}ms (AudioFileReader={tReader}ms, DSP={tChain}ms, OutputDevice.Init={tInit}ms)");
            }
            catch (Exception ex)
            {
                _loadFailedForCurrentTrack = true;
                LogHelper.LogError("LoadTrack", ex);
            }
            finally
            {
                _loadingTrack = false;
            }
        }
    }

    private ISampleProvider? CurrentPlaybackSource => (ISampleProvider?)_analysisCapture ?? _volumeProvider;

    /// <summary>
    /// Abandons a load that shutdown superseded. The device (if any) is retired
    /// before its reader is released; an unconfirmed retirement quarantines the
    /// reader instead of freeing it under a live device.
    /// </summary>
    private void AbandonInFlightLoad(string source, IWavePlayer? device = null)
    {
        var abandonedDevice = device ?? _outputDevice;
        var abandonedReader = _audioFile;
        _outputDevice = null;
        _audioFile = null;
        if (abandonedDevice != null) DetachPlaybackStopped(abandonedDevice);
        try { AudioAnalyzer.Instance.Attach(null); } catch { }
        RetireDeviceAndReleaseReader(abandonedDevice, abandonedReader, source);
    }

    public void Play()
    {
        if (_disposed || Volatile.Read(ref _engineState) >= StateStopping) return;
        if (_audioFile == null && _currentTrack != null && !_loadFailedForCurrentTrack)
        {
            LoadAndPlayAsync(_currentTrack);
            return;
        }
        if (_outputDevice == null || _audioFile == null)
        {
            LogHelper.LogInfo($"[PLAYBACK] Play skipped: _outputDevice={(_outputDevice != null)}, _audioFile={(_audioFile != null)}, _loadFailed={_loadFailedForCurrentTrack}");
            if (_loadFailedForCurrentTrack && _currentTrack != null)
            {
                LogHelper.LogInfo("[PLAYBACK] Retrying failed load");
                _loadFailedForCurrentTrack = false;
                LoadAndPlayAsync(_currentTrack);
                return;
            }
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
            return;
        }

        lock (_transitionLock)
        {
            if (_disposed || Volatile.Read(ref _engineState) >= StateStopping) return;
            if (_outputDevice == null || _audioFile == null) return;

            try
            {
                ResetStuckDetection();
                long p = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                int threadId = Environment.CurrentManagedThreadId;
                LogHelper.LogInfo($"[PLAYBACK] Thread={threadId}: Play start");
                if (VolumeFade && _volumeProvider != null)
                {
                    _volumeProvider.Volume = 0f;
                    _outputDevice.Play();
                    _isPlaying = true;
                    _positionTimer.Start();
                    AudioAnalyzer.Instance.NotifySeek();
                    _ = FadeVolumeAsync(0f, _volume, 120); // تقليل مدة Fade-in لاستجابة أسرع
                }
                else
                {
                    _outputDevice.Play();
                    _isPlaying = true;
                    _positionTimer.Start();
                    AudioAnalyzer.Instance.NotifySeek();
                }
                long tPlay = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - p;
                LogHelper.LogInfo($"[PLAYBACK] Thread={threadId}: Play end (device.Play={tPlay}ms)");
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exPlay)
            {
                LogHelper.LogError("Play failed, attempting device 0 fallback", exPlay);
                TryFallbackToDevice(0);
            }
        }
    }

    /// <summary>
    /// Load and play a track without blocking the UI thread. The full DSP chain is
    /// built on a background worker; playback begins automatically when ready.
    /// </summary>
    public void LoadAndPlayAsync(MusicTrack track)
    {
        if (track == null) return;
        if (_disposed) return;
        if (Volatile.Read(ref _engineState) >= StateStopping) return;
        Interlocked.CompareExchange(ref _engineState, StateActive, StateCreated);

        lock (_playbackLock)
        {
            _queuedTrack = track;
            _loadGeneration++;
            if (_playbackWorkerRunning)
            {
                // If the registered worker thread is dead, the flag is stale
                // (e.g. a previous LoadTrack got abandoned). Respawn instead of
                // silently swallowing the request — a stale flag otherwise freezes
                // the engine forever at end-of-track.
                var wt = _workerThread;
                if (wt == null || !wt.IsAlive)
                {
                    LogHelper.LogInfo("[PLAYBACK] Stale worker detected (thread dead), respawning");
                    _playbackWorkerRunning = false;
                }
                else
                {
                    return; // worker is draining the queue
                }
            }

            _playbackWorkerRunning = true;
            var worker = new Thread(PlaybackWorker)
            {
                IsBackground = true,
                Name = "PlaybackWorker"
            };
            _workerThread = worker;
            worker.Start();
        }
    }

    private void PlaybackWorker()
    {
        try
        {
            while (true)
            {
                if (_disposed || Volatile.Read(ref _engineState) >= StateStopping) { lock (_playbackLock) { _playbackWorkerRunning = false; _queuedTrack = null; } return; }

                MusicTrack track;
                int gen;
                lock (_playbackLock)
                {
                    if (_queuedTrack == null) { _playbackWorkerRunning = false; return; }
                    track = _queuedTrack;
                    _queuedTrack = null;
                    gen = _loadGeneration;
                }

                // Drop stale requests: a newer LoadAndPlayAsync superseded this one.
                if (gen != _loadGeneration || _disposed)
                {
                    LogHelper.LogInfo("[PLAYBACK] PlaybackWorker: skipping stale request");
                    continue;
                }

                long w0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                LoadTrack(track);
                // Re-check generation after the (potentially long) load: if a newer
                // request arrived mid-load, do not start playback of the stale track.
                bool adopted = false;
                lock (_playbackLock)
                {
                    if (gen == _loadGeneration && _queuedTrack == null && !_disposed &&
                        Volatile.Read(ref _engineState) < StateStopping)
                    {
                        adopted = true;
                    }
                }
                if (!adopted || _disposed || Volatile.Read(ref _engineState) >= StateStopping)
                {
                    LogHelper.LogInfo("[PLAYBACK] PlaybackWorker: load completed but request superseded; not playing");
                    continue;
                }
                // Final guard against a request that slipped in between adoption and Play.
                if (gen != _loadGeneration) { continue; }
                Play();
                LogHelper.LogInfo($"PlaybackWorker: load+play={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - w0}ms");
            }
        }
        catch (Exception ex)
        {
            LogHelper.LogError("PlaybackWorker crashed", ex);
            lock (_playbackLock) { _playbackWorkerRunning = false; }
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Pause()
    {
        lock (_transitionLock)
        {
            if (_disposed || Volatile.Read(ref _engineState) >= StateStopping) return;
            if (_outputDevice == null) return;

            try
            {
                AudioAnalyzer.Instance.MarkSilent();
                _outputDevice.Pause();
                _isPlaying = false;
                _positionTimer.Stop();
                PlaybackPaused?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }
    }

    private async Task FadeVolumeAsync(float from, float to, int durationMs)
    {
        if (_volumeProvider == null) return;
        const int steps = 20;
        var stepDelay = durationMs / steps;
        var delta = (to - from) / steps;
        for (int i = 0; i <= steps; i++)
        {
            _volumeProvider.Volume = Math.Clamp(from + delta * i, 0f, 1f);
            await Task.Delay(stepDelay);
        }
        _volumeProvider.Volume = to;
    }

    public void Stop()
    {
        lock (_transitionLock)
        {
            if (_disposed || Volatile.Read(ref _engineState) >= StateStopping) return;
            if (_outputDevice == null) return;

            try
            {
                AudioAnalyzer.Instance.MarkSilent();
                _outputDevice.Stop();
                _isPlaying = false;
                _positionTimer.Stop();
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_transitionLock)
        {
            if (_audioFile == null) return;
            _audioFile.CurrentTime = position;
            _speedPitch?.Reset();
            AudioAnalyzer.Instance.NotifySeek();
            PositionChanged?.Invoke(this, position);
        }
    }

    public void SeekRelative(double seconds)
    {
        lock (_transitionLock)
        {
            if (_audioFile == null) return;
            var newPos = _audioFile.CurrentTime.Add(TimeSpan.FromSeconds(seconds));
            newPos = TimeSpan.FromSeconds(Math.Max(0, Math.Min(newPos.TotalSeconds, _audioFile.TotalTime.TotalSeconds)));
            _audioFile.CurrentTime = newPos;
            _speedPitch?.Reset();
            AudioAnalyzer.Instance.NotifySeek();
            PositionChanged?.Invoke(this, newPos);
        }
    }

    public void ResetEqualizer()
    {
        try { _equalizer?.Reset(); } catch { }
    }

    public void SetQueue(List<MusicTrack> tracks, int startIndex = 0)
    {
        _queue.Clear();
        _queue.AddRange(tracks);
        _currentIndex = Math.Clamp(startIndex, -1, _queue.Count - 1);
        if (_shuffleEnabled) GenerateShuffleOrder();
    }

    public void AddToQueue(MusicTrack track)
    {
        _queue.Add(track);
    }

    public void InsertNext(MusicTrack track)
    {
        if (_currentIndex >= 0 && _currentIndex < _queue.Count)
        {
            _queue.Insert(_currentIndex + 1, track);
        }
        else
        {
            _queue.Add(track);
        }
        if (_shuffleEnabled) GenerateShuffleOrder();
    }

    public void RemoveFromQueue(int index)
    {
        lock (_playbackLock)
        {
            if (index < 0 || index >= _queue.Count) return;
            _queue.RemoveAt(index);

            // Keep _currentIndex pointing at the correct track after removal:
            //  - removed before current  -> current shifted left by one
            //  - removed the current     -> the slot now holds the next track (keep slot)
            //  - removed after current   -> no change
            if (index < _currentIndex)
            {
                _currentIndex--;
            }
            else if (index == _currentIndex)
            {
                if (_currentIndex >= _queue.Count) _currentIndex = _queue.Count - 1;
                _currentTrack = null;
            }

            // Shuffle order indexes are invalid after a removal — regenerate.
            if (_shuffleEnabled) GenerateShuffleOrder();
        }
    }

    public bool MoveQueueItem(int oldIndex, int newIndex)
    {
        lock (_playbackLock)
        {
            if (oldIndex < 0 || oldIndex >= _queue.Count) return false;
            if (newIndex < 0 || newIndex >= _queue.Count) return false;
            if (oldIndex == newIndex) return false;

            var track = _queue[oldIndex];
            _queue.RemoveAt(oldIndex);
            _queue.Insert(newIndex, track);

            // Reposition _currentIndex for the displaced track.
            if (oldIndex == _currentIndex)
            {
                _currentIndex = newIndex;
            }
            else if (oldIndex < _currentIndex && newIndex >= _currentIndex)
            {
                _currentIndex--;
            }
            else if (oldIndex > _currentIndex && newIndex <= _currentIndex)
            {
                _currentIndex++;
            }

            if (_shuffleEnabled) GenerateShuffleOrder();
            return true;
        }
    }

    public void ClearQueue()
    {
        _queue.Clear();
        _currentIndex = -1;
    }

    public void Next()
    {
        if (_queue.Count == 0) return;

        _currentIndex = GetNextIndex();
        if (_currentIndex >= 0 && _currentIndex < _queue.Count)
        {
            LoadAndPlayAsync(_queue[_currentIndex]);
        }
    }

    public void Previous()
    {
        if (_queue.Count == 0) return;

        if (_audioFile != null && _audioFile.CurrentTime.TotalSeconds > 3)
        {
            Seek(TimeSpan.Zero);
            return;
        }

        _currentIndex = GetPreviousIndex();
        if (_currentIndex >= 0 && _currentIndex < _queue.Count)
        {
            LoadAndPlayAsync(_queue[_currentIndex]);
        }
    }

    private int GetNextIndex()
    {
        if (_queue.Count == 0) return -1;

        return _playMode switch
        {
            PlayMode.Order => _currentIndex + 1 < _queue.Count ? _currentIndex + 1 : 0,
            PlayMode.LoopAll => (_currentIndex + 1) % _queue.Count,
            PlayMode.Single => _currentIndex,
            PlayMode.Shuffle => GetShuffleNextIndex(),
            _ => _currentIndex + 1
        };
    }

    private int GetPreviousIndex()
    {
        if (_queue.Count == 0) return -1;

        return _playMode switch
        {
            PlayMode.Order => _currentIndex - 1 >= 0 ? _currentIndex - 1 : _queue.Count - 1,
            PlayMode.LoopAll => (_currentIndex - 1 + _queue.Count) % _queue.Count,
            PlayMode.Single => _currentIndex,
            PlayMode.Shuffle => GetShufflePreviousIndex(),
            _ => _currentIndex - 1
        };
    }

    private void GenerateShuffleOrder()
    {
        _shuffleOrder.Clear();
        for (int i = 0; i < _queue.Count; i++)
            _shuffleOrder.Add(i);

        var rng = new Random();
        for (int i = _shuffleOrder.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (_shuffleOrder[i], _shuffleOrder[j]) = (_shuffleOrder[j], _shuffleOrder[i]);
        }
    }

    private int GetShuffleNextIndex()
    {
        if (_shuffleOrder.Count == 0) GenerateShuffleOrder();
        var pos = _shuffleOrder.IndexOf(_currentIndex);
        pos = (pos + 1) % _shuffleOrder.Count;
        return _shuffleOrder[pos];
    }

    private int GetShufflePreviousIndex()
    {
        if (_shuffleOrder.Count == 0) GenerateShuffleOrder();
        var pos = _shuffleOrder.IndexOf(_currentIndex);
        pos = (pos - 1 + _shuffleOrder.Count) % _shuffleOrder.Count;
        return _shuffleOrder[pos];
    }

    private readonly object _playbackLock = new();

    // Background playback worker: the DSP graph (file reader + EQ + reverb + loudness +
    // sound effects + SoundTouch + volume + analysis tap + output init) is built on a
    // worker thread so the UI thread is never blocked by heavy setup on track start.
    // Requests are serialized: rapid Next/Previous/Pause simply re-queue the newest
    // track and the single worker picks it up. Engine playback events (PlaybackStarted /
    // PositionChanged / TrackChanged) are already marshalled to the UI thread by the
    // ViewModel (MainViewModel.WireAudioEvents uses Dispatch/Dispatcher.BeginInvoke),
    // so firing them from this worker is safe.
    private MusicTrack? _queuedTrack = null;
    private bool _playbackWorkerRunning = false;
    private volatile int _loadGeneration = 0;
    private Thread? _workerThread = null;
    private volatile bool _disposed = false;
    // Lifecycle state machine: 0=Created,1=Active,2=Stopping,3=Disposed.
    private int _engineState = 0;
    private const int StateCreated = 0;
    private const int StateActive = 1;
    private const int StateStopping = 2;
    private const int StateDisposed = 3;

    // How long a transition waits for one device retirement before deferring
    // reader release (or refusing to attach a replacement consumer).
    internal TimeSpan RetireWaitTimeout { get; set; } = TimeSpan.FromSeconds(5);
    // Hard upper bound for ShutdownAsync. Reaching it yields a Quarantined
    // result instead of hanging window shutdown forever.
    internal TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);
    // Synchronous Dispose fallback: window shutdown awaits ShutdownAsync first,
    // so this bound should only matter during abnormal process teardown.
    internal TimeSpan ShutdownSyncWaitTimeout { get; set; } = TimeSpan.FromSeconds(10);

    private readonly object _shutdownGate = new();
    private Task<AudioShutdownResult>? _shutdownTask;

    // Tracked retired-device tasks and reader cleanup tasks so shutdown can
    // observe them instead of dropping fire-and-forget work.
    private readonly object _retireLock = new();
    private readonly Dictionary<IWavePlayer, Task<bool>> _retirementTasks =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<Task<bool>> _retiredDeviceTasks = new();
    private readonly List<Task> _cleanupTasks = new();
    private readonly HashSet<AudioFileReader> _decidedReaders =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<AudioFileReader> _unconfirmedReaders = new();
    private readonly HashSet<IWavePlayer> _unconfirmedDevices =
        new(ReferenceEqualityComparer.Instance);

    private enum RetirementOutcome
    {
        Confirmed,
        Failed,
        TimedOut,
        Faulted
    }

    private RetirementOutcome WaitForRetirement(Task<bool> retireTask)
    {
        try
        {
            if (!retireTask.Wait(RetireWaitTimeout))
            {
                return RetirementOutcome.TimedOut;
            }
            if (retireTask.Status == TaskStatus.RanToCompletion && retireTask.Result)
            {
                return RetirementOutcome.Confirmed;
            }
            return RetirementOutcome.Failed;
        }
        catch (Exception ex)
        {
            LogHelper.LogError("RetireDevice wait failed", ex);
            return RetirementOutcome.Faulted;
        }
    }

    private void DetachPlaybackStopped(IWavePlayer? device)
    {
        if (device == null) return;
        try { device.PlaybackStopped -= OnOutputDevicePlaybackStopped; } catch { }
    }

    /// <summary>
    /// Transitions that replace both device and reader (LoadTrack/abandon).
    /// The reader is released only when retirement is confirmed, deferred when
    /// retirement is still in progress, and quarantined when it failed.
    /// </summary>
    private void RetireDeviceAndReleaseReader(
        IWavePlayer? device,
        AudioFileReader? reader,
        string source)
    {
        if (device == null)
        {
            DisposeReaderOnce(reader, source + ":no-device");
            return;
        }

        Task<bool> retireTask = RetireDeviceAsync(device);
        RetirementOutcome outcome = WaitForRetirement(retireTask);
        switch (outcome)
        {
            case RetirementOutcome.Confirmed:
                DisposeReaderOnce(reader, source + ":retired");
                break;

            case RetirementOutcome.TimedOut:
                LogHelper.LogError(
                    $"RetireDevice timed out ({source}); deferring AudioFileReader disposal",
                    new TimeoutException());
                ScheduleReaderCleanup(reader, retireTask, device);
                break;

            default:
                RecordUnconfirmedDevice(device, source);
                RetainUnconfirmedReader(reader, source + ":retire-failed");
                break;
        }
    }

    private void RecordUnconfirmedDevice(IWavePlayer? device, string source)
    {
        if (device == null) return;
        bool added;
        lock (_retireLock) { added = _unconfirmedDevices.Add(device); }
        if (added)
        {
            LogHelper.LogError(
                $"AudioEngine quarantined output device after {source}",
                new InvalidOperationException("Device retirement could not be confirmed."));
        }
    }

    private void DisposeReaderOnce(AudioFileReader? reader, string source)
    {
        if (reader == null) return;

        // Exactly-once ownership decision: a reader can be either released by
        // this method or retained as unconfirmed, never both.
        lock (_retireLock)
        {
            if (!_decidedReaders.Add(reader)) return;
        }

        try
        {
            reader.Dispose();
        }
        catch (Exception ex)
        {
            lock (_retireLock) { _unconfirmedReaders.Add(reader); }
            LogHelper.LogError($"AudioEngine reader dispose failed ({source})", ex);
        }
    }

    private void RetainUnconfirmedReader(AudioFileReader? reader, string source)
    {
        if (reader == null) return;
        bool added;
        lock (_retireLock)
        {
            if (!_decidedReaders.Add(reader)) return;
            _unconfirmedReaders.Add(reader);
            added = true;
        }
        if (added)
        {
            LogHelper.LogError(
                $"AudioEngine retained AudioFileReader after {source}",
                new InvalidOperationException("Device retirement did not complete successfully."));
        }
    }

    private void ScheduleReaderCleanup(
        AudioFileReader? reader,
        Task<bool> retireTask,
        IWavePlayer? device)
    {
        if (reader == null) return;

        Task cleanup = Task.Run(async () =>
        {
            bool retired = false;
            try { retired = await retireTask.ConfigureAwait(false); }
            catch (Exception ex) { LogHelper.LogError("Deferred reader cleanup: retirement faulted", ex); }

            if (retired)
            {
                DisposeReaderOnce(reader, "deferred-retire");
            }
            else
            {
                RecordUnconfirmedDevice(device, "deferred-retire");
                RetainUnconfirmedReader(reader, "deferred-retire-failed");
            }
        });
        lock (_retireLock)
        {
            _cleanupTasks.Add(cleanup);
            _cleanupTasks.RemoveAll(x => x.IsCompleted);
        }
    }

    private static async Task<bool> WaitForTaskAsync(Task task, DateTime deadline)
    {
        if (task.IsCompleted) return true;
        TimeSpan remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) return task.IsCompleted;
        Task delay = Task.Delay(remaining);
        return await Task.WhenAny(task, delay).ConfigureAwait(false) == task;
    }

    private void OnPositionTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_disposed || Volatile.Read(ref _engineState) >= StateStopping) return;
        // Conservative watchdog: if the engine sits idle with a valid current
        // track but no file loaded (a load that never completed) for ~2s,
        // reload the track. Guarded by _loadingTrack / _loadFailedForCurrentTrack
        // so it never interrupts an active load and never loops on broken files.
        if (!_loadingTrack && !_isPlaying && _audioFile == null && _currentTrack != null && !_loadFailedForCurrentTrack)
        {
            _idleRecoveryTicks++;
            if (_idleRecoveryTicks >= 8)
            {
                _idleRecoveryTicks = 0;
                LogHelper.LogInfo("[PLAYBACK] Watchdog: idle engine detected, reloading current track");
                LoadAndPlayAsync(_currentTrack);
            }
        }
        else
        {
            _idleRecoveryTicks = 0;
        }

        if (!_isPlaying || _audioFile == null) return;

        try
        {
            var pos = _audioFile.CurrentTime;
            var total = _audioFile.TotalTime;
            PositionChanged?.Invoke(this, pos);

            if (total.TotalSeconds <= 0) return;

            if (pos >= total)
            {
                LogHelper.LogInfo($"[PLAYBACK] Timer detected end: pos={pos}, total={total}");
                ResetStuckDetection();
                AdvanceToNext("timer");
            }
            else if (pos.TotalSeconds >= total.TotalSeconds - 0.3)
            {
                bool deviceStopped = false;
                try
                {
                    if (_outputDevice is IWavePlayer wp)
                        deviceStopped = wp.PlaybackState != PlaybackState.Playing;
                }
                catch { }

                if (deviceStopped || pos.TotalSeconds >= total.TotalSeconds - 0.05)
                {
                    LogHelper.LogInfo($"[PLAYBACK] Timer advance: pos={pos.TotalSeconds:F3}s, total={total.TotalSeconds:F3}s, deviceStopped={deviceStopped}");
                    ResetStuckDetection();
                    AdvanceToNext("timer");
                }
                else
                {
                    // Safeguard: if position hasn't moved for 2+ ticks (~500ms) near the end, force advance.
                    var posSec = pos.TotalSeconds;
                    if (Math.Abs(posSec - _lastPositionSeconds) < 0.01)
                    {
                        _stuckNearEndCount++;
                        if (_stuckNearEndCount >= 3)
                        {
                            LogHelper.LogInfo($"[PLAYBACK] Timer stuck-near-end: pos={pos.TotalSeconds:F3}s, total={total.TotalSeconds:F3}s, stuckCount={_stuckNearEndCount}");
                            ResetStuckDetection();
                            AdvanceToNext("timer");
                        }
                    }
                    else
                    {
                        _stuckNearEndCount = 0;
                    }
                    _lastPositionSeconds = posSec;
                }
            }
            else
            {
                _lastPositionSeconds = pos.TotalSeconds;
                _stuckNearEndCount = 0;
            }
        }
        catch (Exception ex)
        {
            LogHelper.LogError("OnPositionTimerElapsed", ex);
        }
    }

    private void ResetStuckDetection()
    {
        _lastPositionSeconds = -1;
        _stuckNearEndCount = 0;
    }

    private void OnOutputDevicePlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_disposed || Volatile.Read(ref _engineState) >= StateStopping) return;
        LogHelper.LogInfo($"[PLAYBACK] OnOutputDevicePlaybackStopped: _isPlaying={_isPlaying}, _loadingTrack={_loadingTrack}, error={e.Exception?.Message ?? "none"}");
        if (_loadingTrack) return; // LoadTrack is disposing the old device — don't double-advance
        if (!_isPlaying) return;   // intentional pause/stop — don't advance

        AdvanceToNext("device");
    }

    private void AdvanceToNext(string reason)
    {
        if (Interlocked.CompareExchange(ref _advanceInProgress, 1, 0) != 0) return;

        try
        {
            PlaybackFinished?.Invoke(this, EventArgs.Empty);
            if (ReplaySong)
            {
                Seek(TimeSpan.Zero);
                Play();
            }
            else
            {
                Next();
            }
            LogHelper.LogInfo($"[PLAYBACK] AdvanceToNext via {reason}");
        }
        catch (Exception ex)
        {
            LogHelper.LogError($"[PLAYBACK] AdvanceToNext failed ({reason})", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _advanceInProgress, 0);
        }
    }

    public Task<AudioShutdownResult> ShutdownAsync()
    {
        return BeginShutdown();
    }

    private Task<AudioShutdownResult> BeginShutdown()
    {
        lock (_shutdownGate)
        {
            if (_shutdownTask != null) return _shutdownTask;

            _disposed = true;
            Interlocked.Exchange(ref _engineState, StateStopping);
            _shutdownTask = Task.Run(ShutdownCoreAsync);
            return _shutdownTask;
        }
    }

    private async Task<AudioShutdownResult> ShutdownCoreAsync()
    {
        string? quarantineReason = null;
        DateTime deadline = DateTime.UtcNow + ShutdownTimeout;

        try
        {
            // Stop timers first so no further callbacks fire.
            try { _positionTimer.Stop(); } catch { }
            try { _positionTimer.Dispose(); } catch { }

            // Reject queued work and wait for the playback worker with a bound.
            lock (_playbackLock)
            {
                _queuedTrack = null;
                _playbackWorkerRunning = false;
            }
            var worker = _workerThread;
            if (worker != null && worker.IsAlive)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                bool joined = false;
                try
                {
                    if (remaining > TimeSpan.Zero)
                    {
                        joined = await Task.Run(() => worker.Join(remaining)).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogError("AudioEngine.Shutdown: worker join failed", ex);
                }
                if (!joined)
                {
                    quarantineReason ??= "Playback worker did not exit before the shutdown deadline.";
                    LogHelper.LogError("AudioEngine.Shutdown: playback worker quarantine", new TimeoutException());
                }
            }

            // Observe every retired device, bounded by the same deadline.
            Task<bool>[] retired;
            lock (_retireLock) { retired = _retiredDeviceTasks.ToArray(); }
            foreach (var task in retired)
            {
                if (!await WaitForTaskAsync(task, deadline).ConfigureAwait(false))
                {
                    quarantineReason ??= "Device retirement did not finish before the shutdown deadline.";
                    continue;
                }
                try
                {
                    if (!task.Result)
                    {
                        quarantineReason ??= "Device retirement reported failure.";
                        LogHelper.LogError("AudioEngine.Shutdown: device retirement failed", new InvalidOperationException("Stop/Dispose reported failure."));
                    }
                }
                catch (Exception ex)
                {
                    quarantineReason ??= "Device retirement faulted.";
                    LogHelper.LogError("AudioEngine.Shutdown: device retirement faulted", ex);
                }
            }

            // Deferred cleanup tasks own some readers. Observe them only within
            // the deadline; a hung cleanup is quarantined, never awaited forever.
            Task[] cleanup;
            lock (_retireLock) { cleanup = _cleanupTasks.ToArray(); }
            foreach (var task in cleanup)
            {
                if (!await WaitForTaskAsync(task, deadline).ConfigureAwait(false))
                {
                    quarantineReason ??= "Deferred reader cleanup did not finish before the shutdown deadline.";
                    continue;
                }
                try { await task.ConfigureAwait(false); }
                catch (Exception ex)
                {
                    quarantineReason ??= "Deferred reader cleanup faulted.";
                    LogHelper.LogError("AudioEngine.Shutdown: deferred cleanup faulted", ex);
                }
            }

            // Serialize final teardown with LoadTrack/DeviceNumber/fallback.
            bool lockTaken = false;
            try
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    lockTaken = Monitor.TryEnter(_transitionLock, remaining);
                }
                if (!lockTaken)
                {
                    quarantineReason ??= "Audio transition did not finish before the shutdown deadline.";
                    LogHelper.LogError("AudioEngine.Shutdown: transition lock quarantine", new TimeoutException());
                }
                else
                {
                    var device = _outputDevice;
                    _outputDevice = null;
                    var reader = _audioFile;
                    _audioFile = null;
                    DetachPlaybackStopped(device);

                    try { AudioAnalyzer.Instance.Attach(null); }
                    catch (Exception ex) { LogHelper.LogError("AudioEngine.Shutdown: AudioAnalyzer detach failed", ex); }

                    bool deviceConfirmed = true;
                    if (device != null)
                    {
                        Task<bool> retireTask = RetireDeviceAsync(device);
                        bool retirementCompleted = retireTask.IsCompleted;
                        if (!retirementCompleted)
                        {
                            TimeSpan retireRemaining = deadline - DateTime.UtcNow;
                            if (retireRemaining > TimeSpan.Zero)
                            {
                                try
                                {
                                    retirementCompleted = retireTask.Wait(retireRemaining);
                                }
                                catch (AggregateException)
                                {
                                    retirementCompleted = true;
                                }
                            }
                        }

                        if (retirementCompleted)
                        {
                            try
                            {
                                if (!retireTask.GetAwaiter().GetResult())
                                {
                                    deviceConfirmed = false;
                                    RecordUnconfirmedDevice(device, "shutdown");
                                    quarantineReason ??= "Current device retirement reported failure.";
                                }
                            }
                            catch (Exception ex)
                            {
                                deviceConfirmed = false;
                                RecordUnconfirmedDevice(device, "shutdown");
                                quarantineReason ??= "Current device retirement faulted.";
                                LogHelper.LogError("AudioEngine.Shutdown: current device retirement faulted", ex);
                            }
                        }
                        else
                        {
                            deviceConfirmed = false;
                            RecordUnconfirmedDevice(device, "shutdown");
                            quarantineReason ??= "Current device retirement did not finish before the shutdown deadline.";
                        }
                    }

                    if (reader != null)
                    {
                        if (deviceConfirmed)
                        {
                            DisposeReaderOnce(reader, "shutdown");
                        }
                        else
                        {
                            RetainUnconfirmedReader(reader, "shutdown-device-unconfirmed");
                        }
                    }
                }
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_transitionLock);
            }
        }
        catch (Exception ex)
        {
            quarantineReason ??= "AudioEngine shutdown faulted: " + ex.Message;
            LogHelper.LogError("AudioEngine.Shutdown failed", ex);
        }
        finally
        {
            lock (_retireLock)
            {
                _retiredDeviceTasks.Clear();
                _cleanupTasks.Clear();
            }
            Interlocked.Exchange(ref _engineState, StateDisposed);
        }

        int quarantinedReaders;
        int quarantinedDevices;
        lock (_retireLock)
        {
            quarantinedReaders = _unconfirmedReaders.Count;
            quarantinedDevices = _unconfirmedDevices.Count;
        }

        if (quarantineReason == null && (quarantinedReaders > 0 || quarantinedDevices > 0))
        {
            quarantineReason = "Unconfirmed audio resources were quarantined.";
        }

        AudioShutdownOutcome outcome = quarantineReason == null
            ? AudioShutdownOutcome.Completed
            : AudioShutdownOutcome.Quarantined;

        if (outcome == AudioShutdownOutcome.Quarantined)
        {
            LogHelper.LogError(
                $"AudioEngine.Shutdown quarantined resources: {quarantineReason}",
                new TimeoutException(quarantineReason ?? "quarantine"));
        }

        return new AudioShutdownResult(
            outcome,
            quarantinedReaders,
            quarantinedDevices,
            quarantineReason);
    }

    public void Dispose()
    {
        Task shutdown = BeginShutdown();
        if (!shutdown.IsCompleted)
        {
            try
            {
                if (!shutdown.Wait(ShutdownSyncWaitTimeout))
                {
                    LogHelper.LogError("AudioEngine.Dispose: shutdown did not complete within the synchronous wait", new TimeoutException());
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("AudioEngine.Dispose: shutdown faulted", ex);
            }
        }
    }
}
