using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MusicPlayer.Utilities;

namespace MusicPlayer.Diagnostics;

public enum SidebarCacheMode
{
    All,
    None,
    FixedOnly
}

/// <summary>
/// Temporary, environment-gated instrumentation for the sidebar drawer A/B
/// diagnosis. It is completely inert unless MUSICPLAYER_SIDEBAR_PROBE=1 is set,
/// and it never changes the production path when disabled.
///
/// Environment variables:
///   MUSICPLAYER_SIDEBAR_PROBE=1          enable the probe
///   MUSICPLAYER_SIDEBAR_CASE=A|B|B2|C    label written to the CSV
///   MUSICPLAYER_SIDEBAR_RUN=&lt;id&gt;         label written to the CSV
///   MUSICPLAYER_SIDEBAR_CACHE=all|none|fixed
///                                        all   = current behaviour (all new caches)
///                                        none  = disable every cache added to the sidebar path
///                                        fixed = keep caches on fixed-size layers only
///   MUSICPLAYER_SIDEBAR_GLASS_FREEZE=1   diagnostic only: keep the last valid
///                                        ImageBrush.Viewbox during motion
///
/// Results are appended to &lt;data dir&gt;/Logs/sidebar-probe.csv.
/// </summary>
public static class SidebarPerfProbe
{
    private const string CsvName = "sidebar-probe.csv";
    private const string MetaName = "sidebar-probe-meta.txt";

    private static readonly bool _enabled = ReadBool("MUSICPLAYER_SIDEBAR_PROBE");
    private static readonly bool _freezeGlass = ReadBool("MUSICPLAYER_SIDEBAR_GLASS_FREEZE");
    private static readonly SidebarCacheMode _cacheMode = ReadCacheMode();

    public static bool Enabled => _enabled;
    public static bool IsMotionActive => _enabled && _active && !_motionEnded;
    public static bool FreezeGlassViewboxDuringMotion => _enabled && _freezeGlass;
    public static SidebarCacheMode CacheMode => _enabled ? _cacheMode : SidebarCacheMode.All;
    public static string CaseName => Environment.GetEnvironmentVariable("MUSICPLAYER_SIDEBAR_CASE") ?? "A";
    public static string RunId => Environment.GetEnvironmentVariable("MUSICPLAYER_SIDEBAR_RUN") ?? "1";

    private static bool _active;
    private static bool _motionEnded;
    private static bool _metaWritten;
    private static int _seq;

    private static long _startTicks;
    private static long _motionEndTicks;
    private static long _uiCpuStartTicks;
    private static long _lastRenderTicks;
    private static readonly List<double> _frameDeltas = new(64);
    private static int _renderCallbacks;

    private static FrameworkElement? _sidebar;
    private static FrameworkElement? _header;
    private static FrameworkElement? _content;
    private static ColumnDefinition? _column;

    private static double _sidebarStartWidth, _sidebarEndWidth, _sidebarMinWidth, _sidebarMaxWidth, _sidebarDpWidth;
    private static double _contentStartWidth, _contentEndWidth, _contentMinWidth, _contentMaxWidth;
    private static double _headerStartWidth, _headerEndWidth;
    private static double _columnStartWidth, _columnEndWidth;
    private static double _lastSidebarWidth = double.NaN;
    private static double _lastContentWidth = double.NaN;
    private static double _lastHeaderWidth = double.NaN;
    private static double _lastColumnWidth = double.NaN;
    private static int _sidebarWidthChanges, _contentWidthChanges, _headerWidthChanges, _columnWidthChanges;

    private static int _glassCalls, _glassSidebarCalls, _glassHeaderCalls, _glassOtherCalls;
    private static long _glassTotalTicks, _glassMaxTicks;
    private static long _glassTransformTicks, _glassViewboxTicks, _glassImageTicks, _glassSetupTicks;
    private static int _surfaceUpdates;
    private static long _surfaceTotalTicks, _surfaceMaxTicks;
    private static int _viewboxSets, _viewboxChanges, _viewboxSkips, _imageSourceAssigns;

    private static int _rebuildEntries, _rebuildActual, _rebuildSkipped;
    private static long _rebuildWorkerTicks, _rebuildApplyTicks;
    private static int _renderTargetPasses;
    private static int _decodeOps;

    private static long _finalGlassTicks, _titleBarTicks, _cacheApplyTicks, _cacheReleaseTicks;

