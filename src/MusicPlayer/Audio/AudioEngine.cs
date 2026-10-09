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
        get => _deviceNumber;
        set
        {
            if (_deviceNumber == value) return;
            _deviceNumber = value;
            if (_outputDevice != null)
            {
                try
                {
                    bool wasPlaying = _isPlaying;
                    var pos = _audioFile?.CurrentTime;
                    var track = CurrentTrack;
                    var oldDevice = _outputDevice;
                    _outputDevice = null;
                    oldDevice.PlaybackStopped -= OnOutputDevicePlaybackStopped;
                    RetireDeviceAsync(oldDevice);
                    if (track != null && _audioFile != null && _volumeProvider != null)
                    {
                        InitOutputDeviceWithFallback(_volumeProvider);
                        _outputDevice.PlaybackStopped += OnOutputDevicePlaybackStopped;
                        if (pos.HasValue) _audioFile.CurrentTime = pos.Value;
                        if (wasPlaying) _outputDevice.Play();
                    }
                }
                catch
                {
                    TryFallbackToDevice(0);
                }
            }
        }
    }

    private IWavePlayer CreateOutputDevice()
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

    private void TryFallbackToDevice(int deviceNumber)
    {
        var oldDevice = _outputDevice;
        _outputDevice = null;
        if (oldDevice != null)
        {
            try { oldDevice.PlaybackStopped -= OnOutputDevicePlaybackStopped; } catch { }
            RetireDeviceAsync(oldDevice);
        }

        var track = CurrentTrack;
        if (track == null || _audioFile == null || _volumeProvider == null)
        {
            LogHelper.LogError("TryFallbackToDevice: track/audioFile/volumeProvider is null", new InvalidOperationException());
            _isPlaying = false;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            _deviceNumber = deviceNumber;
            InitOutputDeviceWithFallback(_volumeProvider, deviceNumber);
            _outputDevice.PlaybackStopped += OnOutputDevicePlaybackStopped;
            _outputDevice.Play();
            _isPlaying = true;
            _positionTimer.Start();
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LogHelper.LogError("TryFallbackToDevice", ex);
            _isPlaying = false;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Retires a finished (or broken) output device on a background thread so a
    /// hung NAudio Stop/Dispose can never block the load pipeline. The new device
    /// is created independently; if the old device number is momentarily still
    /// allocated, the Init retry loops below wait for it to free up.
    /// </summary>
    private void RetireDeviceAsync(IWavePlayer oldDevice)
    {
        try
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { oldDevice.Stop(); } catch { }
                try { oldDevice.Dispose(); } catch { }
            });
        }
        catch { }
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
        _isPlaying = false;
        _loadingTrack = true;
        _positionTimer.Stop();
        var oldDevice = _outputDevice;
        _outputDevice = null;
        if (oldDevice != null)
        {
            try { oldDevice.PlaybackStopped -= OnOutputDevicePlaybackStopped; } catch { }
            RetireDeviceAsync(oldDevice);
        }

        _audioFile?.Dispose();
        _audioFile = null;
        _loadFailedForCurrentTrack = false;

        try
        {
            _currentTrack = track;
            var idx = _queue.FindIndex(t => t.Id == track.Id);
            if (idx >= 0) _currentIndex = idx;

            long w = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int threadId = Environment.CurrentManagedThreadId;
            bool isThreadPool = Thread.CurrentThread.IsThreadPoolThread;
            LogHelper.LogInfo($"[PLAYBACK] Thread={threadId} (ThreadPool={isThreadPool}): LoadTrack start for '{track.Title}' ({track.FilePath})");

            long tReader0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _audioFile = new AudioFileReader(track.FilePath);
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

            long s = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            LogHelper.LogInfo($"[PLAYBACK] Thread={threadId}: OutputDevice.Init start");
            try
            {
                InitOutputDeviceWithFallback(_analysisCapture);
            }
            catch (Exception exInit)
            {
                LogHelper.LogError("OutputDevice init failed (all attempts)", exInit);
                _outputDevice?.Dispose();
                _outputDevice = null;
                throw;
            }
            long tInit = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - s;
            LogHelper.LogInfo($"[PLAYBACK] Thread={threadId}: OutputDevice.Init end ({tInit}ms)");

            _outputDevice.PlaybackStopped += OnOutputDevicePlaybackStopped;

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

    public void Play()
    {
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

    /// <summary>
    /// Load and play a track without blocking the UI thread. The full DSP chain is
    /// built on a background worker; playback begins automatically when ready.
    /// </summary>
    public void LoadAndPlayAsync(MusicTrack track)
    {
        if (track == null) return;

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
                MusicTrack track;
                int gen;
                lock (_playbackLock)
                {
                    if (_queuedTrack == null) { _playbackWorkerRunning = false; return; }
                    track = _queuedTrack;
                    _queuedTrack = null;
                    gen = _loadGeneration;
                }

                long w0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                LoadTrack(track);
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

    public void Seek(TimeSpan position)
    {
        if (_audioFile == null) return;
        _audioFile.CurrentTime = position;
        _speedPitch?.Reset();
        AudioAnalyzer.Instance.NotifySeek();
        PositionChanged?.Invoke(this, position);
    }

    public void SeekRelative(double seconds)
    {
        if (_audioFile == null) return;
        var newPos = _audioFile.CurrentTime.Add(TimeSpan.FromSeconds(seconds));
        newPos = TimeSpan.FromSeconds(Math.Max(0, Math.Min(newPos.TotalSeconds, _audioFile.TotalTime.TotalSeconds)));
        _audioFile.CurrentTime = newPos;
        _speedPitch?.Reset();
        AudioAnalyzer.Instance.NotifySeek();
        PositionChanged?.Invoke(this, newPos);
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
    private int _loadGeneration = 0;
    private Thread? _workerThread = null;

    private void OnPositionTimerElapsed(object? sender, ElapsedEventArgs e)
    {
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

    public void Dispose()
    {
        _positionTimer?.Stop();
        _positionTimer?.Dispose();
        AudioAnalyzer.Instance.Attach(null);
        if (_outputDevice != null)
        {
            _outputDevice.PlaybackStopped -= OnOutputDevicePlaybackStopped;
            _outputDevice?.Dispose();
        }
        _audioFile?.Dispose();
    }
}
