using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Windows.Media;

namespace MusicPlayer.Diagnostics;

/// <summary>
/// Writes real-time render frame telemetry to a shared memory buffer (MemoryMappedFile)
/// so that external monitoring tools (like FpsOverlay.exe) can read and display accurate
/// frame rate, frame time, and jank metrics without impacting MusicPlayer's rendering.
/// </summary>
public static class FpsTelemetry
{
    private const string MmfName = "MusicPlayer_FpsTelemetry";
    private const long BufferSize = 64;

    private static MemoryMappedFile? _mmf;
    private static MemoryMappedViewAccessor? _accessor;
    private static long _frameCount;
    private static long _lastTicks;
    private static double _recentSumMs;
    private static int _recentCount;
    private static double _cachedAvgFps = 60.0;
    private static double _recentMinFps = 60.0;
    private static int _jankCount;
    private static long _lastWindowResetTicks;
    private static bool _started;

    public static void Start()
    {
        if (_started) return;
        _started = true;

        try
        {
            _mmf = MemoryMappedFile.CreateOrOpen(MmfName, BufferSize);
            _accessor = _mmf.CreateViewAccessor(0, BufferSize, MemoryMappedFileAccess.Write);
            _lastTicks = Stopwatch.GetTimestamp();
            _lastWindowResetTicks = _lastTicks;
            CompositionTarget.Rendering += OnRendering;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FpsTelemetry] Init failed: {ex.Message}");
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (_accessor == null) return;

        long now = Stopwatch.GetTimestamp();
        long deltaTicks = now - _lastTicks;
        _lastTicks = now;

        double deltaMs = (deltaTicks * 1000.0) / Stopwatch.Frequency;
        if (deltaMs <= 0.5) return; // filter sub-millisecond duplicate ticks

        bool wasIdleGap = deltaMs > 120.0;

        _frameCount++;
        if (!wasIdleGap)
        {
            _recentSumMs += deltaMs;
            _recentCount++;

            double currentFps = 1000.0 / deltaMs;
            if (currentFps < _recentMinFps) _recentMinFps = currentFps;

            if (deltaMs > 33.33)
            {
                _jankCount++;
            }
        }

        // Rolling 500ms window for average FPS
        double elapsedSinceReset = (now - _lastWindowResetTicks) * 1000.0 / Stopwatch.Frequency;
        if (elapsedSinceReset >= 500.0)
        {
            if (_recentCount > 0)
            {
                _cachedAvgFps = 1000.0 / (_recentSumMs / _recentCount);
            }
            _recentSumMs = 0;
            _recentCount = 0;
            _recentMinFps = 60.0;
            _lastWindowResetTicks = now;
        }

        try
        {
            // Binary layout (matches FpsOverlay):
            // Offset  0 (Int64): Frame Count
            // Offset  8 (Double): Last Frame Delta (ms)
            // Offset 16 (Int64): Timestamp (Stopwatch ticks)
            // Offset 24 (Double): Average FPS
            // Offset 32 (Double): Minimum FPS
            // Offset 40 (Int32): Jank Frame Count (> 33.3ms)
            _accessor.Write(0, _frameCount);
            _accessor.Write(8, deltaMs);
            _accessor.Write(16, now);
            _accessor.Write(24, _cachedAvgFps);
            _accessor.Write(32, _recentMinFps);
            _accessor.Write(40, _jankCount);
        }
        catch
        {
            // Non-critical, ignore write errors
        }
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;

        try
        {
            CompositionTarget.Rendering -= OnRendering;
            _accessor?.Dispose();
            _accessor = null;
            _mmf?.Dispose();
            _mmf = null;
        }
        catch { }
    }
}