    private static int _layoutUpdated, _contentSizeChanged;
    private static int _contentMeasure, _contentArrange, _contentRender;
    private static long _contentMeasureTicks, _contentArrangeTicks, _contentRenderTicks;
    private static long _contentMeasureMaxTicks, _contentArrangeMaxTicks;

    private static int _clipConvertCalls;
    private static long _clipConvertTicks, _clipConvertMaxTicks;

    private static int _insetCalls;
    private static long _insetTicks, _insetMaxTicks;

    private static double _privateStartMb, _privateEndMb, _wsStartMb, _wsEndMb, _gcStartMb, _gcEndMb;
    private static string _view = "?";
    private static bool _open;

    private static readonly object _fileLock = new();

    // ── toggle lifecycle ────────────────────────────────────────────────────

    public static void BeginToggle(bool open, string view, FrameworkElement sidebar,
        FrameworkElement? header, FrameworkElement? content, ColumnDefinition? column)
    {
        if (!_enabled) return;
        if (_active) EndToggle("superseded");

        _active = true;
        _motionEnded = false;
        _open = open;
        _view = view ?? "?";
        _seq++;
        _startTicks = Stopwatch.GetTimestamp();
        _motionEndTicks = 0;
        _uiCpuStartTicks = GetUiThreadCpuTicks();
        _lastRenderTicks = 0;
        _renderCallbacks = 0;
        _frameDeltas.Clear();

        _sidebar = sidebar;
        _header = header;
        _content = content;
        _column = column;

        _sidebarStartWidth = _sidebarEndWidth = _sidebarMinWidth = _sidebarMaxWidth = SafeWidth(sidebar);
        _contentStartWidth = _contentEndWidth = _contentMinWidth = _contentMaxWidth = SafeWidth(content);
        _headerStartWidth = _headerEndWidth = SafeWidth(header);
        _columnStartWidth = _columnEndWidth = column?.ActualWidth ?? 0;
        _lastSidebarWidth = _sidebarStartWidth;
        _lastContentWidth = _contentStartWidth;
        _lastHeaderWidth = _headerStartWidth;
        _lastColumnWidth = _columnStartWidth;
        _sidebarWidthChanges = _contentWidthChanges = _headerWidthChanges = _columnWidthChanges = 0;

        _glassCalls = _glassSidebarCalls = _glassHeaderCalls = _glassOtherCalls = 0;
        _glassTotalTicks = _glassMaxTicks = 0;
        _glassTransformTicks = _glassViewboxTicks = _glassImageTicks = _glassSetupTicks = 0;
        _surfaceUpdates = 0;
        _surfaceTotalTicks = _surfaceMaxTicks = 0;
        _viewboxSets = _viewboxChanges = _viewboxSkips = _imageSourceAssigns = 0;

        _rebuildEntries = _rebuildActual = _rebuildSkipped = 0;
        _rebuildWorkerTicks = _rebuildApplyTicks = 0;
        _renderTargetPasses = 0;
        _decodeOps = 0;

        _finalGlassTicks = _titleBarTicks = _cacheApplyTicks = _cacheReleaseTicks = 0;
        _layoutUpdated = _contentSizeChanged = 0;
        _contentMeasure = _contentArrange = _contentRender = 0;
        _contentMeasureTicks = _contentArrangeTicks = _contentRenderTicks = 0;
        _contentMeasureMaxTicks = _contentArrangeMaxTicks = 0;
        _clipConvertCalls = 0;
        _clipConvertTicks = _clipConvertMaxTicks = 0;
        _insetCalls = 0;
        _insetTicks = _insetMaxTicks = 0;

        var mem = ReadMemory();
        _privateStartMb = mem.privateMb;
        _wsStartMb = mem.workingSetMb;
        _gcStartMb = mem.gcMb;

        if (content != null)
        {
            content.SizeChanged += OnContentSizeChanged;
            content.LayoutUpdated += OnContentLayoutUpdated;
        }
        CompositionTarget.Rendering += OnRendering;

        WriteMetaOnce();
    }

    /// <summary>Stops the motion window (frame sampling + freeze) but keeps the
    /// per-toggle counters alive for the final brush/title-bar work.</summary>
    public static void EndMotion()
    {
        if (!_enabled || !_active || _motionEnded) return;
        _motionEnded = true;
        _motionEndTicks = Stopwatch.GetTimestamp();
        UpdateWidths();
        CompositionTarget.Rendering -= OnRendering;
    }

