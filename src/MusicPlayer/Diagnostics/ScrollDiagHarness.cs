using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicPlayer.Utilities;

namespace MusicPlayer.Diagnostics;

/// <summary>
/// Controlled 4-mode diagnostic harness (A/B/C/D) for simultaneous frame, CPU,
/// Dispatcher, ThreadPool, GC, Image.Source, and decode-time measurement during
/// maximized scrolling in Artists and Tracks Grid.
/// Inert unless MUSICPLAYER_DIAG_MODE is set to A, B, C, or D.
/// </summary>
internal static class ScrollDiagHarness
{
    public enum DiagMode { None, A, B, C, D }

    public static readonly DiagMode Mode = ParseMode(Environment.GetEnvironmentVariable("MUSICPLAYER_DIAG_MODE"));
    public static bool IsActive => Mode != DiagMode.None;
    public static bool IsAutoRun => string.Equals(Environment.GetEnvironmentVariable("MUSICPLAYER_DIAG_AUTO"), "1", StringComparison.Ordinal);

    private static readonly string? OutputPath = Environment.GetEnvironmentVariable("MUSICPLAYER_DIAG_OUT");

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static uint _uiOsThreadId;
    private static bool _measuring;
    private static long _windowStartTicks;
    private static long _lastRenderTicks;
    private static readonly List<double> _frameDeltas = new(512);
    private static readonly List<double> _dispatcherWaits = new(256);
    private static readonly List<int> _tpPending = new(256);
    private static readonly List<int> _tpBusy = new(256);
    private static readonly List<double> _decodeTimes = new(512);
    private static readonly object _decodeLock = new();

    private static int _sourceUpdates;
    private static int _maxRealizedContainers;
    private static TimeSpan _uiCpuStart;
    private static TimeSpan _procCpuStart;
    private static TimeSpan _gcPauseStart;
    private static int _gen0Start, _gen1Start, _gen2Start;

    private static DispatcherTimer? _sampleTimer;
    private static Dispatcher? _uiDispatcher;
    private static Func<int>? _realizedCounter;

    private static BitmapSource? _ramFallbackBitmap;

    private static DiagMode ParseMode(string? val) => val?.Trim().ToUpperInvariant() switch
    {
        "A" => DiagMode.A,
        "B" => DiagMode.B,
        "C" => DiagMode.C,
        "D" => DiagMode.D,
        _ => DiagMode.None
    };

