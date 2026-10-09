using System;
using System.Diagnostics;
using System.Threading;

namespace MusicPlayer.Audio;

/// <summary>
/// Real-time audio analyzer driven by the actual playing audio.
///
/// Analysis is EVENT-DRIVEN: the capture provider signals <see cref="NotifyData"/>
/// every time the audio thread writes a new block, and a background worker then
/// performs a fresh Hann-windowed FFT over the newest samples. There is no
/// polling timer and no guessed block cadence — the spectrum is updated exactly
/// when the music moves, so the wave never lags or flutters between blocks.
///
/// Silence is confirmed by a watermark watchdog (the total sample count written
/// by the audio thread). Only when that watermark stops advancing for ~800ms
/// (paused / stopped / end of track) is the spectrum marked stale and the
/// worker parked.
///
/// FFT size is 1024 (a 23ms window at 44.1kHz) — the same capture size the
/// original Android app used (<c>d5/g.java</c>). A short window means fast
/// events are not smeared in time, keeping the display tight with the song.
/// </summary>
public sealed class AudioAnalyzer : IDisposable
{
    public const int SampleRate = 44100;
    public const int FftSize = 1024;
    public const int BandCount = 64;

    private static AudioAnalyzer? _instance;
    private static readonly object _gate = new();

    /// <summary>Shared analyzer so the pipeline can push and the UI can read.</summary>
    public static AudioAnalyzer Instance
    {
        get
        {
            lock (_gate)
            {
                _instance ??= new AudioAnalyzer();
                return _instance;
            }
        }
    }

    private AudioAnalysisCaptureProvider? _source;

    // FFT scratch buffers
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _magnitudes = new float[FftSize / 2];

    // Band output — valid outside lock for the visualizer.
    private readonly float[] _bands = new float[BandCount];
    private volatile float _level;
    private volatile bool _hasData;

    // Timestamp of the last computed spectrum (Stopwatch ticks). The visualizer
    // uses this to snap to the idle wave the instant fresh data stops.
    private long _lastSpectrumStamp;
    public long LastSpectrumStamp => Volatile.Read(ref _lastSpectrumStamp);

    private readonly AutoResetEvent _wake = new(false);
    private Thread? _worker;
    private volatile bool _running;
    private long _lastWritten;
    private int _silenceMs;
    private long _contentStamp;
    private int _quietMs;

    private readonly object _sync = new();
    private bool _disposed;

    // Precomputed tables for zero-cost per-frame math
    private readonly float[] _hannWindow = new float[FftSize];
    private readonly int[] _bandBinStart = new int[BandCount];
    private readonly int[] _bandBinEnd = new int[BandCount];
    private readonly float[] _bandsScratch = new float[BandCount];
    private long _lastFftTimestamp;