    public static void EndToggle(string reason)
    {
        if (!_enabled || !_active) return;
        if (!_motionEnded) EndMotion();

        _active = false;
        if (_content != null)
        {
            _content.SizeChanged -= OnContentSizeChanged;
            _content.LayoutUpdated -= OnContentLayoutUpdated;
        }
        CompositionTarget.Rendering -= OnRendering;
        try { Application.Current?.MainWindow?.UpdateLayout(); } catch { }
        UpdateWidths();
        _active = false;

        long endTicks = Stopwatch.GetTimestamp();
        long uiCpuEndTicks = GetUiThreadCpuTicks();
        var mem = ReadMemory();
        _privateEndMb = mem.privateMb;
        _wsEndMb = mem.workingSetMb;
        _gcEndMb = mem.gcMb;

        double durationMs = TicksToMs((_motionEndTicks != 0 ? _motionEndTicks : endTicks) - _startTicks);
        double uiCpuMs = (_uiCpuStartTicks > 0 && uiCpuEndTicks >= _uiCpuStartTicks)
            ? (uiCpuEndTicks - _uiCpuStartTicks) / 10000.0
            : 0;
        var frames = ComputeFrameStats();

        try
        {
            WriteCsv(reason, durationMs, uiCpuMs, frames);
        }
        catch (Exception ex)
        {
            LogHelper.LogError("[SidebarProbe] CSV write failed", ex);
        }

        _sidebar = null;
        _header = null;
        _content = null;
        _column = null;
    }

    // ── glass pipeline ──────────────────────────────────────────────────────

    public static long BeginGlassHandler(object sender)
    {
        if (!_enabled || !_active) return 0;
        _glassCalls++;
        if (ReferenceEquals(sender, _sidebar)) _glassSidebarCalls++;
        else if (ReferenceEquals(sender, _header)) _glassHeaderCalls++;
        else _glassOtherCalls++;
        return Stopwatch.GetTimestamp();
    }

    public static void EndGlassHandler(long startTicks)
    {
        if (startTicks == 0) return;
        long dt = Stopwatch.GetTimestamp() - startTicks;
        _glassTotalTicks += dt;
        if (dt > _glassMaxTicks) _glassMaxTicks = dt;
    }

    public static long BeginSurfaceUpdate(string surface)
    {
        if (!_enabled || !_active) return 0;
        _surfaceUpdates++;
        return Stopwatch.GetTimestamp();
    }

    public static void EndSurfaceUpdate(long startTicks, long transformTicks, long viewboxTicks,
        long imageTicks, long setupTicks, bool viewboxChanged, bool viewboxSet, bool imageAssigned)
    {
        if (startTicks == 0) return;
        long dt = Stopwatch.GetTimestamp() - startTicks;
        _surfaceTotalTicks += dt;
        if (dt > _surfaceMaxTicks) _surfaceMaxTicks = dt;
        _glassTransformTicks += transformTicks;
        _glassViewboxTicks += viewboxTicks;
        _glassImageTicks += imageTicks;
        _glassSetupTicks += setupTicks;
        if (viewboxSet) _viewboxSets++;
        if (viewboxChanged) _viewboxChanges++;
        if (imageAssigned) _imageSourceAssigns++;
    }

    public static void RecordViewboxSkipped()
    {
        if (_enabled && _active) _viewboxSkips++;
    }

    public static long BeginGlassRebuild()
    {
        if (!_enabled || !_active) return 0;
        _rebuildEntries++;
        return Stopwatch.GetTimestamp();
    }

    public static void EndGlassRebuild(long startTicks, bool actual, bool skipped)
    {
        if (startTicks == 0) return;
        if (actual) _rebuildActual++;
        if (skipped) _rebuildSkipped++;
    }

    public static long BeginGlassRebuildWorker()
    {
        return _enabled && _active ? Stopwatch.GetTimestamp() : 0;
    }

    public static void EndGlassRebuildWorker(long startTicks)
    {
        if (startTicks == 0) return;
        Interlocked.Add(ref _rebuildWorkerTicks, Stopwatch.GetTimestamp() - startTicks);
    }

    public static long BeginGlassRebuildApply()
    {
        return _enabled && _active ? Stopwatch.GetTimestamp() : 0;
    }

    public static void EndGlassRebuildApply(long startTicks)
    {
        if (startTicks == 0) return;
        Interlocked.Add(ref _rebuildApplyTicks, Stopwatch.GetTimestamp() - startTicks);
    }