    public static BitmapSource GetOrCreateRamFallbackBitmap()
    {
        if (_ramFallbackBitmap != null) return _ramFallbackBitmap;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(45, 52, 68)), null, new Rect(0, 0, 128, 128));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(90, 140, 220)), null, new Point(64, 64), 28, 28);
        }
        var rtb = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        _ramFallbackBitmap = rtb;
        return rtb;
    }

    public static void RecordSourceUpdate()
    {
        if (_measuring) Interlocked.Increment(ref _sourceUpdates);
    }

    public static void RecordDecode(double ms)
    {
        if (!_measuring) return;
        lock (_decodeLock) _decodeTimes.Add(ms);
    }

    private static TimeSpan GetThreadCpuTime(uint osThreadId)
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            foreach (ProcessThread t in proc.Threads)
            {
                if ((uint)t.Id == osThreadId)
                    return t.TotalProcessorTime;
            }
        }
        catch { }
        return TimeSpan.Zero;
    }

    public static void BeginWindow(Dispatcher dispatcher, Func<int> realizedCounter)
    {
        _uiDispatcher = dispatcher;
        _uiOsThreadId = GetCurrentThreadId();
        _realizedCounter = realizedCounter;

        lock (_decodeLock) _decodeTimes.Clear();
        _frameDeltas.Clear();
        _dispatcherWaits.Clear();
        _tpPending.Clear();
        _tpBusy.Clear();
        _sourceUpdates = 0;
        _maxRealizedContainers = 0;

        _uiCpuStart = GetThreadCpuTime(_uiOsThreadId);
        using (var proc = Process.GetCurrentProcess())
            _procCpuStart = proc.TotalProcessorTime;
        _gcPauseStart = GC.GetTotalPauseDuration();
        _gen0Start = GC.CollectionCount(0);
        _gen1Start = GC.CollectionCount(1);
        _gen2Start = GC.CollectionCount(2);

        _windowStartTicks = Stopwatch.GetTimestamp();
        _lastRenderTicks = 0;
        _measuring = true;

        CompositionTarget.Rendering += OnRendering;

        _sampleTimer = new DispatcherTimer(DispatcherPriority.Send, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _sampleTimer.Tick += OnSampleTick;
        _sampleTimer.Start();
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (!_measuring) return;
        long now = Stopwatch.GetTimestamp();
        if (_lastRenderTicks != 0)
        {
            double ms = (now - _lastRenderTicks) * 1000.0 / Stopwatch.Frequency;
            _frameDeltas.Add(ms);
        }
        _lastRenderTicks = now;

        if (_realizedCounter != null)
        {
            try
            {
                int c = _realizedCounter();
                if (c > _maxRealizedContainers) _maxRealizedContainers = c;
            }
            catch { }
        }
    }

    private static void OnSampleTick(object? sender, EventArgs e)
    {
        if (!_measuring || _uiDispatcher == null) return;

        // Sample ThreadPool saturation
        _tpPending.Add((int)Math.Min(int.MaxValue, ThreadPool.PendingWorkItemCount));
        ThreadPool.GetMaxThreads(out int maxW, out _);
        ThreadPool.GetAvailableThreads(out int availW, out _);
        _tpBusy.Add(Math.Max(0, maxW - availW));

        // Probe Dispatcher queue wait latency
        long posted = Stopwatch.GetTimestamp();
        _uiDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            if (!_measuring) return;
            double waitMs = (Stopwatch.GetTimestamp() - posted) * 1000.0 / Stopwatch.Frequency;
            _dispatcherWaits.Add(waitMs);
        }));
    }

    public static string EndWindow(string tabName)
    {
        _measuring = false;
        CompositionTarget.Rendering -= OnRendering;
        if (_sampleTimer != null)
        {
            _sampleTimer.Stop();
            _sampleTimer.Tick -= OnSampleTick;
            _sampleTimer = null;
        }

        double elapsedMs = (Stopwatch.GetTimestamp() - _windowStartTicks) * 1000.0 / Stopwatch.Frequency;
        double elapsedSec = Math.Max(0.001, elapsedMs / 1000.0);

        TimeSpan uiCpuEnd = GetThreadCpuTime(_uiOsThreadId);
        TimeSpan procCpuEnd;
        using (var proc = Process.GetCurrentProcess())
            procCpuEnd = proc.TotalProcessorTime;
        TimeSpan gcPauseEnd = GC.GetTotalPauseDuration();

        double uiCpuMs = Math.Max(0, (uiCpuEnd - _uiCpuStart).TotalMilliseconds);
        double procCpuMs = Math.Max(0, (procCpuEnd - _procCpuStart).TotalMilliseconds);
        double workerCpuMs = Math.Max(0, procCpuMs - uiCpuMs);
        double gcPauseMs = Math.Max(0, (gcPauseEnd - _gcPauseStart).TotalMilliseconds);
        int g0 = GC.CollectionCount(0) - _gen0Start;
        int g1 = GC.CollectionCount(1) - _gen1Start;
        int g2 = GC.CollectionCount(2) - _gen2Start;

        double avgFps = _frameDeltas.Count / elapsedSec;
        var sortedFrames = _frameDeltas.OrderBy(x => x).ToList();
        double p50 = Percentile(sortedFrames, 0.50);
        double p95 = Percentile(sortedFrames, 0.95);
        double maxFrame = sortedFrames.Count > 0 ? sortedFrames[^1] : 0;
        int jank16 = _frameDeltas.Count(x => x > 20.0);
        int jank33 = _frameDeltas.Count(x => x > 33.3);

        double avgDispWait = _dispatcherWaits.Count > 0 ? _dispatcherWaits.Average() : 0;
        double maxDispWait = _dispatcherWaits.Count > 0 ? _dispatcherWaits.Max() : 0;

        double avgPending = _tpPending.Count > 0 ? _tpPending.Average() : 0;
        int maxPending = _tpPending.Count > 0 ? _tpPending.Max() : 0;
        int maxBusy = _tpBusy.Count > 0 ? _tpBusy.Max() : 0;

        int decCount;
        double avgDecMs, maxDecMs;
        lock (_decodeLock)
        {
            decCount = _decodeTimes.Count;
            avgDecMs = decCount > 0 ? _decodeTimes.Average() : 0;
            maxDecMs = decCount > 0 ? _decodeTimes.Max() : 0;
        }

        double srcUpdatesPerSec = _sourceUpdates / elapsedSec;
        int cores = Environment.ProcessorCount;
        double uiCpuPct = (uiCpuMs / elapsedMs) * 100.0; // % of 1 core
        double totalCpuPct = (procCpuMs / (elapsedMs * cores)) * 100.0;

        string line = $"[DIAG-RESULT] mode={Mode} tab={tabName} elapsedMs={elapsedMs:0} frames={_frameDeltas.Count} fps={avgFps:0.0} p50Ms={p50:0.0} p95Ms={p95:0.0} maxMs={maxFrame:0.0} jank20={jank16} jank33={jank33} | uiCpuMs={uiCpuMs:0} ({uiCpuPct:0.0}%core) workerCpuMs={workerCpuMs:0} totalCpuPct={totalCpuPct:0.0}% | dispWaitAvg={avgDispWait:0.00}ms dispWaitMax={maxDispWait:0.00}ms | tpPendingAvg={avgPending:0.0} tpPendingMax={maxPending} tpBusyMax={maxBusy} | srcUpdates={_sourceUpdates} ({srcUpdatesPerSec:0.0}/s) | decCount={decCount} decAvg={avgDecMs:0.00}ms decMax={maxDecMs:0.00}ms | gc={g0}/{g1}/{g2} gcPause={gcPauseMs:0.0}ms | realizedMax={_maxRealizedContainers}";

        LogHelper.LogInfo(line);
        if (!string.IsNullOrEmpty(OutputPath))
        {
            try { File.AppendAllText(OutputPath, line + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        return line;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        int idx = Math.Clamp((int)Math.Floor(p * (sorted.Count - 1)), 0, sorted.Count - 1);
        return sorted[idx];
    }
}