    private AudioAnalyzer()
    {
        // Precompute Hann window
        for (int i = 0; i < FftSize; i++)
        {
            _hannWindow[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1))));
        }

        // Precompute logarithmic frequency band bin ranges
        const double fMin = 20.0;
        const double fMax = 20000.0;
        int binCount = FftSize / 2;
        double binWidth = (double)SampleRate / FftSize;
        for (int b = 0; b < BandCount; b++)
        {
            double f0 = fMin * Math.Pow(fMax / fMin, (double)b / BandCount);
            double f1 = fMin * Math.Pow(fMax / fMin, (double)(b + 1) / BandCount);
            _bandBinStart[b] = Math.Clamp((int)(f0 / binWidth), 0, binCount - 1);
            _bandBinEnd[b] = Math.Clamp((int)(f1 / binWidth), _bandBinStart[b] + 1, binCount);
        }
    }

    /// <summary>
    /// Attach/detach the analyzer to a capture provider. The worker wakes when a
    /// provider is attached and parks again once real silence is confirmed.
    /// </summary>
    public void Attach(AudioAnalysisCaptureProvider? source)
    {
        lock (_sync)
        {
            _source = source;
            _hasData = false;
            _level = 0f;
            Volatile.Write(ref _lastSpectrumStamp, 0L);
            _lastWritten = source != null ? source.SamplesWritten : 0L;
            _silenceMs = 0;
            _running = source != null;
        }

        if (_worker == null)
        {
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "AudioAnalyzer" };
            _worker.Start();
        }
        _wake.Set();
    }

    /// <summary>
    /// Called by the audio thread after every new block is written. Cheap and
    /// non-blocking: it simply marks the analyzer live and wakes the worker.
    /// </summary>
    public void NotifyData()
    {
        if (_disposed) return;
        _running = true;
        _wake.Set();
    }

    /// <summary>
    /// Called immediately after a seek so the WorkerLoop treats the first
    /// post-seek block as fresh data. Without this the worker may compare
    /// SamplesWritten against a pre-seek snapshot and skip the frame, causing
    /// the visualizer to appear frozen for up to ~100ms after every seek.
    /// </summary>
    public void NotifySeek()
    {
        if (_disposed) return;
        lock (_sync)
        {
            // Reset the written-sample watermark so the first post-seek block
            // always triggers a new FFT, even if SamplesWritten hasn't changed yet.
            _lastWritten = -1L;
            // Clear the FFT rate-limit so we don't skip this first frame.
            _lastFftTimestamp = 0L;
        }
        _running = true;
        _wake.Set();
    }


    /// <summary>
    /// Force-immediately drops the wave back to the idle baseline, without
    /// waiting for any freshness/silence watchdog. Called by the engine the
    /// moment playback is stopped, so pressing Stop collapses the wave at once.
    /// </summary>
    public void MarkSilent()
    {
        lock (_sync)
        {
            _hasData = false;
            _level = 0f;
            for (int b = 0; b < BandCount; b++)
                _bands[b] = 0f;
        }
        Volatile.Write(ref _lastSpectrumStamp, 0L);
        _running = false;
    }

    /// <summary>Frequency band magnitudes (0..1), length <see cref="BandCount"/>.</summary>
    public ReadOnlySpan<float> Bands
    {
        get
        {
            lock (_sync) { return _bands.AsSpan(); }
        }
    }

    public float[] BandsCopy()
    {
        lock (_sync)
        {
            return (float[])_bands.Clone();
        }
    }

    public void BandsCopyInto(float[] destination)
    {
        lock (_sync)
        {
            Array.Copy(_bands, destination, Math.Min(_bands.Length, destination.Length));
        }
    }

    /// <summary>
    /// Copies the raw FFT magnitudes (normalized [0,1], length FftSize/2 = 512)
    /// into the destination array. Used by the visualizer for its own bin mapping.
    /// </summary>
    public void MagnitudesCopyInto(float[] destination)
    {
        lock (_sync)
        {
            Array.Copy(_magnitudes, destination, Math.Min(_magnitudes.Length, destination.Length));
        }
    }

    public const int MagnitudeCount = FftSize / 2;

    public float Level => _level;

    public bool HasData => _hasData;

    private void WorkerLoop()
    {
        while (!_disposed)
        {
            if (!_running)
            {
                _wake.WaitOne();
                continue;
            }

            _wake.WaitOne(100);

            if (_disposed) return;

            AudioAnalysisCaptureProvider? source;
            lock (_sync) { source = _source; }
            if (source == null) continue;

            long written = source.SamplesWritten;
            if (written != _lastWritten)
            {
                long now = Stopwatch.GetTimestamp();
                double elapsedMs = (now - _lastFftTimestamp) / (double)Stopwatch.Frequency * 1000.0;
                if (elapsedMs < 6.0)
                {
                    continue;
                }
                _lastFftTimestamp = now;

                _lastWritten = written;
                _silenceMs = 0;
                _hasData = true;
                ComputeSpectrum(source);
            }
            else
            {
                _silenceMs += 100;
                if (_silenceMs >= 300)
                {
                    _hasData = false;
                    _level = 0f;
                    lock (_sync)
                    {
                        for (int b = 0; b < BandCount; b++)
                            _bands[b] = 0f;
                    }
                    _running = false;
                }
            }
        }
    }

    private void ComputeSpectrum(AudioAnalysisCaptureProvider source)
    {
        Volatile.Write(ref _lastSpectrumStamp, Stopwatch.GetTimestamp());
        int filled = source.CopyLatest(_window, FftSize);
        float rms = source.Level;

        // Hann window using precomputed lookup table (zero trigonometry at runtime)
        for (int i = 0; i < filled; i++)
        {
            _re[i] = _window[i] * _hannWindow[i];
            _im[i] = 0f;
        }
        for (int i = filled; i < FftSize; i++)
        {
            _re[i] = 0f;
            _im[i] = 0f;
        }

        FFT(_re, _im);

        // Compute raw magnitudes normalized to [0,1] — NO dB compression.
        const float normScale = FftSize / 2f;
        for (int i = 0; i < FftSize / 2; i++)
        {
            float re0 = _re[i];
            float im0 = _im[i];
            float cur = Math.Clamp(MathF.Sqrt(re0 * re0 + im0 * im0) / normScale, 0f, 1f);
            _magnitudes[i] = cur * 0.5f + _magnitudes[i] * 0.5f;
        }

        ComputeBands();

        // Overall level: blend RMS with FFT frame peak for a stable 0..1 meter.
        float fftLevel = 0f;
        for (int i = 1; i < FftSize / 2; i++)
            if (_magnitudes[i] > fftLevel) fftLevel = _magnitudes[i];
        float blended = 0.55f * rms + 0.45f * fftLevel;
        _level = Math.Clamp(blended, 0f, 1f);

        long now = Stopwatch.GetTimestamp();
        double dtMs = _contentStamp == 0 ? 0d : (now - _contentStamp) / (double)Stopwatch.Frequency * 1000.0;
        _contentStamp = now;
        if (dtMs > 0 && dtMs <= 50)
        {
            if (_level < 0.02f)
            {
                _quietMs += (int)dtMs;
                if (_quietMs >= 200) _hasData = false;
            }
            else
            {
                _quietMs = 0;
                _hasData = true;
            }
        }
    }

    private void ComputeBands()
    {
        // Allocation-free logarithmic frequency band mapping using precomputed bin ranges
        for (int b = 0; b < BandCount; b++)
        {
            int i0 = _bandBinStart[b];
            int i1 = _bandBinEnd[b];
            float max = 0f;
            for (int i = i0; i < i1; i++)
            {
                if (_magnitudes[i] > max) max = _magnitudes[i];
            }
            _bandsScratch[b] = max;
        }

        lock (_sync) { Array.Copy(_bandsScratch, _bands, BandCount); }
    }

    // In-place decimation-in-time radix-2 FFT. n must be a power of two.
    private static void FFT(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len;
            double wr = Math.Cos(ang);
            double wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cur_r = 1.0, cur_i = 0.0;
                for (int k = 0; k < len / 2; k++)
                {
                    float ar = re[i + k];
                    float ai = im[i + k];
                    float br = (float)(re[i + k + len / 2] * cur_r - im[i + k + len / 2] * cur_i);
                    float bi = (float)(re[i + k + len / 2] * cur_i + im[i + k + len / 2] * cur_r);

                    re[i + k] = ar + br;
                    im[i + k] = ai + bi;
                    re[i + k + len / 2] = ar - br;
                    im[i + k + len / 2] = ai - bi;

                    double nxt_r = cur_r * wr - cur_i * wi;
                    cur_i = cur_r * wi + cur_i * wr;
                    cur_r = nxt_r;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wake.Set();
        _worker?.Join(200);
        _wake.Dispose();
    }
}