    public static void RecordRenderTargetPasses(int count)
    {
        if (!_enabled || !_active) return;
        Interlocked.Add(ref _renderTargetPasses, count);
    }

    public static void RecordDecode()
    {
        if (!_enabled || !_active) return;
        Interlocked.Increment(ref _decodeOps);
    }

    public static long BeginCacheApply() => _enabled && _active ? Stopwatch.GetTimestamp() : 0;
    public static void EndCacheApply(long startTicks) { if (startTicks != 0) _cacheApplyTicks = Stopwatch.GetTimestamp() - startTicks; }

    public static long BeginCacheRelease() => _enabled && _active ? Stopwatch.GetTimestamp() : 0;
    public static void EndCacheRelease(long startTicks) { if (startTicks != 0) _cacheReleaseTicks = Stopwatch.GetTimestamp() - startTicks; }

    public static long BeginFinalGlassUpdate() => _enabled && _active ? Stopwatch.GetTimestamp() : 0;
    public static void EndFinalGlassUpdate(long startTicks) { if (startTicks != 0) _finalGlassTicks = Stopwatch.GetTimestamp() - startTicks; }

    public static long BeginTitleBarUpdate() => _enabled && _active ? Stopwatch.GetTimestamp() : 0;
    public static void EndTitleBarUpdate(long startTicks) { if (startTicks != 0) _titleBarTicks = Stopwatch.GetTimestamp() - startTicks; }

    // ── clip converter ──────────────────────────────────────────────────────

    public static long BeginClipConvert()
    {
        if (!_enabled || !_active) return 0;
        _clipConvertCalls++;
        return Stopwatch.GetTimestamp();
    }

    public static void EndClipConvert(long startTicks)
    {
        if (startTicks == 0) return;
        long dt = Stopwatch.GetTimestamp() - startTicks;
        _clipConvertTicks += dt;
        if (dt > _clipConvertMaxTicks) _clipConvertMaxTicks = dt;
    }

    // ── header/viewport sync (MainWindow.Columns.cs) ─────────────────────────

    public static long BeginInsetSync()
    {
        if (!_enabled || !_active) return 0;
        _insetCalls++;
        return Stopwatch.GetTimestamp();
    }

    public static void EndInsetSync(long startTicks)
    {
        if (startTicks == 0) return;
        long dt = Stopwatch.GetTimestamp() - startTicks;
        _insetTicks += dt;
        if (dt > _insetMaxTicks) _insetMaxTicks = dt;
    }

    // ── content grid (InstrumentedGrid) ─────────────────────────────────────

    public static void RecordContentMeasure(long ticks)
    {
        if (!_enabled || !_active) return;
        _contentMeasure++;
        _contentMeasureTicks += ticks;
        if (ticks > _contentMeasureMaxTicks) _contentMeasureMaxTicks = ticks;
    }

    public static void RecordContentArrange(long ticks)
    {
        if (!_enabled || !_active) return;
        _contentArrange++;
        _contentArrangeTicks += ticks;
        if (ticks > _contentArrangeMaxTicks) _contentArrangeMaxTicks = ticks;
    }

    public static void RecordContentRender(long ticks)
    {
        if (!_enabled || !_active) return;
        _contentRender++;
        _contentRenderTicks += ticks;
    }

