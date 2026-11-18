using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using MusicPlayer.Views;

namespace MusicPlayer.Diagnostics;

/// <summary>
/// TEMPORARY, environment-gated measurement instrumentation for the M2.5
/// container-scroll investigation.
///
/// It is inert unless MUSICPLAYER_SCROLLPROBE=1, and even then it only observes:
/// it never handles a wheel event, never changes a scroll offset, never touches
/// layout/virtualization settings and never writes to the app's normal log.
///
/// What it records per scroll burst (a burst ends 750 ms after the last scroll change):
///   - wheel and ScrollChanged event counts and activity window
///   - rendering callback count and gaps (these are composition callbacks, NOT
///     presented frames)
///   - layout pass count and maximum gap between layout passes
///   - maximum input-to-idle latency (wheel time -> Dispatcher Background work)
///   - active ScrollViewer identity, offset/extent/viewport before and after
///   - realized container count and visible container count
///   - total items in the active ItemsControl
///   - window state, client dimensions, DPI, and active ItemsPanel type
///
/// Output: one CSV line appended to MUSICPLAYER_SCROLLPROBE_OUT per burst.
/// </summary>
internal static class ScrollProbe
{
    private const string EnabledVariable = "MUSICPLAYER_SCROLLPROBE";
    private const string OutputVariable = "MUSICPLAYER_SCROLLPROBE_OUT";
    private const int IdleMs = 750;

    private static bool _enabled;
    private static string? _outputPath;
    private static string? _frameOutputPath;
    private static MainWindow? _window;
    private static HwndSource? _source;
    private static DispatcherTimer? _idleTimer;
    private static readonly ScrollChangedEventHandler ScrollChangedHandler = OnScrollChanged;

    private static readonly List<double> RenderTimes = new(512);
    private static readonly List<double> RenderOffsets = new(512);
    private static readonly List<double> LayoutTimes = new(512);
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    private static bool _burstActive;
    private static bool _hasStartSnapshot;
    private static int _wheelCount;
    private static int _scrollChangeCount;
    private static DateTime _burstStartUtc;
    private static DateTime _lastActivityUtc;
    private static double _burstStartMs;
    private static double _lastActivityMs;
    private static double _maxIdleLatencyMs;
    private static ScrollViewer? _activeScroll;
    private static double _startOffset, _startExtent, _startViewport;

