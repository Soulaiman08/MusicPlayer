using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;

namespace MusicPlayer.Audio;

/// <summary>
/// Non-destructive pass-through <see cref="ISampleProvider"/> that taps the
/// playing audio stream for real-time analysis (RMS level + raw samples for FFT).
///
/// It sits at the END of the DSP chain (post EQ / effects / speed-pitch / volume),
/// so the captured signal matches exactly what the user hears. It does NOT modify
/// the audio: every sample read from <see cref="Source"/> is returned unchanged.
///
/// Features:
/// - MMCSS "Pro Audio" registration for kernel-level realtime thread priority scheduling.
/// - Allocation-free, modulo-free power-of-2 ring buffer.
/// - Thread-safe lock-free snapshots.
/// </summary>
public sealed class AudioAnalysisCaptureProvider : ISampleProvider
{
    [DllImport("avrt.dll", SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string TaskName, ref int TaskIndex);

    private readonly ISampleProvider _source;
    private readonly float[] _ring;
    private readonly int _mask;
    private volatile int _writeIndex;
    private volatile int _activityCounter;
    private long _samplesWritten;

    private int _mmcssInitialized = 0;
    private IntPtr _mmcssHandle = IntPtr.Zero;

    public AudioAnalysisCaptureProvider(ISampleProvider source, int ringSamples = 8192)
    {
        _source = source;
        // Ensure power of 2 for fast bitwise masking
        int size = 1;
        while (size < ringSamples) size <<= 1;
        _ring = new float[size];
        _mask = size - 1;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        // Elevate audio callback thread to Windows MMCSS "Pro Audio" priority
        if (Interlocked.Exchange(ref _mmcssInitialized, 1) == 0)
        {
            try
            {
                int taskIndex = 0;
                _mmcssHandle = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
            }
            catch { }
        }

        int read = _source.Read(buffer, offset, count);
        if (read <= 0) return read;

        // Lock-free, zero-modulo ring buffer copy on the audio thread
        int writeIdx = _writeIndex;
        int mask = _mask;
        for (int i = 0; i < read; i++)
        {
            _ring[(writeIdx + i) & mask] = buffer[offset + i];
        }
        _writeIndex = (writeIdx + read) & mask;
        Interlocked.Add(ref _samplesWritten, read);
        _activityCounter++;

        AudioAnalyzer.Instance.NotifyData();

        return read;
    }

    public int ActivityCounter => _activityCounter;
    public long SamplesWritten => _samplesWritten;

    public int CopyLatest(float[] dest, int count)
    {
        int available = Math.Min(count, _ring.Length);
        int writeIdx = _writeIndex;
        int mask = _mask;
        int start = (writeIdx - available) & mask;
        if (start + available <= _ring.Length)
        {
            Array.Copy(_ring, start, dest, 0, available);
        }
        else
        {
            int first = _ring.Length - start;
            Array.Copy(_ring, start, dest, 0, first);
            Array.Copy(_ring, 0, dest, first, available - first);
        }
        return available;
    }

    public float Level
    {
        get
        {
            int writeIdx = _writeIndex;
            int count = Math.Min(256, _ring.Length);
            int mask = _mask;
            float sumSq = 0f;
            int start = (writeIdx - count) & mask;
            for (int i = 0; i < count; i++)
            {
                float s = _ring[(start + i) & mask];
                sumSq += s * s;
            }
            float rms = MathF.Sqrt(sumSq / count);
            return Math.Clamp(rms, 0f, 1f);
        }
    }
}