    // ── frame / layout subscriptions ────────────────────────────────────────

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (!_enabled || !_active || _motionEnded) return;
        long now = Stopwatch.GetTimestamp();
        if (_lastRenderTicks != 0)
        {
            double ms = TicksToMs(now - _lastRenderTicks);
            if (ms >= 0.5) _frameDeltas.Add(ms);
        }
        _lastRenderTicks = now;
        _renderCallbacks++;
        UpdateWidths();
    }

    private static void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_enabled && _active) _contentSizeChanged++;
    }

    private static void OnContentLayoutUpdated(object? sender, EventArgs e)
    {
        if (_enabled && _active) _layoutUpdated++;
    }

    private static void UpdateWidths()
    {
        if (!_active) return;
        if (_sidebar != null)
        {
            double w = SafeWidth(_sidebar);
            _sidebarDpWidth = _sidebar.Width;
            TrackWidth(ref _lastSidebarWidth, ref _sidebarMinWidth, ref _sidebarMaxWidth, ref _sidebarWidthChanges, w);
            _sidebarEndWidth = w;
        }
        if (_content != null)
        {
            double w = SafeWidth(_content);
            TrackWidth(ref _lastContentWidth, ref _contentMinWidth, ref _contentMaxWidth, ref _contentWidthChanges, w);
            _contentEndWidth = w;
        }
        if (_header != null)
        {
            double w = SafeWidth(_header);
            TrackWidth(ref _lastHeaderWidth, ref _headerWidthChanges, w);
            _headerEndWidth = w;
        }
        if (_column != null)
        {
            double w = _column.ActualWidth;
            TrackWidth(ref _lastColumnWidth, ref _columnWidthChanges, w);
            _columnEndWidth = w;
        }
    }

    private static void TrackWidth(ref double last, ref double min, ref double max, ref int changes, double value)
    {
        if (double.IsNaN(last))
        {
            last = value;
            min = value;
            max = value;
            return;
        }
        if (Math.Abs(value - last) >= 0.25)
        {
            changes++;
            last = value;
        }
        if (!double.IsNaN(min) && value < min) min = value;
        if (!double.IsNaN(max) && value > max) max = value;
    }

    private static void TrackWidth(ref double last, ref int changes, double value)
    {
        if (double.IsNaN(last))
        {
            last = value;
            return;
        }
        if (Math.Abs(value - last) >= 0.25)
        {
            changes++;
            last = value;
        }
    }

    // ── CSV ─────────────────────────────────────────────────────────────────

    private static void WriteCsv(string reason, double durationMs, double uiCpuMs, FrameStats stats)
    {
        string path = Path.Combine(AppPaths.LogsPath, CsvName);
        var sb = new StringBuilder(2048);
        sb.Append(Csv(CaseName)).Append(',')
          .Append(Csv(RunId)).Append(',')
          .Append(_seq).Append(',')
          .Append(Csv(reason)).Append(',')
          .Append(Csv(_view)).Append(',')
          .Append(_open ? 1 : 0).Append(',')
          .Append(F(durationMs)).Append(',')
          .Append(F(uiCpuMs)).Append(',')
          .Append(_renderCallbacks).Append(',')
          .Append(stats.Samples).Append(',')
          .Append(F(stats.Median)).Append(',')
          .Append(F(stats.P95)).Append(',')
          .Append(F(stats.Max)).Append(',')
          .Append(stats.Gt33).Append(',')
          .Append(stats.Gt50).Append(',')
          .Append(stats.Gt100).Append(',')
          .Append(_glassCalls).Append(',')
          .Append(_glassSidebarCalls).Append(',')
          .Append(_glassHeaderCalls).Append(',')
          .Append(F(TicksToMs(_glassTotalTicks))).Append(',')
          .Append(F(_glassCalls > 0 ? TicksToMs(_glassTotalTicks) / _glassCalls : 0)).Append(',')
          .Append(F(TicksToMs(_glassMaxTicks))).Append(',')
          .Append(_viewboxSets).Append(',')
          .Append(_viewboxChanges).Append(',')
          .Append(_viewboxSkips).Append(',')
          .Append(F(TicksToMs(_glassViewboxTicks))).Append(',')
          .Append(F(TicksToMs(_glassTransformTicks))).Append(',')
          .Append(F(TicksToMs(_glassImageTicks))).Append(',')
          .Append(_imageSourceAssigns).Append(',')
          .Append(_surfaceUpdates).Append(',')
          .Append(F(_surfaceUpdates > 0 ? TicksToMs(_surfaceTotalTicks) / _surfaceUpdates : 0)).Append(',')
          .Append(F(TicksToMs(_surfaceMaxTicks))).Append(',')
          .Append(_rebuildEntries).Append(',')
          .Append(_rebuildActual).Append(',')
          .Append(_rebuildSkipped).Append(',')
          .Append(F(TicksToMs(_rebuildWorkerTicks))).Append(',')
          .Append(F(TicksToMs(_rebuildApplyTicks))).Append(',')
          .Append(_renderTargetPasses).Append(',')
          .Append(_decodeOps).Append(',')
          .Append(_renderTargetPasses).Append(',')
          .Append(_layoutUpdated).Append(',')
          .Append(_contentSizeChanged).Append(',')
          .Append(_contentMeasure).Append(',')
          .Append(_contentArrange).Append(',')
          .Append(_contentRender).Append(',')
          .Append(F(TicksToMs(_contentMeasureTicks))).Append(',')
          .Append(F(TicksToMs(_contentMeasureMaxTicks))).Append(',')
          .Append(F(TicksToMs(_contentArrangeTicks))).Append(',')
          .Append(F(TicksToMs(_contentArrangeMaxTicks))).Append(',')
          .Append(F(TicksToMs(_contentRenderTicks))).Append(',')
          .Append(_clipConvertCalls).Append(',')
          .Append(F(TicksToMs(_clipConvertTicks))).Append(',')
          .Append(F(TicksToMs(_clipConvertMaxTicks))).Append(',')
          .Append(_insetCalls).Append(',')
          .Append(F(TicksToMs(_insetTicks))).Append(',')
          .Append(F(TicksToMs(_insetMaxTicks))).Append(',')
          .Append(F(TicksToMs(_cacheApplyTicks))).Append(',')
          .Append(F(TicksToMs(_cacheReleaseTicks))).Append(',')
          .Append(F(TicksToMs(_finalGlassTicks))).Append(',')
          .Append(F(TicksToMs(_titleBarTicks))).Append(',')
          .Append(F(_sidebarStartWidth)).Append(',')
          .Append(F(_sidebarEndWidth)).Append(',')
          .Append(F(_sidebarMinWidth)).Append(',')
          .Append(F(_sidebarMaxWidth)).Append(',')
          .Append(_sidebarWidthChanges).Append(',')
          .Append(F(_sidebarDpWidth)).Append(',')          .Append(F(_contentStartWidth)).Append(',')
          .Append(F(_contentEndWidth)).Append(',')
          .Append(F(_contentMinWidth)).Append(',')
          .Append(F(_contentMaxWidth)).Append(',')
          .Append(_contentWidthChanges).Append(',')
          .Append(F(_headerStartWidth)).Append(',')
          .Append(F(_headerEndWidth)).Append(',')
          .Append(F(_columnStartWidth)).Append(',')
          .Append(F(_columnEndWidth)).Append(',')
          .Append(_columnWidthChanges).Append(',')
          .Append(F(_privateStartMb)).Append(',')
          .Append(F(_privateEndMb)).Append(',')
          .Append(F(_privateEndMb - _privateStartMb)).Append(',')
          .Append(F(_wsStartMb)).Append(',')
          .Append(F(_wsEndMb)).Append(',')
          .Append(F(_gcStartMb)).Append(',')
          .Append(F(_gcEndMb)).Append(',')
          .Append(Csv(CacheMode.ToString())).Append(',')
          .Append(FreezeGlassViewboxDuringMotion ? 1 : 0).Append(',')
          .Append(Csv(reason))
          .AppendLine();

        lock (_fileLock)
        {
            bool writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            if (writeHeader) writer.WriteLine(CsvHeader);
            writer.Write(sb.ToString());
        }
    }

    private static void WriteMetaOnce()
    {
        if (_metaWritten) return;
        _metaWritten = true;
        try
        {
            string path = Path.Combine(AppPaths.LogsPath, MetaName);
            var sb = new StringBuilder();
            sb.AppendLine($"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} case={CaseName} run={RunId} ---");
            sb.AppendLine($"cacheMode={CacheMode} freezeGlass={FreezeGlassViewboxDuringMotion}");
            sb.AppendLine($"exe={Environment.ProcessPath}");
            sb.AppendLine($"os={Environment.OSVersion} arch={RuntimeInformation.ProcessArchitecture} cpus={Environment.ProcessorCount}");
            sb.AppendLine($"dotnet={Environment.Version} stopwatchHz={Stopwatch.Frequency}");
            var win = Application.Current?.MainWindow;
            if (win != null)
            {
                try
                {
                    sb.AppendLine($"renderTier={RenderCapability.Tier >> 16} dpi={VisualTreeHelper.GetDpi(win).DpiScaleX:0.###}");
                }
                catch { }
                sb.AppendLine($"window={win.ActualWidth:0.#}x{win.ActualHeight:0.#} state={win.WindowState}");
            }
            lock (_fileLock)
            {
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
        }
        catch { }
    }

    private const string CsvHeader =
        "case,run,seq,reason,view,open,durationMs,uiCpuMs,frameCallbacks,frameSamples,medianFrameMs,p95FrameMs,maxFrameMs,framesGt33,framesGt50,framesGt100," +
        "glassCalls,glassSidebar,glassHeader,glassTotalMs,glassAvgMs,glassMaxMs,viewboxSets,viewboxChanges,viewboxSkips,viewboxMs,transformMs,imageSourceMs,imageSourceAssigns," +
        "surfaceUpdates,surfaceAvgMs,surfaceMaxMs,rebuildEntries,rebuildActual,rebuildSkipped,rebuildWorkerMs,rebuildApplyMs,renderTargetPasses,decodeOps,pixelCopyOps," +
        "layoutUpdated,contentSizeChanged,contentMeasure,contentArrange,contentRender,contentMeasureMs,contentMeasureMaxMs,contentArrangeMs,contentArrangeMaxMs,contentRenderMs," +
        "clipConvertCalls,clipConvertMs,clipConvertMaxMs,insetSyncCalls,insetSyncMs,insetSyncMaxMs,cacheApplyMs,cacheReleaseMs,finalGlassMs,titleBarMs," +
        "sidebarStartWidth,sidebarEndWidth,sidebarMinWidth,sidebarMaxWidth,sidebarWidthChanges,sidebarDpWidth," +
        "contentStartWidth,contentEndWidth,contentMinWidth,contentMaxWidth,contentWidthChanges," +
        "headerStartWidth,headerEndWidth,columnStartWidth,columnEndWidth,columnWidthChanges," +
        "privateStartMB,privateEndMB,privateDeltaMB,workingSetStartMB,workingSetEndMB,gcStartMB,gcEndMB,cacheMode,freezeGlass,notes";

    // ── helpers ─────────────────────────────────────────────────────────────

    private readonly struct FrameStats
    {
        public readonly int Samples;
        public readonly double Median;
        public readonly double P95;
        public readonly double Max;
        public readonly int Gt33;
        public readonly int Gt50;
        public readonly int Gt100;

        public FrameStats(int samples, double median, double p95, double max, int gt33, int gt50, int gt100)
        {
            Samples = samples; Median = median; P95 = p95; Max = max;
            Gt33 = gt33; Gt50 = gt50; Gt100 = gt100;
        }
    }

    private static FrameStats ComputeFrameStats()
    {
        if (_frameDeltas.Count == 0) return new FrameStats(0, 0, 0, 0, 0, 0, 0);
        var sorted = _frameDeltas.ToArray();
        Array.Sort(sorted);
        int n = sorted.Length;
        double median = (n & 1) == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        int idx95 = Math.Min(n - 1, Math.Max(0, (int)Math.Ceiling(n * 0.95) - 1));
        int gt33 = 0, gt50 = 0, gt100 = 0;
        foreach (var ms in sorted)
        {
            if (ms > 33.33) gt33++;
            if (ms > 50.0) gt50++;
            if (ms > 100.0) gt100++;
        }
        return new FrameStats(n, median, sorted[idx95], sorted[n - 1], gt33, gt50, gt100);
    }

    private static double SafeWidth(FrameworkElement? element)
    {
        if (element == null) return 0;
        double w = element.ActualWidth;
        return double.IsNaN(w) || double.IsInfinity(w) ? 0 : w;
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static string F(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? "0"
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static bool ReadBool(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static SidebarCacheMode ReadCacheMode()
    {
        var v = Environment.GetEnvironmentVariable("MUSICPLAYER_SIDEBAR_CACHE");
        if (string.IsNullOrWhiteSpace(v)) return SidebarCacheMode.All;
        if (string.Equals(v, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "off", StringComparison.OrdinalIgnoreCase) || v == "0")
            return SidebarCacheMode.None;
        if (string.Equals(v, "fixed", StringComparison.OrdinalIgnoreCase))
            return SidebarCacheMode.FixedOnly;
        return SidebarCacheMode.All;
    }

    private static (double privateMb, double workingSetMb, double gcMb) ReadMemory()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            p.Refresh();
            return (p.PrivateMemorySize64 / 1048576.0, p.WorkingSet64 / 1048576.0, GC.GetTotalMemory(false) / 1048576.0);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private const uint THREAD_QUERY_INFORMATION = 0x0040;
    private const uint THREAD_QUERY_LIMITED_INFORMATION = 0x0800;

    private static long GetUiThreadCpuTicks()
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenThread(THREAD_QUERY_INFORMATION | THREAD_QUERY_LIMITED_INFORMATION, false, GetCurrentThreadId());
            if (handle == IntPtr.Zero) return 0;
            return GetThreadTimes(handle, out _, out _, out long kernel, out long user)
                ? kernel + user
                : 0;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }
}