    public static void Attach(MainWindow window)
    {
        try
        {
            if (!string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "1", StringComparison.Ordinal))
                return;
            string? path = Environment.GetEnvironmentVariable(OutputVariable);
            if (string.IsNullOrWhiteSpace(path)) return;

            string? dir = Path.GetDirectoryName(path);
            _enabled = true;
            _outputPath = path;
            _frameOutputPath = Path.Combine(dir ?? string.Empty, "scroll-frames.csv");
            _window = window;
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(path,
                "startUtc,lastActivityUtc,endUtc,windowState,clientWidthPx,clientHeightPx,view,scrollId,itemsControl,itemsPanel," +
                "wheels,scrollChanges,activityMs,captureMs,renderCallbacks,renderCallbackHz,maxRenderGapMs,p95RenderGapMs," +
                "layoutUpdatedEvents,maxLayoutGapMs,maxIdleLatencyMs,startOffset,startExtent,startViewport," +
                "endOffset,endExtent,endViewport,realizedContainers,visibleContainers,totalItems,dpiScale,presentedFps" +
                Environment.NewLine,
                Encoding.UTF8);
            File.AppendAllText(_frameOutputPath,
                "burstStartUtc,callbackUtc,view,scrollId,callbackIndex,elapsedMs,gapMs,verticalOffset" +
                Environment.NewLine,
                Encoding.UTF8);

            window.AddHandler(ScrollViewer.ScrollChangedEvent, ScrollChangedHandler, true);
            window.SourceInitialized += (_, _) =>
            {
                try
                {
                    IntPtr handle = new WindowInteropHelper(window).Handle;
                    _source = HwndSource.FromHwnd(handle);
                    _source?.AddHook(WndProc);
                }
                catch { }
            };
            window.Closed += (_, _) => Detach();
            CompositionTarget.Rendering += OnRendering;
            window.LayoutUpdated += OnLayoutUpdated;
            _idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(IdleMs) };
            _idleTimer.Tick += (_, _) => FinishBurst();
        }
        catch
        {
            // Instrumentation must never affect the app.
        }
    }

    private static void Detach()
    {
        try
        {
            if (_window != null)
            {
                _window.LayoutUpdated -= OnLayoutUpdated;
                _window.RemoveHandler(ScrollViewer.ScrollChangedEvent, ScrollChangedHandler);
            }
            try { _source?.RemoveHook(WndProc); } catch { }
            _source = null;
            CompositionTarget.Rendering -= OnRendering;
            _idleTimer?.Stop();
            _idleTimer = null;
            _enabled = false;
        }
        catch { }
    }

    private const int WmMouseWheel = 0x020A;

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseWheel) OnWheel();
        return IntPtr.Zero;
    }

    private static void OnWheel()
    {
        try
        {
            double now = Clock.Elapsed.TotalMilliseconds;
            if (!_burstActive) StartBurst(FindActiveScroll());
            _wheelCount++;
            MarkActivity(now);
            long posted = Clock.ElapsedTicks;
            _window?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                double latency = (Clock.ElapsedTicks - posted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (latency > _maxIdleLatencyMs) _maxIdleLatencyMs = latency;
            }));
            RestartIdleTimer();
        }
        catch { }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_enabled || (e.VerticalChange == 0 && e.HorizontalChange == 0)) return;
        try
        {
            ScrollViewer? scroll = FindAncestor<ScrollViewer>(e.OriginalSource as DependencyObject)
                ?? e.Source as ScrollViewer;
            if (scroll == null) return;

            bool started = !_burstActive;
            if (started) StartBurst(scroll);
            _activeScroll = scroll;
            if (started || !_hasStartSnapshot)
            {
                _startOffset = scroll.VerticalOffset - e.VerticalChange;
                _startExtent = scroll.ExtentHeight - e.ExtentHeightChange;
                _startViewport = scroll.ViewportHeight - e.ViewportHeightChange;
                _hasStartSnapshot = true;
            }

            _scrollChangeCount++;
            MarkActivity(Clock.Elapsed.TotalMilliseconds);
            RestartIdleTimer();
        }
        catch { }
    }

    private static void StartBurst(ScrollViewer? scroll)
    {
        _burstActive = true;
        _burstStartUtc = DateTime.UtcNow;
        _lastActivityUtc = _burstStartUtc;
        _burstStartMs = Clock.Elapsed.TotalMilliseconds;
        _lastActivityMs = _burstStartMs;
        _wheelCount = 0;
        _scrollChangeCount = 0;
        RenderTimes.Clear();
        RenderOffsets.Clear();
        LayoutTimes.Clear();
        _maxIdleLatencyMs = 0;
        _activeScroll = scroll ?? FindActiveScroll();
        _hasStartSnapshot = _activeScroll != null;
        if (_activeScroll != null)
        {
            _startOffset = _activeScroll.VerticalOffset;
            _startExtent = _activeScroll.ExtentHeight;
            _startViewport = _activeScroll.ViewportHeight;
        }
    }

    private static void MarkActivity(double now)
    {
        _lastActivityMs = now;
        _lastActivityUtc = DateTime.UtcNow;
    }

    private static void RestartIdleTimer()
    {
        _idleTimer?.Stop();
        _idleTimer?.Start();
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (!_enabled) return;
        try
        {
            double now = Clock.Elapsed.TotalMilliseconds;
            if (_burstActive && now - _lastActivityMs < IdleMs)
            {
                RenderTimes.Add(now);
                RenderOffsets.Add(_activeScroll?.VerticalOffset ?? double.NaN);
            }
        }
        catch { }
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_enabled) return;
        try
        {
            double now = Clock.Elapsed.TotalMilliseconds;
            if (_burstActive && now - _lastActivityMs < IdleMs) LayoutTimes.Add(now);
        }
        catch { }
    }

    private static void FinishBurst()
    {
        try
        {
            _idleTimer?.Stop();
            if (!_enabled || !_burstActive || (_wheelCount == 0 && _scrollChangeCount == 0) ||
                _outputPath == null || _frameOutputPath == null) return;

            double end = Clock.Elapsed.TotalMilliseconds;
            string view = _window?.ViewModel?.CurrentTab ?? "?";
            ScrollViewer? sv = _activeScroll ?? FindActiveScroll();
            double dpi = _window != null ? VisualTreeHelper.GetDpi(_window).DpiScaleX : 0;
            double clientWidth = _window != null ? _window.ActualWidth * VisualTreeHelper.GetDpi(_window).DpiScaleX : 0;
            double clientHeight = _window != null ? _window.ActualHeight * VisualTreeHelper.GetDpi(_window).DpiScaleY : 0;
            string windowState = _window?.WindowState.ToString() ?? "?";

            int realized = 0, visible = 0, total = 0;
            double endOffset = 0, endExtent = 0, endViewport = 0;
            string scrollId = sv?.Name ?? "?";
            string itemsControlName = "?";
            string itemsPanelType = "?";

            if (sv != null)
            {
                endOffset = sv.VerticalOffset;
                endExtent = sv.ExtentHeight;
                endViewport = sv.ViewportHeight;
                ItemsControl? items = FindAncestor<ItemsControl>(sv);
                if (items != null)
                {
                    itemsControlName = string.IsNullOrWhiteSpace(items.Name) ? items.GetType().Name : items.Name;
                    total = items.Items.Count;
                    Panel? panel = FindVisualChild<Panel>(items);
                    if (panel != null)
                    {
                        itemsPanelType = panel.GetType().Name;
                        realized = panel.Children.Count;
                        Rect viewport = new Rect(0, 0, sv.ActualWidth, sv.ActualHeight);
                        foreach (UIElement child in panel.Children)
                        {
                            if (child is not FrameworkElement fe || !fe.IsVisible) continue;
                            try
                            {
                                Point p = fe.TransformToAncestor(sv).Transform(new Point(0, 0));
                                var bounds = new Rect(p, new Size(fe.ActualWidth, fe.ActualHeight));
                                if (bounds.IntersectsWith(viewport)) visible++;
                            }
                            catch { }
                        }
                    }
                }
            }

            var (maxRenderGap, p95RenderGap, renderCount) = GapStats(RenderTimes);
            var (maxLayoutGap, _, layoutCount) = GapStats(LayoutTimes);
            double activityMs = Math.Max(0, _lastActivityMs - _burstStartMs);
            double callbackHz = CallbackRateHz(RenderTimes);

            var sb = new StringBuilder(256);
            sb.Append(_burstStartUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(_lastActivityUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(windowState).Append(',');
            sb.Append(F(clientWidth)).Append(',');
            sb.Append(F(clientHeight)).Append(',');
            sb.Append(view).Append(',');
            sb.Append(scrollId).Append(',');
            sb.Append(itemsControlName).Append(',');
            sb.Append(itemsPanelType).Append(',');
            sb.Append(_wheelCount).Append(',');
            sb.Append(_scrollChangeCount).Append(',');
            sb.Append(F(activityMs)).Append(',');
            sb.Append(F(end - _burstStartMs)).Append(',');
            sb.Append(renderCount).Append(',');
            sb.Append(F(callbackHz)).Append(',');
            sb.Append(F(maxRenderGap)).Append(',');
            sb.Append(F(p95RenderGap)).Append(',');
            sb.Append(layoutCount).Append(',');
            sb.Append(F(maxLayoutGap)).Append(',');
            sb.Append(F(_maxIdleLatencyMs)).Append(',');
            sb.Append(F(_startOffset)).Append(',');
            sb.Append(F(_startExtent)).Append(',');
            sb.Append(F(_startViewport)).Append(',');
            sb.Append(F(endOffset)).Append(',');
            sb.Append(F(endExtent)).Append(',');
            sb.Append(F(endViewport)).Append(',');
            sb.Append(realized).Append(',');
            sb.Append(visible).Append(',');
            sb.Append(total).Append(',');
            sb.Append(dpi.ToString("0.00", CultureInfo.InvariantCulture)).Append(',');
            sb.Append("NA");

            string line = sb.ToString();
            string burstStartUtc = _burstStartUtc.ToString("O", CultureInfo.InvariantCulture);
            string viewForFrames = view.Replace(",", "_");
            string scrollIdForFrames = scrollId.Replace(",", "_");
            var frameLines = new StringBuilder(RenderTimes.Count * 64);
            double previousFrameMs = double.NaN;
            for (int i = 0; i < RenderTimes.Count; i++)
            {
                double frameMs = RenderTimes[i];
                frameLines.Append(burstStartUtc).Append(',')
                    .Append(_burstStartUtc.AddMilliseconds(frameMs - _burstStartMs).ToString("O", CultureInfo.InvariantCulture)).Append(',')
                    .Append(viewForFrames).Append(',')
                    .Append(scrollIdForFrames).Append(',')
                    .Append(i).Append(',')
                    .Append(F(frameMs - _burstStartMs)).Append(',')
                    .Append(double.IsNaN(previousFrameMs) ? string.Empty : F(frameMs - previousFrameMs)).Append(',')
                    .Append(i < RenderOffsets.Count ? F(RenderOffsets[i]) : string.Empty)
                    .Append(Environment.NewLine);
                previousFrameMs = frameMs;
            }
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    File.AppendAllText(_outputPath!, line + Environment.NewLine, Encoding.UTF8);
                    if (frameLines.Length > 0) File.AppendAllText(_frameOutputPath!, frameLines.ToString(), Encoding.UTF8);
                }
                catch { }
            });
        }
        catch { }
        finally
        {
            _burstActive = false;
            _hasStartSnapshot = false;
            _wheelCount = 0;
            _scrollChangeCount = 0;
            RenderTimes.Clear();
            RenderOffsets.Clear();
            LayoutTimes.Clear();
        }
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static (double maxGap, double p95Gap, int count) GapStats(List<double> times)
    {
        double max = 0;
        var gaps = new List<double>(Math.Max(0, times.Count - 1));
        for (int i = 1; i < times.Count; i++)
        {
            double gap = times[i] - times[i - 1];
            if (gap > max) max = gap;
            gaps.Add(gap);
        }
        if (gaps.Count == 0) return (0, 0, times.Count);
        gaps.Sort();
        int p95Index = Math.Min(gaps.Count - 1, (int)Math.Ceiling(gaps.Count * 0.95) - 1);
        return (max, gaps[p95Index], times.Count);
    }

    private static double CallbackRateHz(List<double> times)
    {
        if (times.Count < 2) return 0;
        double durationMs = times[times.Count - 1] - times[0];
        return durationMs > 0 ? (times.Count - 1) * 1000.0 / durationMs : 0;
    }

    private static ScrollViewer? FindActiveScroll()
    {
        if (_window == null) return null;
        return _window.ViewModel?.CurrentTab switch
        {
            "songs" => _window.SongsGridScroller,
            "artists" => _window.ArtistsSearchScroll,
            "albums" => _window.AlbumsScroll,
            "genres" => _window.GenresScroll,
            "folders" => _window.FoldersScroll,
            _ => null
        };
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T typed) return typed;
            DependencyObject? parent = null;
            try { parent = VisualTreeHelper.GetParent(element); } catch { }
            if (parent == null && element is FrameworkElement fe) parent = fe.Parent;
            if (parent == null && element is FrameworkContentElement fce) parent = fce.Parent;
            element = parent;
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            T? nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
}
