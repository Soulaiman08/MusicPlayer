using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using MusicPlayer.Lyrics;
using MusicPlayer.Models;
using MusicPlayer.Services;
using MusicPlayer.Utilities;
using MusicPlayer.ViewModels;

namespace MusicPlayer.Views;

public partial class MainWindow : Window
{
    private const int WM_APPCOMMAND = 0x319;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_TRAYICON = 0x800;
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const uint SWP_NOCOPYBITS = 0x0200;

    private WindowsIntegrationService? _winIntegration;
    private SystemTrayService? _trayService;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    // Shared-element (hero) transition state between the artists grid and the
    // artist detail page, so the reverse animation lands on the same circle.
    private Point _artistTransitionSourcePos;
    private Size _artistTransitionSourceSize;
    private Point _artistTransitionTargetPos;
    private Size _artistTransitionTargetSize;
    private string _artistTransitionArtistName = string.Empty;
    private bool _isAlbumTransition;
    private MusicTrack? _albumTransitionTrack;
    private bool _isBackTransitioning;
    private bool _heroTransitionPending;
    private bool _artistDetailEnteredViaHero;

    private Point _queueDragStart;
    private bool _queuePressActive;
    private bool _queueIsDragging;
    private bool _queueIsSwiping;
    private bool _queueSwipeConsumed;
    private QueueDragVisual? _queueDragVisual;
    private Border? _queueDropIndicatorLine;
    private MusicTrack? _queueDraggedTrack;
    private int _queueDragFromIndex = -1;
    private int _queueDragOriginalIndex = -1;
    private int _queueCurrentDropIndex;
    private FrameworkElement? _queueCapturedElement;
    private bool _isCleaningUpDrag;
    private bool _isCommittingDrop;
    private bool _isTransferringCapture; // Suppresses LostMouseCapture during DragStart capture transfer
    private long _queueDropCooldownUntil;
    private int _lastDisplacedTargetIndex = -1;
    // Cached TranslateTransform per-border: found once on first access, never searched again.
    // ConditionalWeakTable holds only weak refs so containers collected by GC are released.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Border, TranslateTransform>
        _translateCache = new();
    // Cached VirtualizingStackPanel of the QueueListBox (realized containers live here).
    private VirtualizingStackPanel? _queueVsp;
    // Shared easing — allocated once, never recreated per animation.
    private static readonly System.Windows.Media.Animation.QuinticEase _gapEasing =
        new() { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
    // O(1) track→index lookup for the duration of a drag.
    // Built once at DragStart (O(n)), eliminates the per-recycled-container
    // Queue.IndexOf(track) O(n) scan that fired every frame during auto-scroll.
    private readonly Dictionary<MusicTrack, int> _queueIndexCache = new(256);
    // Cached at DragStart — QueueListBox position in QueueDragCanvas space.
    // Re-reading TranslatePoint every frame via layout-sensitive ScaleTransform
    // produces jitter that makes posInListBox.Y unstable (oscillation root cause).
    private double _queueListTopCache = double.NaN;
    private double _queueListLeftCache = double.NaN;
    private double _queueEstimatedOffset = double.NaN;
    // Gap rate-limiter: only commit a new targetIndex when it has been stable for
    // at least 2 consecutive UpdateQueueDragVisual calls. Prevents single-frame
    // oscillations from triggering displacement animations in opposite directions.
    private int _queuePendingTargetIndex = -1;
    private int _queuePendingTargetStable = 0;
    // Cached pointer (canvas coords), fed ONLY by routed mouse events. Reading
    // Mouse.GetPosition while the pointer is outside the window returns a stale
    // near-top point, which snapped the card back to the source row mid-drag.
    private Point _queueDragCanvasMouse = new Point(double.NaN, double.NaN);

    // Physical-cursor arbitration. GetCursorPos comes straight from the OS and
    // is always correct, unlike WPF's routed coords: once the source row's
    // container is recycled mid-drag the detached container answers
    // e.GetPosition with its OLD slot (the source row) — feeding the card a
    // stale position even though the cursor is at the bottom. That exact read
    // (cardListY=214 = source row) is what snapped the card back. So the CARD
    // position is derived from the OS cursor via PointFromScreen — WPF event
    // coords are never consulted while dragging.
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(ref NativePOINT lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePOINT { public int X; public int Y; }

    private bool TryGetPhysicalPoint(out Point screen, out bool outside, out double winY)
    {
        screen = default; outside = true; winY = double.NaN;
        try
        {
            var nat = new NativePOINT();
            if (!GetCursorPos(ref nat)) return false;
            screen = new Point(nat.X, nat.Y);
            Point tl = PointToScreen(new Point(0, 0));
            double h = ActualHeight, w = ActualWidth;
            winY = nat.Y - tl.Y;
            outside = !(nat.X >= tl.X - 2 && nat.X <= tl.X + w + 2 && nat.Y >= tl.Y - 2 && nat.Y <= tl.Y + h + 2);
            return true;
        }
        catch { return false; }
    }

    // ── Frame-pacing: single CompositionTarget.Rendering loop drives marquee,
    //     lyrics scroll glide, and queue auto-scroll. Elapsed-time based => speed
    //     stays identical on 60/120/144 Hz. Subscription lifecycle:
    //       - First animation starts  → subscribe
    //       - Every vsync while active → update animation using elapsed time
    //       - Last animation finishes → unsubscribe immediately
    private EventHandler? _perfRenderHandler;
    private long _lastPerfTicks;
    private static readonly double _perfTicksPerMs = Stopwatch.Frequency / 1000.0;
    private bool _miniMarqueeActive;
    private bool _fpMarqueeActive;
    private bool _lyricsScrollActive;
    private bool _queueAutoScrollActive;
    // Accumulated scroll velocity (rows per 25ms base tick). Increases while
    // held at top/bottom edge (acceleration), decays when released (deceleration).
    private double _queueScrollVelocity = 0.0;
    private int    _queueScrollDir      = 0; // last scroll direction (-1 up, +1 down)

    public MainWindow()
    {
        InitializeComponent();
        SmoothScrollHelper.Initialize();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnWindowLoaded;
        Deactivated += OnWindowDeactivated;
        Activated += OnWindowActivated;
        StateChanged += OnWindowStateChanged;
        PreviewMouseUp += Window_PreviewMouseUp;
        LostMouseCapture += Window_LostMouseCapture;
        // Any real activity marks the interaction clock: background jobs
        // (wallpaper thumbs, artist face-crop upgrade) gate and cancel on it,
        // so returning to the app after a pause must be visible to those gates
        // — previously ONLY wheel/tab/navigation marked it, which let heavy
        // jobs keep running under the user's first clicks (return-lag).
        PreviewMouseMove += (_, _) => SmoothScrollHelper.MarkInteraction();
        PreviewMouseDown += (_, _) => SmoothScrollHelper.MarkInteraction();
        PreviewKeyDown   += (_, _) => SmoothScrollHelper.MarkInteraction();

        // Debounce the marquee re-measure on resize: Start*Marquee calls UpdateLayout(),
        // a synchronous full-tree layout pass. Running that on every SizeChanged event
        // (dozens per second while dragging the resize border) freezes the whole window.
        _resizeMarqueeTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };
        _resizeMarqueeTimer.Tick += (_, _) =>
        {
            _resizeMarqueeTimer.Stop();
            StartMiniTitleMarquee();
            StartFpTitleMarquee();
        };
        SizeChanged += (_, _) =>
        {
            _resizeMarqueeTimer.Stop();
            _resizeMarqueeTimer.Start();
            EnsureBlurCache();
        };

        // Single reusable timer for the "pause auto-follow after user scrolls
        // lyrics" behavior. Created once instead of allocating a new timer on
        // every ScrollChanged event (wheel scrolling used to allocate one per tick).
        _lyricsUserScrollPause = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _lyricsUserScrollPause.Tick += (_, _) =>
        {
            _lyricsUserScrollPause.Stop();
            ScrollLyricsToCurrent();
        };
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            oldVm.Queue.CollectionChanged -= OnQueueCollectionChanged;
            oldVm.ScrollToCurrentTrackRequested -= OnScrollToCurrentTrackRequested;
        }
        if (e.NewValue is MainViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            newVm.Queue.CollectionChanged += OnQueueCollectionChanged;
            newVm.ScrollToCurrentTrackRequested += OnScrollToCurrentTrackRequested;
            newVm.PropertyChanged += OnBackgroundPropertyChanged;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => { _bgDirty = true; EnsureBlurCache(); });
            RestoreWindowState();
        }
    }

    private void OnBackgroundPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.BackgroundImagePath) or nameof(MainViewModel.WallpaperBlur))
        {
            _bgDirty = true;
            _bgBlurTimer?.Stop();
            RebuildBlurCache();
        }
    }

    private bool _bgDirty;
    private System.Windows.Threading.DispatcherTimer? _bgBlurTimer;

    private void EnsureBlurCache()
    {
        if (_bgBlurTimer == null)
        {
            _bgBlurTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _bgBlurTimer.Tick += (_, _) =>
            {
                _bgBlurTimer.Stop();
                if (_bgDirty)
                {
                    _bgDirty = false;
                    RebuildBlurCache();
                }
            };
        }
        _bgBlurTimer.Stop();
        _bgBlurTimer.Start();
    }

    private void RebuildBlurCache()
    {
        try
        {
            var vm = ViewModel;
            if (vm == null) return;

            string path = vm.BackgroundImagePath;
            if (string.IsNullOrEmpty(path)) return;

            BitmapImage source = new();
            source.BeginInit();
            if (path.StartsWith("pack:"))
                source.UriSource = new Uri(path, UriKind.Absolute);
            else
                source.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.EndInit();
            source.Freeze();

            double blurRadius = vm.WallpaperBlur;
            if (blurRadius < 0.5)
            {
                BackgroundImage.Source = source;
                return;
            }

            int w = Math.Max((int)ActualWidth, 256);
            int h = Math.Max((int)ActualHeight, 256);

            var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                var ib = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
                ctx.DrawRectangle(ib, null, new Rect(0, 0, w, h));
            }
            target.Render(visual);

            var blurred = new BlurEffect { Radius = blurRadius, RenderingBias = RenderingBias.Performance };
            var wrapper = new System.Windows.Controls.Image { Source = target, Effect = blurred };
            wrapper.Measure(new Size(w, h));
            wrapper.Arrange(new Rect(0, 0, w, h));

            var result = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            result.Render(wrapper);
            result.Freeze();

            BackgroundImage.Source = result;
        }
        catch { }
    }


    private void OnQueueCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        QueueRefreshRequested();
    }

    private bool _queueRefreshPending;
    // Coalesces the queue-highlight refresh so bursts of CollectionChanged /
    // IsPlaying events collapse into a single pass per dispatcher cycle.
    private void QueueRefreshRequested()
    {
        if (_queueRefreshPending) return;
        _queueRefreshPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _queueRefreshPending = false;
            RefreshQueueHighlight();
        });
    }

    // ── Frame-pacing loop ────────────────────────────────────────────────────
    // Drives the marquee (mini + full player), the lyrics scroll glide and the
    // queue auto-scroll from a single vsync-aligned CompositionTarget.Rendering
    // callback. All motion is a function of elapsed time, not timer ticks, so
    // speed is identical at any refresh rate.
    private void EnsurePerfRenderActive()
    {
        if (_perfRenderHandler != null) return;
        _lastPerfTicks = Stopwatch.GetTimestamp();
        _perfRenderHandler = OnAnimationRenderingFrame;
        System.Windows.Media.CompositionTarget.Rendering += _perfRenderHandler;
    }

    private void StopPerfRender()
    {
        if (_perfRenderHandler != null)
        {
            System.Windows.Media.CompositionTarget.Rendering -= _perfRenderHandler;
            _perfRenderHandler = null;
        }
        if (_perfSampleCount > 0)
        {
            double avgMs = _perfSampleTotalMs / _perfSampleCount;
            double avgFps = 1000.0 / avgMs;
            LogHelper.LogInfo($"PERF anim-loop ended: frames = {_perfSampleCount}, avgFrameMs = {avgMs:0.00}, avgFps = {avgFps:0.0}, maxFrameMs = {_perfSampleMaxMs:0.00}");
            _perfSampleCount = 0;
            _perfSampleTotalMs = 0;
            _perfSampleMaxMs = 0;
        }
    }

    private void OnAnimationRenderingFrame(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double dtMs = (now - _lastPerfTicks) / _perfTicksPerMs;
        _lastPerfTicks = now;
        if (dtMs > 100.0) dtMs = 100.0; // clamp after long stalls (alt-tab, sleep)

        _perfSampleCount++;
        _perfSampleTotalMs += dtMs;
        if (_perfSampleMaxMs < dtMs) _perfSampleMaxMs = dtMs;

        bool anyActive = false;

        if (_miniMarqueeActive)
        {
            anyActive = true;
            if (!AdvanceMarquee(dtMs, MiniTitleTransform, ref _marqueeOffset, _marqueeSingleTextWidth,
                    ref _marqueePauseRemainingMs))
            {
                _miniMarqueeActive = false;
            }
        }

        if (_fpMarqueeActive)
        {
            anyActive = true;
            if (!AdvanceMarquee(dtMs, FpTitleTransform, ref _fpMarqueeOffset, _fpMarqueeSingleTextWidth,
                    ref _fpMarqueePauseRemainingMs))
            {
                _fpMarqueeActive = false;
            }
        }

        if (_lyricsScrollActive)
        {
            anyActive = true;
            if (!AdvanceLyricsScroll()) _lyricsScrollActive = false;
        }

        if (_queueAutoScrollActive)
        {
            anyActive = true;
            AdvanceQueueAutoScroll(dtMs);
        }

        if (!anyActive) StopPerfRender();
    }

    private long _perfSampleCount;
    private double _perfSampleTotalMs;
    private double _perfSampleMaxMs;

    // Returns true while the marquee should keep running.
    // Scrolls the text ONCE from right to left, then stops and resets to position 0.
    // This keeps CompositionTarget.Rendering active only during the scroll, not forever.
    private static bool AdvanceMarquee(double dtMs, TranslateTransform? transform,
        ref double offset, double singleTextWidth, ref double pauseRemainingMs)
    {
        if (transform == null) return false;

        if (pauseRemainingMs > 0)
        {
            pauseRemainingMs -= dtMs;
            return true;
        }

        offset -= MarqueeSpeed * (dtMs / 1000.0);
        double loopPoint = -(singleTextWidth + 60);
        if (offset <= loopPoint)
        {
            // One full scroll complete — reset and stop.
            offset = 0;
            transform.X = 0;
            return false;
        }

        transform.X = offset;
        return true;
    }

    private const double MarqueePauseMs = 2000; // PauseTicks(125) @16ms

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        // Services may not be ready yet (DI builds in background). Initialize them after DataContext is set.
        if (App.ServiceProvider != null)
            InitializeServices(hwnd);

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => { StartMiniTitleMarquee(); StartFpTitleMarquee(); });

        // Auto-start scroll diagnostics for perf testing
        SmoothScrollHelper.AutoStartIfPerfTest();

        // Optional self-measured Full Player transition probe (frame pacing).
        // Only runs when MUSICPLAYER_PERFTEST=1 — no effect on normal use.
        try
        {
            if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1")
            {
                var probeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                probeTimer.Tick += (_, _) => { probeTimer.Stop(); RunFullPlayerTransitionProbe(); };
                probeTimer.Start();

                // Periodic scroll diagnostics dump every 3 seconds
                var scrollDiagTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                scrollDiagTimer.Tick += (_, _) =>
                {
                    string diag = SmoothScrollHelper.DumpDiagnostics();
                    long gc0 = System.GC.CollectionCount(0);
                    long mem = System.GC.GetTotalMemory(false) / 1024;
                    diag += $" GC0={gc0} memKB={mem}";
                    LogHelper.LogInfo(diag);
                    System.Diagnostics.Debug.WriteLine(diag);
                };
                scrollDiagTimer.Start();
            }
        }
        catch { }

        // Set default artist image for ArtistImageConverter (grey circle with person icon)
        _defaultArtistImage = CreateDefaultArtistImage();
        Converters.ArtistImageConverter.DefaultArtistImage = _defaultArtistImage;

        // Recover GPU context after sleep/resume (handles app launch after sleep).
        // Toggle render mode to force WPF to reinitialize its rendering pipeline.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
        {
            try
            {
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                {
                    RenderOptions.ProcessRenderMode = RenderMode.Default;
                });
            }
            catch { }
        });
    }

    private void CacheStaticBackgroundLayers()
    {
        try
        {
            if (RootBorder?.Child is not Grid rootGrid || rootGrid.Children.Count < 3) return;

            if (rootGrid.Children[0] is Image bgImage)
            {
                bgImage.CacheMode = new System.Windows.Media.BitmapCache();
                System.Windows.Media.RenderOptions.SetCachingHint(bgImage, System.Windows.Media.CachingHint.Cache);
            }

            if (rootGrid.Children[1] is Border bgGradient)
            {
                bgGradient.CacheMode = new System.Windows.Media.BitmapCache();
            }
        }
        catch { }

        try
        {
            if (SmallAlbumArtBorder != null)
                SmallAlbumArtBorder.CacheMode = new System.Windows.Media.BitmapCache();
        }
        catch { }
    }

    internal void LoadContent()
    {
        ContentGrid.Visibility = Visibility.Visible;
    }

    internal void InitializeServices(nint hwnd)
    {
        _winIntegration = App.ServiceProvider?.GetRequiredService<WindowsIntegrationService>();
        if (_winIntegration != null)
        {
            _winIntegration.SetWindowHandle(hwnd);
            _winIntegration.SetTimelineProvider(() => ViewModel == null ? null : ((TimeSpan, TimeSpan)?)(ViewModel.Position, ViewModel.Duration));
            _winIntegration.PlayPauseRequested += () => ViewModel?.PlayPauseCommand.Execute(null);
            _winIntegration.NextRequested += () => ViewModel?.NextCommand.Execute(null);
            _winIntegration.PreviousRequested += () => ViewModel?.PreviousCommand.Execute(null);
        }

        _trayService = App.ServiceProvider?.GetRequiredService<SystemTrayService>();
        if (_trayService != null)
        {
            _trayService.Initialize(hwnd);
            _trayService.PlayPauseRequested += () => ViewModel?.PlayPauseCommand.Execute(null);
            _trayService.NextRequested += () => ViewModel?.NextCommand.Execute(null);
            _trayService.PreviousRequested += () => ViewModel?.PreviousCommand.Execute(null);
            _trayService.OpenRequested += () => Dispatcher.BeginInvoke((Action)(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
            _trayService.ExitRequested += () => Dispatcher.BeginInvoke((Action)(() => { Application.Current.Shutdown(); }));
        }

        if (ViewModel != null)
            ViewModel.PropertyChanged += OnViewModelTrackChanged;

        RestoreWindowState();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // NOTE: WM_APPCOMMAND (media keys F9/F10/F11) is intentionally NOT
        // consumed here. If we mark it handled, Windows' own media-key monitor
        // never sees the press and suppresses its native media overlay card.
        // Letting it reach the shell shows the native overlay; playback then
        // responds globally through our SMTC session (ButtonPressed).
        if (msg == WM_HOTKEY)
        {
            handled = true;
            _winIntegration?.HandleHotKey(wParam.ToInt32());
        }
        else if (msg == WM_TRAYICON)
        {
            handled = true;
            _trayService?.HandleCallbackMessage(lParam.ToInt32());
        }
        else if (msg == WM_DEVICECHANGE && wParam == DBT_DEVNODES_CHANGED)
        {
            HandleDeviceChange();
        }
        else if (msg == WM_WINDOWPOSCHANGING)
        {
            // Windows normally reuses (copies) the old client bitmap when a
            // window is resized/maximized, which flashes stale doubled content
            // until WPF finishes its layout pass. Forcing a full repaint (no
            // copybits) eliminates that moment of ghosting/freeze.
            var wpos = System.Runtime.InteropServices.Marshal.PtrToStructure<WINDOWPOS>(lParam);
            wpos.flags |= SWP_NOCOPYBITS;
            System.Runtime.InteropServices.Marshal.StructureToPtr(wpos, lParam, true);
        }
        return IntPtr.Zero;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public nint hwnd;
        public nint hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    private void HandleDeviceChange()
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            if (ViewModel == null) return;
            var engine = App.ServiceProvider?.GetRequiredService<Audio.AudioEngine>();
            if (engine == null) return;

            bool wasPlaying = engine.IsPlaying;
            var devices = Audio.AudioEngine.GetOutputDevices();
            if (devices.Count == 0) return;

            bool currentDeviceAvailable = devices.Any(d => d.Id == engine.DeviceNumber);
            if (!currentDeviceAvailable && engine.DeviceNumber >= 0)
            {
                engine.DeviceNumber = devices[0].Id;
            }
        }));
    }

    private void RestoreWindowState()
    {
        MainViewModel? viewModel = ViewModel;
        if (viewModel == null) return;
        ApplyPersistedWindowState(
            viewModel.GetWindowSetting("WindowWidth", 1280.0),
            viewModel.GetWindowSetting("WindowHeight", 720.0),
            viewModel.GetWindowSetting("WindowX", double.NaN),
            viewModel.GetWindowSetting("WindowY", double.NaN),
            viewModel.GetWindowSettingBool("WindowMaximized", false));
    }

    internal void ApplyPersistedWindowState(double width, double height, double x, double y, bool maximized)
    {
        try
        {
            if (width >= MinWidth) Width = width;
            if (height >= MinHeight) Height = height;
            if (!double.IsNaN(x) && !double.IsNaN(y))
            {
                double virtualScreenLeft = SystemParameters.VirtualScreenLeft;
                double virtualScreenTop = SystemParameters.VirtualScreenTop;
                double virtualScreenWidth = SystemParameters.VirtualScreenWidth;
                double virtualScreenHeight = SystemParameters.VirtualScreenHeight;
                if (x >= virtualScreenLeft && x + 200.0 <= virtualScreenLeft + virtualScreenWidth &&
                    y >= virtualScreenTop && y + 100.0 <= virtualScreenTop + virtualScreenHeight)
                {
                    Left = x;
                    Top = y;
                    WindowStartupLocation = WindowStartupLocation.Manual;
                }
            }
            if (maximized) WindowState = WindowState.Maximized;
        }
        catch { }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        try { CancelQueueDrag("WindowClosing"); } catch { }
        try { _trayService?.RemoveIcon(); } catch { }
        try { SaveWindowState(); } catch { }

        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(3000);
            Environment.Exit(0);
        }) { IsBackground = true }.Start();
    }

    private void SaveWindowState()
    {
        MainViewModel? viewModel = ViewModel;
        if (viewModel == null) return;
        try
        {
            bool isMaximized = WindowState == WindowState.Maximized;
            viewModel.SetWindowSetting("WindowMaximized", isMaximized);
            if (!isMaximized)
            {
                viewModel.SetWindowSetting("WindowWidth", ActualWidth);
                viewModel.SetWindowSetting("WindowHeight", ActualHeight);
                viewModel.SetWindowSetting("WindowX", Left);
                viewModel.SetWindowSetting("WindowY", Top);
            }
        }
        catch { }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SearchQuery = string.Empty;
            ViewModel.SearchResults.Clear();
        }
    }

    private void SearchBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        AnimateCloseZoom();
    }

    private void AllSongsCard_Click(object sender, RoutedEventArgs e) => ViewModel?.NavigateCommand.Execute("songs");
    private void AllSongsCard_Click(object sender, MouseButtonEventArgs e) => ViewModel?.NavigateCommand.Execute("songs");
    private void FoldersCard_Click(object sender, MouseButtonEventArgs e) => ViewModel?.NavigateCommand.Execute("folders");
    private void FavoritesCard_Click(object sender, MouseButtonEventArgs e) => ViewModel?.NavigateCommand.Execute("favorites");
    private void AlbumsCard_Click(object sender, MouseButtonEventArgs e) => ViewModel?.NavigateCommand.Execute("albums");
    private void ArtistsCard_Click(object sender, MouseButtonEventArgs e) => ViewModel?.NavigateCommand.Execute("artists");
    private void GenresCard_Click(object sender, MouseButtonEventArgs e) => ViewModel?.NavigateCommand.Execute("genres");
    private void RecentPlayCard_Click(object sender, MouseButtonEventArgs e) => ViewModel?.NavigateCommand.Execute("recent");
    private void RecentAddCard_Click(object sender, MouseButtonEventArgs e) => ViewModel?.NavigateCommand.Execute("recent_add");
    private void MostPlayCard_Click(object sender, MouseButtonEventArgs e) => ViewModel?.NavigateCommand.Execute("most_played");

    private int _previousTabIndex;
    private bool _isTabSliding;
    private string? _pendingTab;

    private void TabSongs_Click(object sender, RoutedEventArgs e) => SwitchTab("songs");
    private void TabArtists_Click(object sender, RoutedEventArgs e) => SwitchTab("artists");
    private void TabAlbums_Click(object sender, RoutedEventArgs e) => SwitchTab("albums");
    private void TabGenres_Click(object sender, RoutedEventArgs e) => SwitchTab("genres");
    private void TabFolders_Click(object sender, RoutedEventArgs e) => SwitchTab("folders");

    private Grid? GetTabContent(string tab) => tab switch
    {
        "songs" => TabContent_Songs,
        "artists" => TabContent_Artists,
        "albums" => TabContent_Albums,
        "genres" => TabContent_Genres,
        "folders" => TabContent_Folders,
        _ => TabContent_Songs
    };

    private void SwitchTab(string newTab)
    {
        var vm = ViewModel;
        if (vm == null || vm.CurrentTab == newTab || _isTabSliding)
        {
            // A click that lands mid-slide is not dropped: the latest requested
            // tab is remembered and switched to as soon as the running slide
            // finishes, so rapid tab browsing always ends on the tab the user
            // last clicked (intermediate clicks coalesce into that one slide).
            if (_isTabSliding && vm != null && vm.CurrentTab != newTab)
            {
                _pendingTab = newTab;
                if (SmoothScrollHelper.ProfilingEnabled)
                    LogHelper.LogInfo($"TAB-QUEUE requested={newTab} current={vm.CurrentTab}");
            }
            return;
        }

        // NOTE: LoadTabData is intentionally called AFTER the animation below,
        // not here — building the WrapPanel items while the slide animation runs
        // causes the animation to stutter. Data is loaded once the tab is visible.

        int newIndex = newTab switch { "songs" => 0, "artists" => 1, "albums" => 2, "genres" => 3, "folders" => 4, _ => 0 };
        int oldIndex = _previousTabIndex;
        bool movingRight = newIndex > oldIndex;

        var outgoing = GetTabContent(vm.CurrentTab);
        var incoming = GetTabContent(newTab);

        if (outgoing == null || incoming == null || TabSlideHost == null) return;

        double w = TabSlideHost.ActualWidth;
        if (w <= 0) w = 600;

        _isTabSliding = true;
        SmoothScrollHelper.BeginTabSlide(newTab);
        vm.CurrentTab = newTab;

        outgoing.Visibility = Visibility.Visible;
        incoming.Visibility = Visibility.Visible;

        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(220);

        double incomingStart = movingRight ? w : -w;
        double outgoingEnd = movingRight ? -w : w;

        var incomingTransform = new TranslateTransform(incomingStart, 0);
        var outgoingTransform = new TranslateTransform(0, 0);
        incoming.RenderTransform = incomingTransform;
        outgoing.RenderTransform = outgoingTransform;

        // Two-phase tab slide: making the incoming page Visible forces a one-time
        // layout of its whole subtree. Paying that while the slide's first frames
        // are being composed turns it into a visible start hitch, so the layout is
        // forced first and the slide begins right after, on clean frames.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            if (!_isTabSliding) return;
            long prepT0 = 0L;
            if (SmoothScrollHelper.ProfilingEnabled) prepT0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { incoming.UpdateLayout(); outgoing.UpdateLayout(); } catch { }
            if (SmoothScrollHelper.ProfilingEnabled)
                LogHelper.LogInfo($"TAB-PREP {newTab} layoutMs={(System.Diagnostics.Stopwatch.GetTimestamp() - prepT0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0} relMs={SmoothScrollHelper.TabRelMs():0}");
            var inAnim = new DoubleAnimation(incomingStart, 0, dur) { EasingFunction = ease };
            var outAnim = new DoubleAnimation(0, outgoingEnd, dur) { EasingFunction = ease };

            inAnim.Completed += (_, _) =>
            {
                incoming.RenderTransform = null;
            };

            outgoingTransform.BeginAnimation(TranslateTransform.XProperty, outAnim);
            incomingTransform.BeginAnimation(TranslateTransform.XProperty, inAnim);
        });

        _ = Task.Run(async () =>
        {
            await Task.Delay(dur + TimeSpan.FromMilliseconds(50));
            Dispatcher.Invoke(() =>
            {
                outgoing.Visibility = Visibility.Collapsed;
                outgoing.RenderTransform = null;
                incoming.RenderTransform = null;
                _isTabSliding = false;

                // Load data AFTER animation ends — avoids racing WrapPanel
                // item creation with the slide animation and causing stutter.
                vm.LoadTabData(newTab);
                if (SmoothScrollHelper.ProfilingEnabled)
                    LogHelper.LogInfo($"TAB-LOADKICK {newTab} relMs={SmoothScrollHelper.TabRelMs():0}");

                // Drain a click that arrived while this slide was running.
                var pending = _pendingTab;
                _pendingTab = null;
                if (pending != null && pending != vm.CurrentTab)
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => SwitchTab(pending)));
            });
        });

        _previousTabIndex = newIndex;
    }

    private void FolderRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderItem folder })
            ViewModel?.NavigateToFolder(folder.Path);
    }

    private void AlbumRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: MusicTrack dataContext } border) return;

        FrameworkElement? square = null;
        if (border.Child is StackPanel sp && sp.Children.Count > 0)
            square = sp.Children[0] as System.Windows.Controls.Grid;
        square ??= border;

        _artistTransitionSourcePos = square.TransformToAncestor(ContentGrid).Transform(new Point(0, 0));
        _artistTransitionSourceSize = new Size(square.ActualWidth, square.ActualHeight);
        _artistTransitionArtistName = dataContext.Album;
        _isAlbumTransition = true;
        _albumTransitionTrack = dataContext;

        var converter = new Converters.AlbumArtConverter();
        if (converter.Convert(dataContext, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture) is not ImageSource imageSource) return;

        ResetArtistTransition();
        // Hide the album detail page BEFORE navigating to avoid flash
        AlbumDetailView.Opacity = 0;
        _heroTransitionPending = true;

        ViewModel?.NavigateToAlbum(dataContext.Album, dataContext.Artist);

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            var target = FindAlbumDetailArt();
            if (target != null)
            {
                _artistTransitionTargetPos = target.TransformToAncestor(ContentGrid).Transform(new Point(0, 0));
                _artistTransitionTargetSize = new Size(target.ActualWidth, target.ActualHeight);
            }
            else
            {
                _artistTransitionTargetPos = _artistTransitionSourcePos;
                _artistTransitionTargetSize = new Size(280, 280);
            }

            // Aggressively hide EVERYTHING that could show the album art during hero morph.
            // 1) Hide the entire AlbumDetailView (parent opacity)
            // 2) Hide the specific art Border element (direct target)
            AlbumDetailView.Opacity = 0;
            AlbumDetailView.IsHitTestVisible = false;
            if (target != null)
            {
                target.Opacity = 0;
                target.IsHitTestVisible = false;
            }

            LogHelper.LogInfo($"HeroTransition fwd album: src=({_artistTransitionSourcePos.X:F1},{_artistTransitionSourcePos.Y:F1}) target=({_artistTransitionTargetPos.X:F1},{_artistTransitionTargetPos.Y:F1})");
            StartForwardTransition(imageSource);
        });
    }

    private void GenreRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: MusicTrack dataContext }) return;
        var genre = dataContext.Genre;
        if (!string.IsNullOrEmpty(genre))
            ViewModel?.NavigateToGenre(genre);
    }

    private void NewPlaylist_Click(object sender, RoutedEventArgs e) => ShowNewPlaylistOverlay();
    private void NewPlaylist_Click(object sender, MouseButtonEventArgs e) => ShowNewPlaylistOverlay();

    private void ShowNewPlaylistOverlay()
    {
        if (ViewModel == null) return;
        ViewModel.NewPlaylistName = "My Playlist";
        ViewModel.ShowNewPlaylistOverlay = true;
        Dispatcher.BeginInvoke(() =>
        {
            NewPlaylistNameBox?.SelectAll();
            NewPlaylistNameBox?.Focus();
        });
    }

    private void NewPlaylistCancel_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ShowNewPlaylistOverlay = false;
    }

    private void NewPlaylistConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var name = ViewModel.NewPlaylistName?.Trim();
        ViewModel.ShowNewPlaylistOverlay = false;
        if (!string.IsNullOrWhiteSpace(name))
            ViewModel.CreatePlaylistCommand.Execute(name);
    }

    private void RenamePlaylistCancel_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ShowRenamePlaylistOverlay = false;
    }

    private void RenamePlaylistConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var name = ViewModel.RenamePlaylistName?.Trim();
        var p = ViewModel.RenamingPlaylist;
        ViewModel.ShowRenamePlaylistOverlay = false;
        if (p != null && !string.IsNullOrWhiteSpace(name))
            ViewModel.RenamePlaylistCommand.Execute((p, name));
    }

    private void FavoritesPlaylist_Click(object sender, MouseButtonEventArgs e) => ViewModel?.NavigateCommand.Execute("favorites");

    private void PlaylistItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlaylistViewModel dataContext })
            ViewModel?.NavigateToPlaylist(dataContext.Id, dataContext.Name);
    }

    private void PlaylistDetailBack_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.GoBackCommand.Execute(null);
    }

    private void PlaylistDetailTrack_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MusicTrack dataContext })
            ViewModel?.PlayTrackCommand.Execute(dataContext);
    }

    private void PlaylistDetailContextPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: MusicTrack vm } } })
            ViewModel?.PlayTrackCommand.Execute(vm);
    }

    private void PlaylistDetailContextRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: MusicTrack vm } } })
            ViewModel?.RemoveTrackFromPlaylistCommand.Execute(vm);
    }

    private void GenreDetailBack_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.GoBackCommand.Execute(null);
    }

    private void GenreDetailTrack_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MusicTrack dataContext })
            ViewModel?.PlayTrackCommand.Execute(dataContext);
    }

    private void GenreDetailContextPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: MusicTrack vm } } })
            ViewModel?.PlayTrackCommand.Execute(vm);
    }

    private void AddGenreToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.GenreDetailTracks.Count > 0)
        {
            ViewModel.PendingTracksToAdd = ViewModel.GenreDetailTracks.ToList();
            ViewModel.OpenPlaylistSelectCommand.Execute(null);
        }
    }

    private void TrackRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MusicTrack dataContext })
            ViewModel?.PlayTrackCommand.Execute(dataContext);
    }

    private void ArtistRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: MusicTrack dataContext } border) return;

        FrameworkElement? circle = null;
        if (border.Child is StackPanel sp && sp.Children.Count > 0)
            circle = sp.Children[0] as System.Windows.Controls.Grid;
        circle ??= border;

        _artistTransitionSourcePos = circle.TransformToAncestor(ContentGrid).Transform(new Point(0, 0));
        _artistTransitionSourceSize = new Size(circle.ActualWidth, circle.ActualHeight);
        _artistTransitionArtistName = dataContext.Artist;
        _isAlbumTransition = false;
        _albumTransitionTrack = null;

        var converter = new Converters.ArtistImageConverter();
        var imageSource = converter.Convert(dataContext.Artist, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;

        if (imageSource == null)
        {
            // Capture the exact default-artist element rendered in the grid
            // (same icon the program shows for artists without a photo).
            _defaultArtistImage = CaptureElementToBitmap(circle);
            imageSource = _defaultArtistImage;
        }

        if (imageSource == null)
        {
            ViewModel?.NavigateToArtist(dataContext.Artist);
            return;
        }

        ResetArtistTransition();
        // Hide the detail circle AND the detail page BEFORE navigating so the
        // transition has no aborted full-page flash while the overlay morphs.
        ArtistDetailCircle.Opacity = 0;
        ArtistDetailView.Opacity = 0;
        _heroTransitionPending = true;

        ViewModel?.NavigateToArtist(dataContext.Artist);

        // Measure the target AFTER navigation but BEFORE any slide transform
        // so the overlay lands exactly on the real final circle position.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            var target = FindArtistDetailCircle();
            if (target != null)
            {
                _artistTransitionTargetPos = target.TransformToAncestor(ContentGrid).Transform(new Point(0, 0));
                _artistTransitionTargetSize = new Size(target.ActualWidth, target.ActualHeight);
            }
            else
            {
                _artistTransitionTargetPos = _artistTransitionSourcePos;
                _artistTransitionTargetSize = new Size(300, 300);
            }

            // The big circle stays visible and continuous under the overlay; only the
            // surrounding content (back row + info panel) will be mix-revealed later.
            PrepareDetailMixReveal();

            LogHelper.LogInfo($"HeroTransition fwd: src=({_artistTransitionSourcePos.X:F1},{_artistTransitionSourcePos.Y:F1}) target=({_artistTransitionTargetPos.X:F1},{_artistTransitionTargetPos.Y:F1})");

            StartForwardTransition(imageSource);
        });
    }

    // Hide only the non-circle content of the artist detail page so it can be
    // mix-revealed (fade + slide) once the shared element lands on the circle.
    private void PrepareDetailMixReveal()
    {
        // Opacity stays whatever ArtistRow_Click set (0) so the detail page
        // fades in together with the morphing circle via StartForwardTransition.
        ArtistDetailView.RenderTransform = null;

        // The real circle must stay hidden while the overlay morphs; it becomes
        // the shared element. We reveal it only after the overlay fades out.
        ArtistDetailCircle.Opacity = 0;

        ArtistDetailBackRow.Opacity = 0;
        ArtistDetailBackRow.RenderTransform = new TranslateTransform { Y = -12 };

        ArtistDetailInfo.Opacity = 0;
        ArtistDetailInfo.RenderTransform = new TranslateTransform { Y = 22 };
    }

    private void RevealDetailMix(TimeSpan? delay = null)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        var slideBack = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        var slideInfo = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };

        if (delay.HasValue)
        {
            fadeIn.BeginTime = delay;
            slideBack.BeginTime = delay;
            slideInfo.BeginTime = delay;
        }

        ArtistDetailBackRow.BeginAnimation(OpacityProperty, fadeIn);
        if (ArtistDetailBackRow.RenderTransform is TranslateTransform t1)
            t1.BeginAnimation(TranslateTransform.YProperty, slideBack);

        ArtistDetailInfo.BeginAnimation(OpacityProperty, fadeIn);
        if (ArtistDetailInfo.RenderTransform is TranslateTransform t2)
            t2.BeginAnimation(TranslateTransform.YProperty, slideInfo);
    }

    private System.Windows.Controls.Grid? FindArtistDetailCircle()
    {
        if (VisualTreeHelper.GetChildrenCount(ArtistDetailView) < 2) return null;
        var contentPanel = VisualTreeHelper.GetChild(ArtistDetailView, 1) as Panel;
        if (contentPanel == null) return null;
        foreach (var child in contentPanel.Children)
        {
            if (child is System.Windows.Controls.Grid g && Math.Abs(g.Width - 300) < 0.5)
                return g;
        }
        return null;
    }

    private FrameworkElement? FindAlbumDetailArt()
    {
        if (VisualTreeHelper.GetChildrenCount(AlbumDetailView) < 2) return null;
        var contentPanel = VisualTreeHelper.GetChild(AlbumDetailView, 1) as Panel;
        if (contentPanel == null) return null;
        foreach (var child in contentPanel.Children)
        {
            if (child is FrameworkElement fe && Math.Abs(fe.Width - 280) < 1)
                return fe;
            if (child is Panel p)
            {
                foreach (var gc in p.Children)
                    if (gc is FrameworkElement gfe && Math.Abs(gfe.Width - 280) < 1)
                        return gfe;
            }
        }
        return null;
    }

    private static System.Windows.Media.ImageSource? _defaultArtistImage;

    private static System.Windows.Media.ImageSource? CreateDefaultArtistImage()
    {
        try
        {
            if (_defaultArtistImage != null) return _defaultArtistImage;

            const int size = 256;
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Grey circle background
                dc.DrawEllipse(
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3E, 0x48)),
                    null,
                    new System.Windows.Point(size / 2.0, size / 2.0),
                    size / 2.0, size / 2.0);

                // Person icon (head + body) scaled from 48×48 viewBox to 256×256.
                // Geometry.Parse returns frozen (read-only) geometries, so apply
                // the scale via DrawingContext.PushTransform instead of setting
                // .Transform (setting it throws "read-only state" -> crash).
                double s = size / 48.0;
                var headGeom = System.Windows.Media.Geometry.Parse(
                    "M24,24c4.4,0,8-3.6,8-8s-3.6-8-8-8s-8,3.6-8,8S19.6,24,24,24z");
                var bodyGeom = System.Windows.Media.Geometry.Parse(
                    "M24,28c-5.3,0-16,2.7-16,8v4h32v-4C40,30.7,29.3,28,24,28z");

                var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
                dc.PushTransform(new System.Windows.Media.ScaleTransform(s, s));
                dc.DrawGeometry(brush, null, headGeom);
                dc.DrawGeometry(brush, null, bodyGeom);
                dc.Pop();
            }

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            _defaultArtistImage = rtb;
            return rtb;
        }
        catch
        {
            return null;
        }
    }

    private static System.Windows.Media.ImageSource? CaptureElementToBitmap(FrameworkElement? element)
    {
        if (element == null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
        try
        {
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(element.ActualWidth),
                (int)Math.Ceiling(element.ActualHeight),
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(element);
            rtb.Freeze();
            return rtb;
        }
        catch { return null; }
    }

    private static void CollectImages(DependencyObject root, List<Image> images)
    {
        if (root is Image img)
        {
            images.Add(img);
            return;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            CollectImages(VisualTreeHelper.GetChild(root, i), images);
    }

    private void ResetArtistTransition()
    {
        ArtistTransitionOverlay.BeginAnimation(OpacityProperty, null);
        ArtistTransitionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ArtistTransitionScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ArtistTransitionTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        ArtistTransitionTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        ArtistDetailCircle.BeginAnimation(OpacityProperty, null);
        ArtistDetailCircle.Opacity = 1;
        ArtistDetailBackRow.BeginAnimation(OpacityProperty, null);
        ArtistDetailInfo.BeginAnimation(OpacityProperty, null);
        if (ArtistDetailBackRow.RenderTransform is TranslateTransform rm)
            rm.BeginAnimation(TranslateTransform.YProperty, null);
        if (ArtistDetailInfo.RenderTransform is TranslateTransform tm)
            tm.BeginAnimation(TranslateTransform.YProperty, null);
        ArtistDetailView.BeginAnimation(OpacityProperty, null);
        ArtistDetailView.Opacity = 1;
        if (ArtistDetailView.RenderTransform is TranslateTransform oldTranslate)
            oldTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        AlbumDetailView.BeginAnimation(OpacityProperty, null);
        // During album transitions, don't restore AlbumDetailView or art —
        // the caller hides them explicitly to prevent double-image.
        if (!_isAlbumTransition)
        {
            AlbumDetailView.Opacity = 1;
            AlbumDetailView.IsHitTestVisible = true;
            var albumArt = FindAlbumDetailArt();
            if (albumArt != null)
            {
                albumArt.BeginAnimation(OpacityProperty, null);
                albumArt.Opacity = 1;
                albumArt.IsHitTestVisible = true;
            }
        }

        // Restore ellipse clip and ScaleTransform center for artist transitions
        ArtistTransitionGrid.Clip = ArtistTransitionClip;
        ArtistTransitionScale.CenterX = 150;
        ArtistTransitionScale.CenterY = 150;

        // Restore every ContentGrid child to full opacity (the reverse transition
        // fades all of them in, whoever GoBack reveals).
        foreach (var child in ContentGrid.Children)
        {
            if (child == ArtistTransitionOverlay) continue;
            if (_isAlbumTransition && child == AlbumDetailView) continue;
            if (child is FrameworkElement cfe)
            {
                cfe.BeginAnimation(OpacityProperty, null);
                cfe.Opacity = 1;
            }
        }

        ArtistsViewGrid.BeginAnimation(OpacityProperty, null);
        ArtistsViewGrid.Opacity = 1;
        if (ArtistsViewGrid.RenderTransform is TranslateTransform avt)
            avt.BeginAnimation(TranslateTransform.YProperty, null);
        else
            ArtistsViewGrid.RenderTransform = null;
    }

    private void StartForwardTransition(ImageSource imageSource)
    {
        const double morphMs = 350;

        // Nav already happened; the pending flag is only needed so the CurrentView
        // handler doesn't cancel the detail page's hidden state during the morph.
        _heroTransitionPending = false;
        _artistDetailEnteredViaHero = !_isAlbumTransition;

        double targetSize = _artistTransitionTargetSize.Width;
        double scale = Math.Clamp(_artistTransitionSourceSize.Width / targetSize, 0.1, 1.5);
        var sourceCenter = new Point(
            _artistTransitionSourcePos.X + _artistTransitionSourceSize.Width / 2,
            _artistTransitionSourcePos.Y + _artistTransitionSourceSize.Height / 2);
        var targetCenter = new Point(
            _artistTransitionTargetPos.X + targetSize / 2,
            _artistTransitionTargetPos.Y + targetSize / 2);

        ArtistTransitionImage.Source = imageSource;
        ArtistTransitionOverlay.Opacity = 1.0;
        ArtistTransitionOverlay.Visibility = Visibility.Visible;

        Canvas.SetLeft(ArtistTransitionGrid, _artistTransitionTargetPos.X);
        Canvas.SetTop(ArtistTransitionGrid, _artistTransitionTargetPos.Y);
        ArtistTransitionGrid.Width = targetSize;
        ArtistTransitionGrid.Height = targetSize;

        // Update ScaleTransform center to match actual grid size (150 for artist, 140 for album 280)
        ArtistTransitionScale.CenterX = targetSize / 2;
        ArtistTransitionScale.CenterY = targetSize / 2;

        // Set clip based on transition type
        if (_isAlbumTransition)
        {
            ArtistTransitionGrid.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, targetSize, targetSize));
        }
        else
        {
            ArtistTransitionClip.Center = new Point(targetSize / 2, targetSize / 2);
            ArtistTransitionClip.RadiusX = targetSize / 2;
            ArtistTransitionClip.RadiusY = targetSize / 2;
            ArtistTransitionGrid.Clip = ArtistTransitionClip;
        }

        // ScaleTransform scales from Center (targetSize/2, targetSize/2).
        // Translate = sourceCenter - targetCenter compensates for the offset.
        ArtistTransitionScale.ScaleX = scale;
        ArtistTransitionScale.ScaleY = scale;
        ArtistTransitionTranslate.X = sourceCenter.X - targetCenter.X;
        ArtistTransitionTranslate.Y = sourceCenter.Y - targetCenter.Y;

        var duration = TimeSpan.FromMilliseconds(morphMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // Artist: fade the detail page in from 0 as the circle grows.
        // Album: do NOT fade in here — AlbumDetailView stays hidden (opacity 0)
        // during the entire hero morph to prevent double-image. It is revealed
        // only after the overlay disappears (see below).
        if (!_isAlbumTransition)
            ArtistDetailView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1.0, duration) { EasingFunction = ease });

        ArtistTransitionScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, duration) { EasingFunction = ease });
        ArtistTransitionScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, duration) { EasingFunction = ease });
        ArtistTransitionTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, duration) { EasingFunction = ease });
        ArtistTransitionTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0.0, duration) { EasingFunction = ease });

        if (_isAlbumTransition)
        {
            // Album crossfade: once the morph lands on the target, crossfade
            // the overlay OUT while simultaneously fading the real page IN.
            // No gap = no flicker. The overlay covers the page the whole time
            // so there's never a double-image.
            var crossfadeEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var crossfadeDuration = TimeSpan.FromMilliseconds(250);

            // Overlay fades out starting right after morph completes
            var overlayFadeOut = new DoubleAnimation(0.0, crossfadeDuration) { BeginTime = duration, EasingFunction = crossfadeEase };
            overlayFadeOut.Completed += (_, _) =>
            {
                ArtistTransitionOverlay.Visibility = Visibility.Collapsed;
                ArtistTransitionOverlay.BeginAnimation(OpacityProperty, null);
                ArtistTransitionOverlay.Opacity = 1;
                // Restore interactivity AFTER overlay is gone
                AlbumDetailView.IsHitTestVisible = true;
                var artEl = FindAlbumDetailArt();
                if (artEl != null) artEl.IsHitTestVisible = true;
            };
            ArtistTransitionOverlay.BeginAnimation(OpacityProperty, overlayFadeOut);

            // Content fades in at the same time as overlay fades out (crossfade)
            var contentFadeIn = new DoubleAnimation(1.0, crossfadeDuration) { BeginTime = duration, EasingFunction = crossfadeEase };
            AlbumDetailView.BeginAnimation(OpacityProperty, contentFadeIn);

            var art = FindAlbumDetailArt();
            if (art != null)
            {
                art.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, crossfadeDuration) { BeginTime = duration, EasingFunction = crossfadeEase });
            }
        }
        else
        {
            // Artist: "Mix" reveal for surrounding content (back row + info)
            RevealDetailMix(duration);

            // After the surrounding content has finished its mix reveal, reveal the real
            // circle while the overlay is STILL fully opaque (zero flicker).
            var revealEnd = duration + TimeSpan.FromMilliseconds(280);

            var earlyReveal = revealEnd - TimeSpan.FromMilliseconds(100);
            if (earlyReveal < TimeSpan.Zero) earlyReveal = TimeSpan.Zero;
            var earlyTimer = new System.Windows.Threading.DispatcherTimer { Interval = earlyReveal };
            earlyTimer.Tick += (_, _) =>
            {
                earlyTimer.Stop();
                ArtistDetailCircle.Opacity = 1;
            };
            earlyTimer.Start();

            var handoffEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200)) { BeginTime = revealEnd, EasingFunction = handoffEase };
            fadeOut.Completed += (_, _) => ArtistTransitionOverlay.Visibility = Visibility.Collapsed;
            ArtistTransitionOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    private void ArtistDetailBack_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        LogHelper.LogInfo($"HeroTransition back: artist='{_artistTransitionArtistName}' srcW={_artistTransitionSourceSize.Width} " +
            $"enteredViaHero={_artistDetailEnteredViaHero} ArtistDetailView.Opacity={ArtistDetailView.Opacity} Vis={ArtistDetailView.Visibility} view='{ViewModel?.CurrentView}'");

        if (!_artistDetailEnteredViaHero || string.IsNullOrEmpty(_artistTransitionArtistName) || _artistTransitionSourceSize.Width <= 0)
        {
            LogHelper.LogInfo("HeroTransition back: FALLBACK - no stored state");
            vm.GoBackCommand.Execute(null);
            return;
        }

        var converter = new Converters.ArtistImageConverter();
        var imageSource = converter.Convert(_artistTransitionArtistName, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource
            ?? _defaultArtistImage;

        if (imageSource == null)
        {
            LogHelper.LogInfo("HeroTransition back: FALLBACK - no image");
            vm.GoBackCommand.Execute(null);
            return;
        }

        LogHelper.LogInfo("HeroTransition back: starting reverse");

        // Measure the detail circle exactly (the overlay must start right on top of it)
        var target = FindArtistDetailCircle();
        if (target != null)
        {
            _artistTransitionTargetPos = target.TransformToAncestor(ContentGrid).Transform(new Point(0, 0));
            _artistTransitionTargetSize = new Size(target.ActualWidth, target.ActualHeight);
        }

        StartReverseTransition(imageSource);
    }

    private void AlbumDetailBack_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        if (string.IsNullOrEmpty(_artistTransitionArtistName) || _artistTransitionSourceSize.Width <= 0)
        {
            vm.GoBackCommand.Execute(null);
            return;
        }

        ImageSource? imageSource = null;
        var converter = new Converters.AlbumArtConverter();
        if (_albumTransitionTrack != null)
            imageSource = converter.Convert(_albumTransitionTrack, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;

        if (imageSource == null)
        {
            vm.GoBackCommand.Execute(null);
            return;
        }

        LogHelper.LogInfo($"HeroTransition back: album='{_artistTransitionArtistName}' srcW={_artistTransitionSourceSize.Width}");

        var target = FindAlbumDetailArt();
        // Immediately hide the art element to prevent double-image during back transition
        if (target != null)
        {
            target.BeginAnimation(OpacityProperty, null);
            target.Opacity = 0;
        }

        if (target != null)
        {
            _artistTransitionTargetPos = target.TransformToAncestor(ContentGrid).Transform(new Point(0, 0));
            _artistTransitionTargetSize = new Size(target.ActualWidth, target.ActualHeight);
        }

        StartReverseTransition(imageSource);
    }

    private void StartReverseTransition(ImageSource imageSource)
    {
        // Block all input (clicks + Escape) until the back animation finishes.
        _isBackTransitioning = true;
        if (TransitionInputBlocker != null)
            TransitionInputBlocker.Visibility = Visibility.Visible;

        // Safety net: never leave input blocked if the fade-out animation is
        // cancelled for any reason. The animation takes ~650ms total.
        var unblockTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        unblockTimer.Tick += (_, _) =>
        {
            unblockTimer.Stop();
            _isBackTransitioning = false;
            if (TransitionInputBlocker != null)
                TransitionInputBlocker.Visibility = Visibility.Collapsed;
        };
        unblockTimer.Start();

        const double morphMs = 350;

        // Save album transition state before ResetArtistTransition clears animations
        bool wasAlbumTransition = _isAlbumTransition;

        ResetArtistTransition();

        // Restore album state (ResetArtistTransition doesn't clear these anymore,
        // but be safe)
        _isAlbumTransition = wasAlbumTransition;

        // Reset transforms on the detail view we're about to fade
        if (_isAlbumTransition)
            AlbumDetailView.RenderTransform = null;
        else
            ArtistDetailView.RenderTransform = null;

        double targetSize = _artistTransitionTargetSize.Width;

        // Show the overlay sitting exactly on the big detail element
        ArtistTransitionImage.Source = imageSource;
        ArtistTransitionOverlay.Opacity = 1.0;
        ArtistTransitionOverlay.Visibility = Visibility.Visible;

        Canvas.SetLeft(ArtistTransitionGrid, _artistTransitionTargetPos.X);
        Canvas.SetTop(ArtistTransitionGrid, _artistTransitionTargetPos.Y);
        ArtistTransitionGrid.Width = targetSize;
        ArtistTransitionGrid.Height = targetSize;

        // Update ScaleTransform center to match actual grid size
        ArtistTransitionScale.CenterX = targetSize / 2;
        ArtistTransitionScale.CenterY = targetSize / 2;

        // Set clip based on transition type
        if (_isAlbumTransition)
        {
            ArtistTransitionGrid.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, targetSize, targetSize));
        }
        else
        {
            ArtistTransitionClip.Center = new Point(targetSize / 2, targetSize / 2);
            ArtistTransitionClip.RadiusX = targetSize / 2;
            ArtistTransitionClip.RadiusY = targetSize / 2;
            ArtistTransitionGrid.Clip = ArtistTransitionClip;
        }

        ArtistTransitionScale.ScaleX = 1.0;
        ArtistTransitionScale.ScaleY = 1.0;
        ArtistTransitionTranslate.X = 0.0;
        ArtistTransitionTranslate.Y = 0.0;

        // Step 1: hide the detail page immediately (no fade — hero covers it)
        if (_isAlbumTransition)
        {
            AlbumDetailView.BeginAnimation(OpacityProperty, null);
            AlbumDetailView.Opacity = 0;
        }
        else
        {
            ArtistDetailView.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        // Step 2: after detail fades, go back and morph the element to its grid position
        var goBackTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        goBackTimer.Tick += (_, _) =>
        {
            goBackTimer.Stop();

            LogHelper.LogInfo($"HeroTransition back: pre-goback view='{ViewModel?.CurrentView}' isAlbum={_isAlbumTransition}");

            // Fade in EVERY view container GoBack might reveal. Start the clocks BEFORE
            // GoBack so the revealed view's very first frame is already faded and it ramps
            // to full opacity in sync with the morphing element.
            var duration = TimeSpan.FromMilliseconds(morphMs);
            var morphEase = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var revealEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            foreach (var child in ContentGrid.Children)
            {
                if (child == ArtistTransitionOverlay) continue;
                if (child == ArtistDetailView && !_isAlbumTransition) continue;
                if (child == AlbumDetailView && _isAlbumTransition) continue;
                if (child is not FrameworkElement fe) continue;
                if (fe.Visibility == Visibility.Collapsed)
                {
                    fe.Opacity = 0;
                    var fadeIn = new DoubleAnimation(0, 1.0, duration) { EasingFunction = morphEase };
                    fadeIn.Completed += (_, _) =>
                    {
                        fe.BeginAnimation(OpacityProperty, null);
                        fe.Opacity = 1;
                    };
                    fe.BeginAnimation(OpacityProperty, fadeIn);
                }
            }

            ViewModel?.GoBackCommand.Execute(null);

            LogHelper.LogInfo($"HeroTransition back: post-goback view='{ViewModel?.CurrentView}'");

            // Keep every image inside the just-revealed view hidden until the element
            // lands on its grid slot, then pop them in.
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() =>
                {
                    var revealedImages = new List<Image>();
                    foreach (var child in ContentGrid.Children)
                    {
                        if (child == ArtistTransitionOverlay) continue;
                        if (child == ArtistDetailView && !_isAlbumTransition) continue;
                        if (child == AlbumDetailView && _isAlbumTransition) continue;
                        if (child is FrameworkElement cfe && cfe.Visibility == Visibility.Visible && cfe.IsVisible)
                        {
                            CollectImages(cfe, revealedImages);
                        }
                    }
                    foreach (var img in revealedImages)
                        img.Opacity = 0;
                    LogHelper.LogInfo($"HeroTransition back: hiddenImages={revealedImages.Count}");

                    if (revealedImages.Count > 0)
                    {
                        var revealImagesTimer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
                        revealImagesTimer.Tick += (_, _) =>
                        {
                            revealImagesTimer.Stop();
                            var pop = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = revealEase };
                            pop.Completed += (_, _) =>
                            {
                                foreach (var img in revealedImages)
                                {
                                    img.BeginAnimation(OpacityProperty, null);
                                    img.Opacity = 1;
                                }
                            };
                            foreach (var img in revealedImages)
                                img.BeginAnimation(OpacityProperty, pop);
                        };
                        revealImagesTimer.Start();
                    }
                }));

            // Morph overlay from detail element back to grid position
            double scale = Math.Clamp(_artistTransitionSourceSize.Width / targetSize, 0.1, 1.5);

            var ease = morphEase;

            // ScaleTransform center was set to (targetSize/2, targetSize/2) in forward transition.
            // Translate = sourceCenter - targetCenter compensates for center-based scaling.
            var sourceCenter = new Point(
                _artistTransitionSourcePos.X + _artistTransitionSourceSize.Width / 2,
                _artistTransitionSourcePos.Y + _artistTransitionSourceSize.Height / 2);
            var targetCenter = new Point(
                _artistTransitionTargetPos.X + targetSize / 2,
                _artistTransitionTargetPos.Y + targetSize / 2);

            ArtistTransitionScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
            ArtistTransitionScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
            ArtistTransitionTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(sourceCenter.X - targetCenter.X, duration) { EasingFunction = ease });
            ArtistTransitionTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(sourceCenter.Y - targetCenter.Y, duration) { EasingFunction = ease });

            // Fade the overlay out after morph completes
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150)) { BeginTime = duration, EasingFunction = revealEase };
            fadeOut.Completed += (_, _) =>
            {
                ArtistTransitionOverlay.Visibility = Visibility.Collapsed;
                _isAlbumTransition = false;
                _albumTransitionTrack = null;
                // Re-enable input now that the back animation finished.
                _isBackTransitioning = false;
                if (TransitionInputBlocker != null)
                    TransitionInputBlocker.Visibility = Visibility.Collapsed;
            };
            ArtistTransitionOverlay.BeginAnimation(OpacityProperty, fadeOut);
        };
        goBackTimer.Start();
    }

    private void ArtistAlbum_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MusicTrack dataContext })
            ViewModel?.NavigateToAlbum(dataContext.Album, dataContext.Artist);
    }

    private void AlbumListRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MusicTrack dataContext })
            ViewModel?.NavigateToAlbum(dataContext.Album, dataContext.Artist);
    }

    private void AlbumDetailTrack_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MusicTrack dataContext })
            ViewModel?.PlayTrackCommand.Execute(dataContext);
    }

    private void PlayArtist_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.PlayArtistCommand.Execute(null);
    }

    private void ShuffleArtist_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ShuffleArtistCommand.Execute(null);
    }

    private void AddArtistToQueue_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.AddArtistToQueueCommand.Execute(null);
    }

    private void AddArtistToPlaylist_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && ViewModel.ArtistDetailTracks.Count > 0)
        {
            ViewModel.PendingTracksToAdd = ViewModel.ArtistDetailTracks.ToList();
            ViewModel.OpenPlaylistSelectCommand.Execute(null);
        }
    }

    private void AddAlbumToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.AlbumDetailTracks.Count > 0)
        {
            ViewModel.PendingTracksToAdd = ViewModel.AlbumDetailTracks.ToList();
            ViewModel.OpenPlaylistSelectCommand.Execute(null);
        }
    }

    private void SearchResult_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MusicTrack dataContext })
            ViewModel?.PlayTrackCommand.Execute(dataContext);
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T typed) return typed;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private void QueueItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't start drag/swipe if user clicked directly on an interactive button (play, remove)
        if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null)
        {
            return;
        }

        // Clean up any stale drag if one somehow remained active
        if (_queueIsDragging || _queueDragVisual != null || _queueCapturedElement != null)
        {
            CancelQueueDrag("NewMouseDownWhileActive");
        }

        _queueDragStart = e.GetPosition(null);
        _queuePressActive = true;
        _queueIsDragging = false;
        _queueIsSwiping = false;
        _queueSwipeConsumed = false;
        _queueDragVisual = null;
        _queueDraggedTrack = null;
        _queueDragFromIndex = -1;
        _queueDragOriginalIndex = -1;
        _queueCurrentDropIndex = -1;
        _lastDisplacedTargetIndex = -1;
        StopAutoScroll();

        if (sender is FrameworkElement fe)
        {
            ResetQueueItemVisual(fe);
            _queueCapturedElement = fe;
            // Capture the mouse right away so rapid movements NEVER escape the item!
            fe.CaptureMouse();
        }
        e.Handled = false;
    }

    private void QueueItem_MouseMove(object sender, MouseEventArgs e)
    {
        // Cache the routed pointer position for the (rare) no-GetCursorPos
        // fallback and for swipe initiation. This is skipped once the drag is
        // live: a recycled container under capture answers e.GetPosition with
        // its OLD slot — the source row — and would feed a stale position.
        if (QueueDragCanvas != null && !_queueIsDragging)
            _queueDragCanvasMouse = e.GetPosition(QueueDragCanvas);

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _queuePressActive = false;
            return;
        }

        if (sender is not FrameworkElement fe) return;
        MusicTrack? track = fe.DataContext as MusicTrack;
        if (track == null) return;

        Point pos = e.GetPosition(null);
        double dx = pos.X - _queueDragStart.X;
        double dy = pos.Y - _queueDragStart.Y;
        double adx = Math.Abs(dx);
        double ady = Math.Abs(dy);
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (!_queueIsDragging && !_queueIsSwiping)
        {
            // Initiation needs a live press: after any cleanup (commit/cancel)
            // the physical button may still be held while the gesture is over —
            // without this guard those moves re-arm phantom drags/swipes from
            // the stale anchor (double cancels, ghost visuals, stray removes).
            if (!_queuePressActive) return;
            // Post-drop cooldown: block re-initiation for 300 ms after any drop/cancel
            // so that the MouseMove flooding from ReleaseMouseCapture can't spawn a
            // ghost drag on the row that just received the dropped card.
            if (Environment.TickCount64 < _queueDropCooldownUntil) return;
            // Enforce intentional 6px drag threshold before entering drag state
            if (dist < 6.0) return;

            if (adx > 10 && adx > ady * 1.5 && dx > 0)
            {
                _queueIsSwiping = true;
            }
            else if (dist >= 6.0)
            {
                // Reorder is index-based on the full Queue; a filtered view
                // shows a subset in view order, so drops would land on wrong
                // tracks. Reorder only on the unfiltered list (click to play
                // and swipe to remove are object-based and stay enabled).
                if (ViewModel != null && !string.IsNullOrWhiteSpace(ViewModel.QueueFilterText))
                    return;
                // If the drawer open animation (scale 0.88→1) is still running,
                // do NOT snap to 1.0 instantly — that pop reads as "the queue
                // grew the moment I grabbed it". Spring the remaining distance
                // smoothly instead; a panel that is already at 1.0 just stays.
                if (QueueDrawerScale != null)
                {
                    double curX = QueueDrawerScale.ScaleX;
                    double curY = QueueDrawerScale.ScaleY;
                    if (Math.Abs(curX - 1.0) > 0.001 || Math.Abs(curY - 1.0) > 0.001)
                    {
                        var spring = new System.Windows.Media.Animation.DoubleAnimation(
                            curX, 1.0, TimeSpan.FromMilliseconds(120))
                        {
                            EasingFunction = new System.Windows.Media.Animation.CubicEase
                            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
                            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                        };
                        spring.Completed += (_, _) =>
                        {
                            if (QueueDrawerScale == null) return;
                            QueueDrawerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                            QueueDrawerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                            QueueDrawerScale.ScaleX = 1.0;
                            QueueDrawerScale.ScaleY = 1.0;
                        };
                        QueueDrawerScale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
                        QueueDrawerScale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
                    }
                    else
                    {
                        QueueDrawerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        QueueDrawerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        QueueDrawerScale.ScaleX = 1.0;
                        QueueDrawerScale.ScaleY = 1.0;
                    }
                }
                _queueIsDragging = true;
                _queueDraggedTrack = track;

                // Accurately resolve item index from its container to prevent duplicate-track identity confusion
                var lbi = FindVisualParent<ListBoxItem>(fe);
                int resolvedIndex = (lbi != null && QueueListBox != null)
                    ? QueueListBox.ItemContainerGenerator.IndexFromContainer(lbi)
                    : (ViewModel?.Queue.IndexOf(track) ?? -1);

                _queueDragOriginalIndex = resolvedIndex;
                _queueDragFromIndex = resolvedIndex;
                _queueCurrentDropIndex = resolvedIndex;
                _lastDisplacedTargetIndex = resolvedIndex;
                // Build O(1) track→index cache once — used by RehydrateQueueContainerDisplacement
                // to avoid O(n) Queue.IndexOf on every recycled container during auto-scroll.
                BuildQueueIndexCache();

                // Cache QueueListBox position in canvas space once at DragStart.
                // TranslatePoint goes through the ScaleTransform and is sensitive
                // to every layout pass (ScrollToVerticalOffset triggers one per
                // frame) — reading it each frame made posInListBox.Y jitter by
                // 1-3px even with a motionless cursor, flipping the gap index.
                if (QueueListBox != null && QueueDragCanvas != null)
                {
                    Point lt = QueueListBox.TranslatePoint(new Point(0, 0), QueueDragCanvas);
                    _queueListTopCache = lt.Y;
                    _queueListLeftCache = lt.X;
                    _queueAutoScrollViewer ??= FindScrollViewer(QueueListBox);
                    _queueEstimatedOffset = _queueAutoScrollViewer?.VerticalOffset ?? 0.0;
                }

                StartQueueDragAdorner(fe, track);
                StartAutoScroll();
                fe.Cursor = Cursors.SizeAll;
                // Transfer capture from the row-border to QueueListBox (a permanent
                // non-virtualizing element). The Release fires QueueItem_LostMouseCapture
                // synchronously — suppress its recapture logic during this transfer.
                _isTransferringCapture = true;
                if (fe.IsMouseCaptured) fe.ReleaseMouseCapture();
                if (QueueListBox != null && !QueueListBox.IsMouseCaptured)
                {
                    QueueListBox.CaptureMouse();
                    _queueCapturedElement = QueueListBox;
                }
                _isTransferringCapture = false;
                // Keep the drag cursor over the whole drawer surface (the row
                // border's SizeAll only applies while the pointer is over the
                // row itself — past the bottom edge it reverts to the default
                // arrow right where the gap keeps scrolling).
                if (QueueDrawerOverlay != null) QueueDrawerOverlay.Cursor = Cursors.SizeAll;
                SetQueueItemVisibility(resolvedIndex, hidden: true);
                HideDropPlaceholder();
                LogHelper.LogInfo($"[QUEUE_DRAG] DragStart: track='{track.Title}' index={_queueDragOriginalIndex}");
                return;
            }
        }

        if (_queueIsDragging)
        {
            // QueueListBox_PreviewMouseMove receives all mouse moves for the list and captured pointer.
            // Suppress duplicate calls here to avoid dual updates in the same frame.
            return;
        }

        if (_queueIsSwiping && !_queueSwipeConsumed)
        {
            double offset = Math.Max(0, dx);
            var translate = FindTranslateTransform(fe);
            if (translate != null) translate.X = Math.Min(offset, 170);
            fe.Opacity = 1.0 - Math.Min(offset / 260.0, 0.5);
            if (offset > 100)
            {
                _queueSwipeConsumed = true;
                AnimateSwipeOut(fe, track);
            }
        }
    }

    // List-level move pump for an active drag: tunneling preview fires for any
    // route passing through the ListBox, no matter which element currently
    // holds mouse capture (source row, re-captured fallback, or none after
    // virtualization recycled the source row). Without this, the dragged card
    // freezes wherever updates stopped (e.g. pinned at the bottom) and the
    // drop commits a stale index. Row handlers stay as-is (swipe + initiation).
    private void QueueListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_queueIsDragging || _queueDraggedTrack == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (QueueDragCanvas != null) _queueDragCanvasMouse = e.GetPosition(QueueDragCanvas);
        // While auto-scrolling is active, AdvanceQueueAutoScroll (on CompositionTarget.Rendering)
        // drives the visual updates synchronized to vsync. Suppress redundant mid-frame calls to avoid vibration.
        if (_queueAutoScrollActive) return;
        UpdateQueueDragVisual(_queueDraggedTrack);
    }

    private void QueueItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;

        if (fe.IsMouseCaptured) fe.ReleaseMouseCapture();
        _queueCapturedElement = null;
        _queuePressActive = false;

        if (_queueIsDragging)
        {
            CommitQueueDrag();
            return;
        }

        if (_queueIsSwiping)
        {
            var translate = FindTranslateTransform(fe);
            if (translate != null)
            {
                double currentX = translate.X;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(currentX, 0,
                    TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                translate.BeginAnimation(TranslateTransform.XProperty, anim);
            }
            var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation(fe.Opacity, 1.0,
                TimeSpan.FromMilliseconds(180));
            fe.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            fe.Cursor = Cursors.Hand;
            _queueIsSwiping = false;
            return;
        }

        Point pos = e.GetPosition(null);
        double dx = Math.Abs(pos.X - _queueDragStart.X);
        double dy = Math.Abs(pos.Y - _queueDragStart.Y);
        double dist = Math.Sqrt(dx * dx + dy * dy);
        _queueIsDragging = false;
        _queueIsSwiping = false;

        if (dist < 6.0)
        {
            if (fe.DataContext is MusicTrack t)
            {
                fe.Cursor = Cursors.Hand;
                ViewModel?.PlayQueueTrackCommand.Execute(t);
                return;
            }
        }
    }

    private void QueueItem_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isCommittingDrop || _isCleaningUpDrag) return;
        // Suppress during deliberate capture transfer to QueueListBox at DragStart.
        if (_isTransferringCapture) return;
        // While inactive the OS owns capture churn — verdict belongs to OnWindowActivated.
        if (!IsActive) return;
        if (_queueIsDragging || _queueDragVisual != null || _queueCapturedElement != null)
        {
            // If QueueListBox already holds capture, this LostCapture fired because
            // we deliberately released the row-border during the DragStart transfer.
            // Do NOT recapture to the border — QueueListBox is the intended owner.
            if (_queueIsDragging && QueueListBox != null && QueueListBox.IsMouseCaptured)
            {
                _queueCapturedElement = QueueListBox;
                return;
            }

            // Button still down: capture died because virtualization recycled
            // the source row — re-capture to QueueListBox (stable, non-virtualizing).
            if (_queueIsDragging && Mouse.LeftButton == MouseButtonState.Pressed &&
                _queueDraggedTrack != null && QueueListBox != null)
            {
                try
                {
                    if (!QueueListBox.IsMouseCaptured)
                    {
                        QueueListBox.CaptureMouse();
                        _queueCapturedElement = QueueListBox;
                        LogHelper.LogInfo("[QUEUE_DRAG] Recapture -> QueueListBox");
                        return;
                    }
                }
                catch { }
            }
            LogHelper.LogInfo("[QUEUE_DRAG] DragCancel.LostCapture (QueueItem)");
            CancelQueueDrag("LostCapture");
        }
    }

    private void QueueItem_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            if (_queueIsDragging && _queueDraggedTrack != null)
            {
                RehydrateQueueContainerDisplacement(fe, animateIfTrackMatches: false);
            }
            else
            {
                ResetQueueItemVisual(fe);
            }
        }
    }

    private void Window_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isCommittingDrop || _isCleaningUpDrag) return;
        // While inactive (Alt-Tabbed away) capture loss is expected OS behavior,
        // not a user abort — leave the verdict to OnWindowActivated.
        if (!IsActive) return;
        if (_queueIsDragging || _queueDragVisual != null || _queueCapturedElement != null)
        {
            // A live drag whose button is still down is owned by the row-level
            // handler (re-capture or cancel) — never double-cancel from here.
            if (_queueIsDragging && Mouse.LeftButton == MouseButtonState.Pressed) return;
            LogHelper.LogInfo("[QUEUE_DRAG] DragCancel.LostCapture (Window)");
            CancelQueueDrag("LostCapture");
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // A drag survives deactivation (Alt-Tab mid-gesture): cancelling here
        // is what snapped the card back and reverted the cursor every time the
        // window lost focus. The verdict is deferred to OnWindowActivated,
        // which cancels only if the button was released while away.
        if (_queueIsDragging || _queueDragVisual != null || _queueCapturedElement != null)
        {
            LogHelper.LogInfo("[QUEUE_DRAG] Deactivated during drag — verdict deferred to Activated");
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        // Returning to the window (Alt-Tab back, tray restore) counts as
        // activity: background jobs must cancel instead of meeting the user
        // with CPU-heavy work already in flight.
        SmoothScrollHelper.MarkInteraction();
        // A drag that survived deactivation resumes iff the button is still
        // held; a release while away safely cancels instead.
        if (_queueIsDragging || _queueDragVisual != null || _queueCapturedElement != null)
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                LogHelper.LogInfo("[QUEUE_DRAG] DragCancel.WindowActivated (MouseNotPressed)");
                CancelQueueDrag("WindowActivated");
            }
            else if (_queueIsDragging)
            {
                LogHelper.LogInfo("[QUEUE_DRAG] Resumed after Activated (button held)");
            }
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // Same as deactivation: defer the verdict to OnWindowActivated.
            if (_queueIsDragging || _queueDragVisual != null || _queueCapturedElement != null)
            {
                LogHelper.LogInfo("[QUEUE_DRAG] Minimized during drag — verdict deferred to Activated");
            }
        }
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // Always clear the drop indicator line on any left mouse release
        HideDropPlaceholder();

        if (_queueIsDragging)
        {
            // A release always commits where the CARD points: recompute the gap
            // fresh from the clamped aim point (same ClampDropY the visual uses),
            // so a drop past the edge lands at the edge — never snaps back.
            // Genuine aborts stay explicit: Escape key, drawer close, or a
            // release while away ( arbitrated on re-activation).
            if (QueueListBox != null)
            {
                try
                {
                    double h = QueueListBox.ActualHeight;
                    if (h > 0)
                    {
                        double cardListY = 0;
                        if (_queueDragVisual != null && QueueDragCanvas != null)
                        {
                            double listTop = QueueListBox.TranslatePoint(new Point(0, 0), QueueDragCanvas).Y;
                            cardListY = _queueDragVisual.CurrentY - listTop;
                        }
                        else
                        {
                            cardListY = Mouse.GetPosition(QueueListBox).Y - 28;
                        }
                        double aimY = Math.Max(0, cardListY + 28);
                        _queueCurrentDropIndex = GetQueueGapIndexFromMouse(ClampDropY(aimY, h));
                        LogHelper.LogInfo($"[QUEUE_DRAG] ReleaseDBG from={_queueDragOriginalIndex} gap={_queueCurrentDropIndex} yL={Mouse.GetPosition(QueueListBox).Y:F1} cardListY={cardListY:F1} aim={aimY:F1} listH={h:F1}");
                    }
                }
                catch { }
                CommitQueueDrag();
            }
            else
            {
                CancelQueueDrag("DroppedOutsideQueue");
            }
        }
        else if (_queueDragVisual != null)
        {
            CancelQueueDrag("StaleDragOnMouseUp");
        }
    }

    // The card is clamped inside the list (see dragTop clamp) while the cursor
    // may run past the panel edge on a fast flick. The gap — and the drop —
    // must follow the CARD (what the user sees), so clamp the aim point to the
    // card's travel range: [18, listH-30] mirrors cardTop in [listTop-10,
    // listBottom-58] plus the 28px cursor-to-card offset.
    private static double ClampDropY(double y, double listH)
    {
        double lo = 20.0, hi = listH - 24.0;
        if (hi < lo) return listH / 2.0;
        return Math.Max(lo, Math.Min(hi, y));
    }

    private void UpdateQueueDragVisual(MusicTrack track)
    {
        if (QueueDragCanvas == null || QueueListBox == null || ViewModel == null) return;
        if (_queueDragVisual == null) return;

        Point posInCanvas;
        bool physOutside = true; double physWinY = double.NaN;
        Point physScreen = default;
        bool physKnown = TryGetPhysicalPoint(out physScreen, out physOutside, out physWinY);
        if (physKnown)
        {
            posInCanvas = QueueDragCanvas.PointFromScreen(physScreen);
        }
        else
        {
            posInCanvas = _queueDragCanvasMouse;
            if (double.IsNaN(posInCanvas.X) || double.IsNaN(posInCanvas.Y))
                posInCanvas = Mouse.GetPosition(QueueDragCanvas);
        }

        double listH = QueueListBox.ActualHeight;
        double listW = QueueListBox.ActualWidth;
        double listTop  = double.IsNaN(_queueListTopCache)  ? QueueListBox.TranslatePoint(new Point(0, 0), QueueDragCanvas).Y : _queueListTopCache;
        double listLeft = double.IsNaN(_queueListLeftCache) ? QueueListBox.TranslatePoint(new Point(0, 0), QueueDragCanvas).X : _queueListLeftCache;

        // Bound the card strictly inside QueueListBox boundaries with clean padding
        double visualH = _queueDragVisual.ActualHeight > 0 ? _queueDragVisual.ActualHeight : 56;
        double minDragTop = listTop + 4;
        double maxDragTop = listTop + listH - visualH - 4;
        if (maxDragTop < minDragTop) maxDragTop = minDragTop;
        double dragTop = Math.Clamp(posInCanvas.Y - 28, minDragTop, maxDragTop);

        Point posInListBox = new Point(posInCanvas.X - listLeft, posInCanvas.Y - listTop);
        double centerX = listLeft + 10;

        _queueDragVisual.SetPosition(centerX, dragTop);

        int newIndex = GetQueueGapIndexFromMouse(ClampDropY(posInListBox.Y, listH));
        _queueCurrentDropIndex = newIndex;

        int fromIndex = _queueDragOriginalIndex;
        int rawTarget = newIndex > fromIndex ? newIndex - 1 : newIndex;
        rawTarget = Math.Clamp(rawTarget, 0, Math.Max(0, ViewModel.Queue.Count - 1));

        // Rate-limiter: only commit rawTarget after it has been stable for ≥2 calls during auto-scroll
        int targetIndex;
        if (rawTarget == _queuePendingTargetIndex)
        {
            _queuePendingTargetStable++;
        }
        else
        {
            _queuePendingTargetIndex = rawTarget;
            _queuePendingTargetStable = 1;
        }

        if (_queueAutoScrollActive)
            targetIndex = _queuePendingTargetStable >= 2 ? rawTarget : _lastDisplacedTargetIndex;
        else
            targetIndex = rawTarget;
        if (targetIndex < 0) targetIndex = rawTarget;

        bool targetChanged = targetIndex != _lastDisplacedTargetIndex;
        if (targetChanged)
        {
            _lastDisplacedTargetIndex = targetIndex;
            ApplyNeighborDisplacement(fromIndex, targetIndex, animate: true);
        }
        HideDropPlaceholder();

        // Auto-scroll control: only engage if pointer is near top/bottom and list has room to scroll
        var sv = _queueAutoScrollViewer ??= FindScrollViewer(QueueListBox);
        double maxOff = sv?.ScrollableHeight ?? 0;
        if (maxOff <= 0 && ViewModel.Queue.Count > 0)
        {
            double estimatedTotalH = ViewModel.Queue.Count * 60.0;
            maxOff = Math.Max(0, estimatedTotalH - (sv?.ViewportHeight > 0 ? sv.ViewportHeight : listH));
        }

        double curOff = !double.IsNaN(_queueEstimatedOffset) ? _queueEstimatedOffset : (sv?.VerticalOffset ?? 0);
        bool canScrollUp = curOff > 0.5;
        bool canScrollDown = maxOff > 0 && curOff < maxOff - 0.5;
        bool horizInRange = posInListBox.X >= -60 && posInListBox.X <= listW + 60;

        bool wantScrollUp   = horizInRange && posInListBox.Y < 40 && canScrollUp;
        bool wantScrollDown  = horizInRange && posInListBox.Y > listH - 40 && canScrollDown;

        if (wantScrollUp || wantScrollDown)
        {
            if (!_queueAutoScrollActive)
                StartAutoScroll();
        }
        else
        {
            if (_queueAutoScrollActive)
                StopAutoScroll();
        }
    }

    private static double CalculateExpectedDisplacement(int itemIndex, int fromIndex, int targetIndex)
    {
        if (itemIndex < 0 || fromIndex < 0 || targetIndex < 0 || fromIndex == targetIndex)
            return 0.0;
        if (itemIndex == fromIndex)
            return 0.0;

        const double rowHeight = 60.0;
        if (targetIndex > fromIndex)
        {
            // Dragging DOWN: rows between fromIndex + 1 and targetIndex slide UP by 60px
            if (itemIndex > fromIndex && itemIndex <= targetIndex)
                return -rowHeight;
        }
        else if (targetIndex < fromIndex)
        {
            // Dragging UP: rows between targetIndex and fromIndex - 1 slide DOWN by 60px
            if (itemIndex >= targetIndex && itemIndex < fromIndex)
                return rowHeight;
        }
        return 0.0;
    }

    private void RehydrateQueueContainerDisplacement(FrameworkElement containerOrBorder, bool animateIfTrackMatches)
    {
        if (!_queueIsDragging || ViewModel == null || QueueListBox == null) return;
        var border = containerOrBorder as Border ?? FindQueueItemBorder(containerOrBorder);
        if (border == null) return;

        var track = (border.DataContext ?? (containerOrBorder as FrameworkElement)?.DataContext) as MusicTrack;
        if (track == null) return;

        // If this container hosts the currently dragged track
        if (_queueDraggedTrack != null && ReferenceEquals(track, _queueDraggedTrack))
        {
            if (border.Opacity != 0.0)
            {
                if (border.HasAnimatedProperties) border.BeginAnimation(UIElement.OpacityProperty, null);
                border.Opacity = 0.0;
            }
            // Use cached TranslateTransform
            var translateSource = GetOrCacheTranslateTransform(border);
            if (translateSource != null)
            {
                if (translateSource.HasAnimatedProperties)
                {
                    translateSource.BeginAnimation(TranslateTransform.XProperty, null);
                    translateSource.BeginAnimation(TranslateTransform.YProperty, null);
                }
                if (translateSource.X != 0.0) translateSource.X = 0;
                if (translateSource.Y != 0.0) translateSource.Y = 0;
            }
            border.Tag = null;
            return;
        }
        else
        {
            if (border.Opacity < 1.0)
            {
                if (border.HasAnimatedProperties) border.BeginAnimation(UIElement.OpacityProperty, null);
                border.Opacity = 1.0;
            }
        }

        // O(1) track→index lookup from pre-built drag cache.
        // Falls back to IndexFromContainer + Queue.IndexOf only on a cache miss
        // (first-frame edge case before BuildQueueIndexCache completes).
        int itemIndex;
        if (!_queueIndexCache.TryGetValue(track, out itemIndex) || itemIndex < 0)
        {
            var lbi2 = FindVisualParent<ListBoxItem>(border);
            itemIndex = -1;
            if (lbi2 != null)
                itemIndex = QueueListBox.ItemContainerGenerator.IndexFromContainer(lbi2);
            if (itemIndex < 0)
                itemIndex = ViewModel.Queue.IndexOf(track);
        }
        if (itemIndex < 0) return;

        int fromIndex = _queueDragOriginalIndex;
        int targetIndex = _lastDisplacedTargetIndex;
        double expectedY = CalculateExpectedDisplacement(itemIndex, fromIndex, targetIndex);

        // Use cached TranslateTransform — avoids repeated VisualTree walks
        var translate = GetOrCacheTranslateTransform(border);
        if (translate == null) return;

        bool canAnimate = false;
        if (border.Tag is ValueTuple<MusicTrack, double> prev)
        {
            if (ReferenceEquals(prev.Item1, track))
            {
                if (Math.Abs(prev.Item2 - expectedY) < 0.5 && Math.Abs(translate.Y - expectedY) < 0.5)
                    return;
                canAnimate = animateIfTrackMatches;
            }
        }

        border.Tag = (track, expectedY);

        if (canAnimate && Math.Abs(translate.Y - expectedY) > 0.5)
        {
            // Shared QuinticEaseOut — no GC allocation per animation
            var anim = new System.Windows.Media.Animation.DoubleAnimation(translate.Y, expectedY,
                TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = _gapEasing
            };
            translate.BeginAnimation(TranslateTransform.YProperty, anim);
        }
        else
        {
            if (translate.HasAnimatedProperties)
                translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = expectedY;
        }
    }

    // Build O(1) track→index lookup table. Called once at DragStart.
    // During the drag, Queue order is frozen (commit only on drop), so
    // this snapshot stays valid for the entire drag session.
    private void BuildQueueIndexCache()
    {
        _queueIndexCache.Clear();
        if (ViewModel?.Queue == null) return;
        var q = ViewModel.Queue;
        for (int i = 0; i < q.Count; i++)
            if (q[i] is MusicTrack t) _queueIndexCache[t] = i;
    }

    // Returns only realized (visible) ListBoxItem containers from the VirtualizingStackPanel.
    // This is ~16× faster than iterating all items via ContainerFromIndex(i).
    private IEnumerable<ListBoxItem> GetRealizedQueueContainers()
    {
        _queueVsp ??= FindDescendant<VirtualizingStackPanel>(QueueListBox);
        if (_queueVsp == null) yield break;
        int count = VisualTreeHelper.GetChildrenCount(_queueVsp);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(_queueVsp, i) is ListBoxItem lbi)
                yield return lbi;
        }
    }

    // Returns a cached TranslateTransform for a border — walks VisualTree only on first access.
    private TranslateTransform? GetOrCacheTranslateTransform(Border border)
    {
        if (_translateCache.TryGetValue(border, out var cached)) return cached;
        var tt = border.RenderTransform as TranslateTransform ?? FindTranslateTransform(border);
        if (tt != null) _translateCache.Add(border, tt);
        return tt;
    }

    private void ApplyNeighborDisplacement(int fromIndex, int targetIndex, bool animate)
    {
        if (QueueListBox == null || ViewModel == null) return;
        // Only iterate realized (visible) containers — skips all virtualized rows
        foreach (var container in GetRealizedQueueContainers())
        {
            RehydrateQueueContainerDisplacement(container, animateIfTrackMatches: animate);
        }
    }

    private void ResetAllDisplacements(bool animated = false)
    {
        _lastDisplacedTargetIndex = -1;
        if (QueueListBox == null) return;
        int count = QueueListBox.Items.Count;
        for (int i = 0; i < count; i++)
        {
            if (QueueListBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container)
            {
                var border = FindQueueItemBorder(container);
                if (border == null) continue;
                border.Tag = null;
                var translate = border.RenderTransform as TranslateTransform ?? FindTranslateTransform(border);
                if (translate != null)
                {
                    if (animated && Math.Abs(translate.Y) > 0.5)
                    {
                        var anim = new System.Windows.Media.Animation.DoubleAnimation(translate.Y, 0.0,
                            TimeSpan.FromMilliseconds(120))
                        {
                            EasingFunction = new System.Windows.Media.Animation.CubicEase
                            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };
                        translate.BeginAnimation(TranslateTransform.YProperty, anim);
                    }
                    else
                    {
                        if (translate.HasAnimatedProperties)
                            translate.BeginAnimation(TranslateTransform.YProperty, null);
                        translate.Y = 0;
                    }
                }
            }
        }
        if (!animated)
        {
            ResetVisualTreeQueueItems(QueueListBox);
        }
    }

    private static void ResetVisualTreeQueueItems(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ListBoxItem lbi)
            {
                ResetQueueItemVisual(lbi);
            }
            else
            {
                if (child is Border b && (b.Name == "QueueItemBorder" || b.RenderTransform is TranslateTransform))
                {
                    ResetQueueItemVisual(b);
                }
                ResetVisualTreeQueueItems(child);
            }
        }
    }

    private void CommitQueueDrag()
    {
        if (_isCommittingDrop || _isCleaningUpDrag) return;
        if (!_queueIsDragging || _queueDraggedTrack == null || ViewModel == null)
        {
            CancelQueueDrag("CommitInvalidState");
            return;
        }

        _isCommittingDrop = true;
        _isCleaningUpDrag = true;
        try
        {
            LogHelper.LogInfo("[QUEUE_DRAG] DragDrop");

            StopAutoScroll();
            HideDropPlaceholder();

            int fromIndex = _queueDragOriginalIndex;
            if (fromIndex < 0) fromIndex = ViewModel.Queue.IndexOf(_queueDraggedTrack);

            int gapIndex = _queueCurrentDropIndex;
            int targetIndex = gapIndex > fromIndex ? gapIndex - 1 : gapIndex;
            targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, ViewModel.Queue.Count - 1));

            MusicTrack trackToMove = _queueDraggedTrack;
            var capturedFe = _queueCapturedElement;

            // ── Release capture BEFORE starting any animation so that the flood of
            //    MouseMove events generated by ReleaseMouseCapture is handled while
            //    _isCommittingDrop = true (suppressed) rather than after the flag
            //    is cleared by the Completed callback, which was causing an
            //    immediate phantom DragStart on the destination row.
            _queuePressActive = false;
            _queueIsDragging = false;
            _queueCapturedElement = null;
            if (capturedFe != null)
            {
                capturedFe.Cursor = Cursors.Hand;
                try
                {
                    if (capturedFe.IsMouseCaptured) capturedFe.ReleaseMouseCapture();
                    ResetQueueItemVisual(capturedFe);
                }
                catch { }
            }
            try { Mouse.Capture(null); } catch { }
            if (QueueDrawerOverlay != null) QueueDrawerOverlay.Cursor = null;

            if (fromIndex >= 0 && targetIndex >= 0 && fromIndex != targetIndex)
            {
                LogHelper.LogInfo($"[QUEUE_DRAG] DragDrop: reorder {fromIndex} -> {targetIndex}");
                ViewModel.MoveQueueItem(fromIndex, targetIndex, saveState: true);
            }
            else
            {
                ViewModel.FinalizeQueueDrag();
            }

            if (_queueDragVisual != null && QueueDragCanvas != null && QueueListBox != null)
            {
                ListBoxItem? targetContainer = QueueListBox.ItemContainerGenerator.ContainerFromIndex(targetIndex) as ListBoxItem;
                if (targetContainer != null)
                {
                    Point targetPos = targetContainer.TranslatePoint(new Point(0, 0), QueueDragCanvas);
                    double targetX = targetPos.X + 10;
                    double targetY = targetPos.Y;

                    var translate = _queueDragVisual.Translation;
                    if (translate != null)
                    {
                        double fromX = translate.X;
                        double fromY = translate.Y;
                        // No-op drop: fade in place instead of gliding to origin.
                        if (fromIndex != targetIndex)
                        {
                            var animX = new System.Windows.Media.Animation.DoubleAnimation(fromX, targetX,
                                TimeSpan.FromMilliseconds(120))
                            {
                                EasingFunction = new System.Windows.Media.Animation.CubicEase
                                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                            };
                            var animY = new System.Windows.Media.Animation.DoubleAnimation(fromY, targetY,
                                TimeSpan.FromMilliseconds(120))
                            {
                                EasingFunction = new System.Windows.Media.Animation.CubicEase
                                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                            };
                            translate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animX);
                            translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animY);
                        }
                        var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0,
                            TimeSpan.FromMilliseconds(120));
                        fadeAnim.Completed += (_, _) =>
                        {
                            RevealAllHiddenQueueRows();
                            ResetAllDisplacements(animated: false);
                            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                                () =>
                                {
                                    RevealAllHiddenQueueRows();
                                    ResetAllDisplacements(animated: false);
                                });
                            EndQueueDragAdorner();
                            RefreshQueueHighlight();
                            _isCommittingDrop = false;
                        };
                        _queueDragVisual.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

                        _queueListTopCache = double.NaN;
                        _queueListLeftCache = double.NaN;
                        _queueEstimatedOffset = double.NaN;
                        _queueAutoScrollViewer = null;
                        _queuePendingTargetIndex = -1;
                        _queuePendingTargetStable = 0;
                        _lastDisplacedTargetIndex = -1;
                        _queueDraggedTrack = null;
                        _queueDragFromIndex = -1;
                        _queueDragOriginalIndex = -1;
                        _queueCurrentDropIndex = -1;
                        _queueIndexCache.Clear();
                        // Arm the post-drop cooldown so the MouseMove flood from
                        // ReleaseMouseCapture cannot start a new drag for 300 ms.
                        _queueDropCooldownUntil = Environment.TickCount64 + 300;
                        LogHelper.LogInfo("[QUEUE_DRAG] DragCleanup completed (animated)");
                        return;
                    }
                }
            }

            // Non-animated fallback path (no target container found)
            RevealAllHiddenQueueRows();
            ResetAllDisplacements(animated: false);
            EndQueueDragAdorner();
            // Deferred reset for containers recycled by WPF layout pass after MoveQueueItem
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                () => ResetAllDisplacements(animated: false));

            _queueDraggedTrack = null;
            _queueDragFromIndex = -1;
            _queueDragOriginalIndex = -1;
            _queueCurrentDropIndex = -1;
            _queueIndexCache.Clear();
            _queueListTopCache = double.NaN;
            _queueListLeftCache = double.NaN;
            _queueEstimatedOffset = double.NaN;
            _queueAutoScrollViewer = null;
            _queuePendingTargetIndex = -1;
            _queuePendingTargetStable = 0;
            _lastDisplacedTargetIndex = -1;
            // Arm the post-drop cooldown (non-animated path too)
            _queueDropCooldownUntil = Environment.TickCount64 + 300;
            RefreshQueueHighlight();
            LogHelper.LogInfo("[QUEUE_DRAG] DragCleanup completed");
            _isCommittingDrop = false;
        }
        catch (Exception ex)
        {
            LogHelper.LogError("[QUEUE_DRAG] Exception in CommitQueueDrag", ex);
            CancelQueueDrag("ExceptionInCommit");
        }
        finally
        {
            _isCleaningUpDrag = false;
        }
    }

    public void CancelQueueDrag(string reason)
    {
        if (_isCommittingDrop || _isCleaningUpDrag) return;
        if (!_queueIsDragging && _queueDragVisual == null && _queueDraggedTrack == null && _queueCapturedElement == null && !_queueIsSwiping)
        {
            return;
        }
        // Arm the cooldown on cancel too so that physical-button-still-down
        // moves right after a cancel don't immediately restart the gesture.
        _queueDropCooldownUntil = Environment.TickCount64 + 300;

        _isCleaningUpDrag = true;
        try
        {
            LogHelper.LogInfo($"[QUEUE_DRAG] DragCancel.{reason}");

            StopAutoScroll();
            HideDropPlaceholder();
            EndQueueDragAdorner();
            ResetAllDisplacements(animated: true);

            if (_queueDragOriginalIndex >= 0)
            {
                SetQueueItemVisibility(_queueDragOriginalIndex, hidden: false);
            }
            if (_queueDraggedTrack != null)
            {
                SetQueueItemVisibility(_queueDraggedTrack, hidden: false);
            }

            // Guarantee all realized items in QueueListBox have full opacity restored
            if (QueueListBox != null)
            {
                int count = QueueListBox.Items.Count;
                for (int i = 0; i < count; i++)
                {
                    if (QueueListBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                    {
                        var border = FindQueueItemBorder(item);
                        if (border != null && border.Opacity < 1.0)
                        {
                            border.BeginAnimation(UIElement.OpacityProperty, null);
                            border.Opacity = 1.0;
                        }
                    }
                }
            }

            if (_queueCapturedElement != null)
            {
                try
                {
                    _queueCapturedElement.Cursor = Cursors.Hand;
                    if (_queueCapturedElement.IsMouseCaptured) _queueCapturedElement.ReleaseMouseCapture();
                    ResetQueueItemVisual(_queueCapturedElement);
                }
                catch { }
                _queueCapturedElement = null;
            }
            try { Mouse.Capture(null); } catch { }

            if (QueueDrawerOverlay != null) QueueDrawerOverlay.Cursor = null;

            _queueIsDragging = false;
            _queueIsSwiping = false;
            _queueSwipeConsumed = false;
            _queuePressActive = false;
            _queueDraggedTrack = null;
            _queueDragFromIndex = -1;
            _queueDragOriginalIndex = -1;
            _queueCurrentDropIndex = -1;
            _queuePendingTargetIndex = -1;
            _queuePendingTargetStable = 0;
            _lastDisplacedTargetIndex = -1;
            _queueListTopCache = double.NaN;
            _queueListLeftCache = double.NaN;
            _queueEstimatedOffset = double.NaN;
            _queueAutoScrollViewer = null;
            _queueIndexCache.Clear();

            RefreshQueueHighlight();
            LogHelper.LogInfo("[QUEUE_DRAG] DragCleanup completed");
        }
        catch (Exception ex)
        {
            LogHelper.LogError($"[QUEUE_DRAG] Exception in CancelQueueDrag ({reason})", ex);
        }
        finally
        {
            _isCleaningUpDrag = false;
        }
    }

    // Reveals every queue row whose border was set to Opacity=0 during a drag.
    // Called on both commit and cancel paths. After MoveQueueItem the original
    // index has shifted, so iterating by container is the only reliable way.
    private void RevealAllHiddenQueueRows()
    {
        if (QueueListBox == null) return;
        int count = QueueListBox.Items.Count;
        for (int i = 0; i < count; i++)
        {
            if (QueueListBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                ResetQueueItemVisual(item);
            }
        }
        ResetVisualTreeQueueItems(QueueListBox);
    }

    private void StartQueueDragAdorner(FrameworkElement fe, MusicTrack track)
    {
        if (QueueDragCanvas == null || QueueListBox == null) return;
        // Fit narrow panels: a forced 260+ minimum overflows the list and
        // reads as the panel growing on grab.
        double visualWidth = Math.Max(200, QueueListBox.ActualWidth - 20);
        _queueDragVisual = new QueueDragVisual(track, visualWidth);
        QueueDragCanvas.Children.Add(_queueDragVisual);

        Point posInListBox = QueueListBox.TranslatePoint(new Point(0, 0), QueueDragCanvas);
        double centerX = posInListBox.X + 10;
        double startY;
        try { startY = fe.TranslatePoint(new Point(0, 0), QueueDragCanvas).Y; }
        catch { startY = posInListBox.Y; }

        _queueDragVisual.SetPosition(centerX, startY);
    }

    private void EndQueueDragAdorner()
    {
        if (QueueDragCanvas != null)
        {
            if (_queueDragVisual != null)
            {
                _queueDragVisual.BeginAnimation(UIElement.OpacityProperty, null);
                if (_queueDragVisual.Translation != null)
                {
                    _queueDragVisual.Translation.BeginAnimation(TranslateTransform.XProperty, null);
                    _queueDragVisual.Translation.BeginAnimation(TranslateTransform.YProperty, null);
                }
                QueueDragCanvas.Children.Remove(_queueDragVisual);
                _queueDragVisual = null;
            }

            if (_queueDropIndicatorLine != null)
            {
                QueueDragCanvas.Children.Remove(_queueDropIndicatorLine);
                _queueDropIndicatorLine = null;
            }

            // Remove any leftover QueueDragVisual instances from canvas
            for (int i = QueueDragCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (QueueDragCanvas.Children[i] is QueueDragVisual qdv)
                {
                    QueueDragCanvas.Children.RemoveAt(i);
                }
            }
        }
        else
        {
            _queueDragVisual = null;
        }
    }

    // Hides / shows the row that hosts the given track. We use Opacity (not
    // Visibility) so the gap stays, the row stays hit-testable, and mouse
    // capture on its Border is not lost.
    private void SetQueueItemVisibility(int index, bool hidden)
    {
        if (QueueListBox == null || index < 0) return;
        var container = QueueListBox.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;
        if (container == null) return;
        var border = FindQueueItemBorder(container);
        if (border == null) return;
        border.BeginAnimation(UIElement.OpacityProperty, null);
        border.Opacity = hidden ? 0.0 : 1.0;
    }

    private void SetQueueItemVisibility(MusicTrack track, bool hidden)
    {
        if (track == null || QueueListBox == null) return;
        var container = QueueListBox.ItemContainerGenerator.ContainerFromItem(track) as ListBoxItem;
        if (container == null) return;
        var border = FindQueueItemBorder(container);
        if (border == null) return;
        border.BeginAnimation(UIElement.OpacityProperty, null);
        border.Opacity = hidden ? 0.0 : 1.0;
    }

    private static Border? FindQueueItemBorder(DependencyObject? parent)
    {
        if (parent == null) return null;
        if (parent is ListBoxItem lbi && lbi.Template != null)
        {
            if (lbi.Template.FindName("QueueItemBorder", lbi) is Border directBorder)
                return directBorder;
        }
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border b && (b.Name == "QueueItemBorder" || b.RenderTransform is TranslateTransform))
            {
                return b;
            }
            var found = FindQueueItemBorder(child);
            if (found != null) return found;
        }
        return null;
    }

    private static void ResetQueueItemVisual(FrameworkElement fe)
    {
        fe.Tag = null;
        if (!fe.IsHitTestVisible) fe.IsHitTestVisible = true;
        var border = fe as Border ?? FindQueueItemBorder(fe);
        if (border != null)
        {
            if (!border.IsHitTestVisible) border.IsHitTestVisible = true;
            border.Tag = null;
            if (border.HasAnimatedProperties)
                border.BeginAnimation(UIElement.OpacityProperty, null);
            border.Opacity = 1.0;
            var borderTranslate = border.RenderTransform as TranslateTransform ?? FindTranslateTransform(border);
            if (borderTranslate != null)
            {
                if (borderTranslate.HasAnimatedProperties)
                {
                    borderTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                    borderTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                }
                if (borderTranslate.X != 0.0) borderTranslate.X = 0;
                if (borderTranslate.Y != 0.0) borderTranslate.Y = 0;
            }
        }
        if (fe.HasAnimatedProperties)
            fe.BeginAnimation(UIElement.OpacityProperty, null);
        fe.Opacity = 1.0;
        var translate = FindTranslateTransform(fe);
        if (translate != null)
        {
            if (translate.HasAnimatedProperties)
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
            }
            if (translate.X != 0.0) translate.X = 0;
            if (translate.Y != 0.0) translate.Y = 0;
        }
    }

    // Applies the "now playing" highlight + equalizer / play affordance to every
    // realized row. Also resets any recycled swipe state.
    private void RefreshQueueHighlight()
    {
        if (QueueListBox == null || ViewModel == null) return;
        if (_queueIsDragging) return;
        var current = ViewModel.CurrentTrack;
        bool playing = ViewModel.IsPlaying;
        int n = QueueListBox.Items.Count;
        for (int i = 0; i < n; i++)
        {
            if (QueueListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container) continue;
            if (container is FrameworkElement fe) ResetQueueItemVisual(fe);

            var track = container.DataContext as MusicTrack;
            bool isCurrent = current != null && track != null && track.Id == current.Id;
            var border = FindQueueItemBorder(container);
            if (border != null)
            {
                if (border.HasAnimatedProperties)
                    border.BeginAnimation(UIElement.OpacityProperty, null);
                if (border.Opacity != 1.0) border.Opacity = 1.0;
                if (!ReferenceEquals(border.Background, Brushes.Transparent))
                    border.Background = Brushes.Transparent;
            }

            var eq = container.FindName("QueueEqIcon") as FrameworkElement;
            var play = container.FindName("QueueBtnPlay") as FrameworkElement;
            bool showEq = isCurrent && playing;
            if (eq != null) eq.Visibility = showEq ? Visibility.Visible : Visibility.Collapsed;
            if (play != null) play.Visibility = showEq ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // Geometric gap index: returns the insertion gap [0..Count] where the
    // dragged row should land. Rows are uniform (56px + 2px margins = 60px
    // pitch, no top padding), so the gap maps directly from the cursor Y plus
    // the scroll offset — valid for realized AND unrealized regions. Probing
    // realized containers capped the gap at the realization edge, so fast
    // drops below the visible rows landed too shallow. Convention matches the
    // old midpoint rule: gap = first row whose midpoint is below the cursor.
    private int GetQueueGapIndexFromMouse(double mouseYInListBox)
    {
        if (ViewModel == null || QueueListBox == null) return 0;
        int count = ViewModel.Queue.Count;
        if (count == 0) return 0;

        const double RowPitch = 60.0;
        const double HalfRow = 30.0;
        double offset = 0.0;
        try
        {
            var sv = _queueAutoScrollViewer ??= FindScrollViewer(QueueListBox);
            if (sv != null)
            {
                offset = !double.IsNaN(_queueEstimatedOffset) ? _queueEstimatedOffset : sv.VerticalOffset;
            }
        }
        catch { }

        double totalY = mouseYInListBox + offset;
        int rawGap = (int)Math.Floor((totalY + HalfRow) / RowPitch);
        rawGap = Math.Clamp(rawGap, 0, count);

        // Hysteresis deadband: prevent pointer micro-movements near row boundaries from flipping gap back and forth
        if (_queueCurrentDropIndex >= 0 && Math.Abs(rawGap - _queueCurrentDropIndex) == 1)
        {
            double boundaryY = (Math.Max(rawGap, _queueCurrentDropIndex) * RowPitch) - HalfRow;
            if (Math.Abs(totalY - boundaryY) < 14.0)
            {
                return _queueCurrentDropIndex;
            }
        }

        return rawGap;
    }

    private void ShowDropPlaceholder(int gapIndex)
    {
        // Drop indicator line disabled - neighbors shift shows position visually
    }

    private void HideDropPlaceholder()
    {
        if (_queueDropIndicatorLine != null && QueueDragCanvas != null)
        {
            QueueDragCanvas.Children.Remove(_queueDropIndicatorLine);
            _queueDropIndicatorLine = null;
        }
    }

    private void StartAutoScroll()
    {
        if (_queueAutoScrollActive) return;
        _queueAutoScrollActive = true;
        _queueScrollVelocity = 0.0; // start fresh — velocity accelerates from zero
        EnsurePerfRenderActive();
    }

    // Driven from the rendering loop at vsync cadence. Elapsed-time scaled so
    // the auto-scroll speed matches the old 25ms-tick behavior at any refresh.
    private void AdvanceQueueAutoScroll(double dtMs)
    {
        if (QueueListBox == null || _queueDragVisual == null || !_queueIsDragging)
        {
            StopAutoScroll();
            return;
        }

        Point canvasPos;
        if (TryGetPhysicalPoint(out Point physPt, out _, out _))
            canvasPos = QueueDragCanvas.PointFromScreen(physPt);
        else if (!double.IsNaN(_queueDragCanvasMouse.X))
            canvasPos = _queueDragCanvasMouse;
        else
            canvasPos = Mouse.GetPosition(QueueDragCanvas);

        double listTopC  = double.IsNaN(_queueListTopCache)  ? QueueListBox.TranslatePoint(new Point(0, 0), QueueDragCanvas).Y : _queueListTopCache;
        double listLeftC = double.IsNaN(_queueListLeftCache) ? QueueListBox.TranslatePoint(new Point(0, 0), QueueDragCanvas).X : _queueListLeftCache;
        Point mousePos = new Point(canvasPos.X - listLeftC, canvasPos.Y - listTopC);
        double listH = QueueListBox.ActualHeight;
        double listW = QueueListBox.ActualWidth;
        double scrollSpeed = 0;

        bool horizInRange = mousePos.X >= -60 && mousePos.X <= listW + 60;

        // Velocity-based acceleration: starts slow, ramps up the longer the pointer
        // stays at the edge — AND faster the deeper the mouse is into the edge zone.
        // edgeFactor 0..1 = how far into the zone (0=boundary, 1=extreme edge/outside).
        // AccelPerSec is multiplied by (0.4 + edgeFactor * 1.6) → 0.4×..2.0× base rate.
        const double VelocityMin   = 0.8;   // initial speed (rows/25ms)
        const double VelocityMax   = 9.0;   // cap (~540 px/s at 60 Hz)
        const double AccelPerSec   = 4.5;   // base velocity gained per second at edge
        const double DecelPerSec   = 18.0;  // velocity lost per second off-edge
        const double EdgeZone      = 48.0;  // pixels from top/bottom that trigger scroll

        int    edgeDir    = 0;
        double edgeFactor = 0.0; // 0 = just entered zone, 1 = fully at extreme

        if (horizInRange)
        {
            if (mousePos.Y < EdgeZone)
            {
                edgeDir    = -1;
                edgeFactor = Math.Clamp(1.0 - mousePos.Y / EdgeZone, 0.0, 1.0);
            }
            else if (mousePos.Y > listH - EdgeZone)
            {
                edgeDir    = 1;
                edgeFactor = Math.Clamp((mousePos.Y - (listH - EdgeZone)) / EdgeZone, 0.0, 1.0);
            }
        }
        // Also handle mouse outside list bounds (past top/bottom) as maximum factor
        if (!horizInRange || mousePos.Y < 0 || mousePos.Y > listH)
            edgeFactor = Math.Clamp(edgeFactor, 0.8, 1.0);

        if (edgeDir != 0)
        {
            // At edge: accelerate, with rate proportional to depth in zone
            _queueScrollDir = edgeDir;
            double accelRate = AccelPerSec * (0.4 + edgeFactor * 1.6);
            if (_queueScrollVelocity < VelocityMin)
                _queueScrollVelocity = VelocityMin;
            _queueScrollVelocity = Math.Min(VelocityMax,
                _queueScrollVelocity + accelRate * dtMs / 1000.0);
            scrollSpeed = edgeDir * _queueScrollVelocity;
        }
        else if (_queueScrollVelocity > 0.01)
        {
            // Off-edge but still has velocity: decelerate gradually and keep scrolling
            _queueScrollVelocity = Math.Max(0.0,
                _queueScrollVelocity - DecelPerSec * dtMs / 1000.0);
            scrollSpeed = _queueScrollDir * _queueScrollVelocity;
        }
        else
        {
            // Velocity fully drained: truly stop
            _queueScrollVelocity = 0.0;
        }

        if (Math.Abs(scrollSpeed) > 0)
        {
            var sv = _queueAutoScrollViewer ??= FindScrollViewer(QueueListBox);
            if (sv != null)
            {
                double maxOff = sv.ScrollableHeight;
                if (maxOff <= 0 && ViewModel?.Queue.Count > 0)
                {
                    double estimatedTotalH = ViewModel.Queue.Count * 60.0;
                    maxOff = Math.Max(0, estimatedTotalH - (sv.ViewportHeight > 0 ? sv.ViewportHeight : listH));
                }

                double currentOffset = !double.IsNaN(_queueEstimatedOffset) ? _queueEstimatedOffset : sv.VerticalOffset;
                double target = currentOffset + scrollSpeed * (dtMs / 25.0);

                if (scrollSpeed < 0 && target <= 0.5)
                {
                    _queueEstimatedOffset = 0;
                    sv.ScrollToVerticalOffset(0);
                    StopAutoScroll();
                    if (_queueDraggedTrack != null) UpdateQueueDragVisual(_queueDraggedTrack);
                    return;
                }
                if (scrollSpeed > 0 && maxOff > 0 && target >= maxOff - 0.5)
                {
                    _queueEstimatedOffset = maxOff;
                    sv.ScrollToVerticalOffset(maxOff);
                    StopAutoScroll();
                    if (_queueDraggedTrack != null) UpdateQueueDragVisual(_queueDraggedTrack);
                    return;
                }

                target = Math.Clamp(target, 0, Math.Max(0, maxOff));
                _queueEstimatedOffset = target;
                sv.ScrollToVerticalOffset(target);
            }

            if (_queueDragVisual != null && QueueDragCanvas != null)
            {
                double visualH = _queueDragVisual.ActualHeight > 0 ? _queueDragVisual.ActualHeight : 56;
                double minDragTop = listTopC + 4;
                double maxDragTop = listTopC + listH - visualH - 4;
                if (maxDragTop < minDragTop) maxDragTop = minDragTop;
                double dragTop = Math.Clamp(canvasPos.Y - 28, minDragTop, maxDragTop);
                _queueDragVisual.SetPosition(listLeftC + 10, dragTop);
            }

            // Maintain gap displacement synchronized during auto-scroll
            int newIndex = GetQueueGapIndexFromMouse(ClampDropY(mousePos.Y, listH));
            _queueCurrentDropIndex = newIndex;
            int fromIndex = _queueDragOriginalIndex;
            int rawTarget = newIndex > fromIndex ? newIndex - 1 : newIndex;
            rawTarget = Math.Clamp(rawTarget, 0, Math.Max(0, (ViewModel?.Queue.Count ?? 1) - 1));

            if (rawTarget != _lastDisplacedTargetIndex)
            {
                _lastDisplacedTargetIndex = rawTarget;
                // Snap displacement directly during auto-scroll to avoid animation lag/thrashing
                ApplyNeighborDisplacement(fromIndex, rawTarget, animate: false);
            }
        }
        else if (_queueScrollVelocity <= 0.01)
        {
            // Velocity fully drained and not at edge — stop auto-scroll
            StopAutoScroll();
        }
        // else: still decelerating with remaining velocity, continue next frame
    }

    private ScrollViewer? _queueAutoScrollViewer;

    private void UpdateAutoScroll(double mouseYInListBox, double listHeight)
    {
    }

    private void StopAutoScroll()
    {
        if (!_queueAutoScrollActive) return;
        _queueAutoScrollActive = false;
        _queueScrollVelocity = 0.0;  // reset velocity so next auto-scroll starts fresh
        var sv = _queueAutoScrollViewer ??= FindScrollViewer(QueueListBox);
        if (sv != null) _queueEstimatedOffset = sv.VerticalOffset;

        UpdatePerfRenderAfterMarqueeStop();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            ScrollViewer? found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    private void AnimateSwipeOut(FrameworkElement fe, MusicTrack track)
    {
        var translate = FindTranslateTransform(fe);
        if (translate == null)
        {
            ViewModel?.RemoveFromQueueCommand.Execute(track);
            return;
        }

        double currentX = translate.X;
        var slideAnim = new System.Windows.Media.Animation.DoubleAnimation(currentX, 500,
            TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        };
        var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation(fe.Opacity, 0,
            TimeSpan.FromMilliseconds(200));

        fadeAnim.Completed += (_, _) =>
        {
            ResetQueueItemVisual(fe);
            ViewModel?.RemoveFromQueueCommand.Execute(track);
            RefreshQueueHighlight();
        };

        fe.IsHitTestVisible = false;
        translate.BeginAnimation(TranslateTransform.XProperty, slideAnim);
        fe.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
    }

    private void QueueListBox_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void QueueListBox_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel == null) return;
        if (e.Data.GetDataPresent(typeof(MusicTrack)) && sender is ListBox lb)
        {
            MusicTrack? dropped = e.Data.GetData(typeof(MusicTrack)) as MusicTrack;
            if (dropped == null) return;
            int fromIndex = ViewModel.Queue.IndexOf(dropped);
            if (fromIndex < 0) return;
            int toIndex = GetQueueDropIndex(lb, e);
            if (toIndex < 0) toIndex = ViewModel.Queue.Count - 1;
            if (toIndex == fromIndex || toIndex == fromIndex + 1) return;
            ViewModel.MoveQueueItem(fromIndex, toIndex > fromIndex ? toIndex - 1 : toIndex);
        }
        e.Handled = true;
    }

    private void ClearQueueFilter_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            ViewModel.QueueFilterText = string.Empty;
    }

    private void OnScrollToCurrentTrackRequested()
    {
        if (QueueListBox == null || ViewModel?.CurrentTrack == null) return;
        var current = ViewModel.CurrentTrack;
        QueueListBox.ScrollIntoView(current);
        RefreshQueueHighlight();
    }

    // A recycled container keeps its visuals (Recycling reuses the whole subtree
    // including RenderTransforms). Without this, rows that scrolled out mid-drag
    // re-enter later with stale displacement offsets / hidden opacity / stale gap
    // Tags — overlapping text, invisible rows, poisoned drop math. Clean EVERY
    // container as it leaves realization so reuse always starts pristine. The
    // dragged row's own hide is re-applied independently on every pump by
    // ApplyNeighborDisplacement, so no container ever carries or loses it here.
    private void QueueListBox_CleanUpVirtualizedItem(object sender, System.Windows.Controls.CleanUpVirtualizedItemEventArgs e)
    {
        try
        {
            if (e.UIElement is FrameworkElement fe)
                ResetQueueItemVisual(fe);
        }
        catch { }
    }

    private void QueueListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (QueueListBox == null || ViewModel == null) return;
        var selectedTrack = QueueListBox.SelectedItem as MusicTrack;
        if (selectedTrack == null) return;

        int index = ViewModel.Queue.IndexOf(selectedTrack);
        if (index < 0) return;

        if (e.Key == Key.Delete)
        {
            ViewModel.RemoveFromQueueCommand.Execute(selectedTrack);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            if (e.Key == Key.Up && index > 0)
            {
                ViewModel.MoveQueueItem(index, index - 1);
                QueueListBox.SelectedItem = selectedTrack;
                QueueListBox.ScrollIntoView(selectedTrack);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down && index < ViewModel.Queue.Count - 1)
            {
                ViewModel.MoveQueueItem(index, index + 1);
                QueueListBox.SelectedItem = selectedTrack;
                QueueListBox.ScrollIntoView(selectedTrack);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter)
        {
            ViewModel.PlayQueueTrackCommand.Execute(selectedTrack);
            e.Handled = true;
        }
    }

    private void AnimateQueueDrawerToggle(bool open)
    {
        if (QueueDrawerOverlay == null || QueueDrawerPanel == null || QueueDrawerBackdrop == null) return;

        var scale = QueueDrawerScale;
        if (scale == null) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (open)
        {
            QueueDrawerOverlay.Visibility = Visibility.Visible;

            // Scale: 0.88 → 1
            var scaleAnim = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            // Fade panel in: 0 → 1
            QueueDrawerPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });

            // Backdrop fade: 0 → 1
            QueueDrawerBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                RefreshQueueHighlight();
                if (ViewModel?.CurrentTrack != null)
                {
                    QueueListBox?.ScrollIntoView(ViewModel.CurrentTrack);
                }
            });
        }
        else
        {
            CancelQueueDrag("QueueDrawerClosing");
            var closeEase = new CubicEase { EasingMode = EasingMode.EaseIn };
            var dur = TimeSpan.FromMilliseconds(200);

            // Scale: 1 → 0.88
            var scaleOut = new DoubleAnimation(1.0, 0.88, dur) { EasingFunction = closeEase };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);

            // Fade panel out: 1 → 0
            var fadeOut = new DoubleAnimation(QueueDrawerPanel.Opacity, 0, dur) { EasingFunction = closeEase };
            fadeOut.Completed += (_, _) =>
            {
                QueueDrawerOverlay.Visibility = Visibility.Collapsed;
                // Reset for next open
                scale.ScaleX = 0.88;
                scale.ScaleY = 0.88;
            };
            QueueDrawerPanel.BeginAnimation(OpacityProperty, fadeOut);

            // Backdrop fade
            QueueDrawerBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(QueueDrawerBackdrop.Opacity, 0, dur) { EasingFunction = closeEase });
        }
    }

    private static TranslateTransform? FindTranslateTransform(FrameworkElement fe)
    {
        if (fe.RenderTransform is TranslateTransform t1) return t1;
        return FindTranslateInTree(fe);
    }

    private static TranslateTransform? FindTranslateInTree(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement childFe)
            {
                if (childFe.RenderTransform is TransformGroup tg)
                    return tg.Children.OfType<TranslateTransform>().FirstOrDefault();
                if (childFe.RenderTransform is TranslateTransform t)
                    return t;
            }
            TranslateTransform? found = FindTranslateInTree(child);
            if (found != null) return found;
        }
        return null;
    }

    private static int GetQueueDropIndex(ListBox lb, DragEventArgs e)
    {
        int count = lb.Items.Count;
        if (count <= 0) return 0;
        for (int i = 0; i < count; i++)
        {
            ListBoxItem? item = lb.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
            if (item == null) continue;
            Rect bounds = VisualTreeHelper.GetDescendantBounds(item);
            Point pos = e.GetPosition(item);
            if (pos.Y < bounds.Top + bounds.Height / 2)
                return i;
        }
        return count - 1;
    }

    private void MiniPlayer_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.OpenFullPlayerCommand.Execute(null);
    }
    private void CloseModal_Click(object sender, RoutedEventArgs e) => ViewModel?.CloseTopmostOverlay();
    private void ModalBackdrop_Click(object sender, MouseButtonEventArgs e) => ViewModel?.CloseTopmostOverlay();

    private void SpeedPreset_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && ViewModel != null)
            ViewModel.SetSpeedPresetCommand.Execute(tag);
    }

    private void SpeedTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var tb = sender as TextBox;
            if (tb != null && ViewModel != null)
            {
                ViewModel.SpeedText = tb.Text;
                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (sender is TextBox t && ViewModel != null)
                t.Text = ViewModel.SpeedText;
            e.Handled = true;
        }
    }

    private void PitchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var tb = sender as TextBox;
            if (tb != null && ViewModel != null)
            {
                ViewModel.PitchText = tb.Text;
                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (sender is TextBox t && ViewModel != null)
                t.Text = ViewModel.PitchText;
            e.Handled = true;
        }
    }

    private void SpeedPitchReset_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ResetSpeedPitchCommand.Execute(null);
    }

    private bool _resumePlaybackAfterSeek;
    private bool _seekScrubActive;
    private bool _seekScrubPaused;
    private Point _seekScrubStart;

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel == null || ViewModel.Duration.TotalSeconds <= 0) return;
        var slider = (Slider)sender;

        // Grabbing the thumb itself → normal drag (handled by DragStarted/DragCompleted)
        if (TryGetSeekThumb(slider, out var thumb) && IsPointInElement(thumb, e.GetPosition(thumb)))
            return;

        // Click anywhere on the bar → jump there, and scrub (with pause) if the user drags.
        var pct = Math.Clamp(e.GetPosition(slider).X / Math.Max(1, slider.ActualWidth) * 100.0, 0, 100);
        slider.Value = pct; // TwoWay binding → PlaybackProgress setter → Seek()
        _seekScrubStart = e.GetPosition(slider);
        _seekScrubActive = true;
        _seekScrubPaused = false;
        slider.CaptureMouse();
        e.Handled = true;
    }

    private void SeekSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_seekScrubActive || ViewModel == null) return;
        var slider = (Slider)sender;
        if (!slider.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(slider);

        // Enter scrub-drag mode once the pointer actually moves (small threshold).
        if (!_seekScrubPaused && Math.Abs(pos.X - _seekScrubStart.X) > 5)
        {
            _seekScrubPaused = true;
            ViewModel.IsSeeking = true;
            _resumePlaybackAfterSeek = ViewModel.IsPlaying;
            if (ViewModel.IsPlaying)
                ViewModel.PlayPauseCommand.Execute(null);
        }

        if (_seekScrubPaused)
        {
            var pct = Math.Clamp(pos.X / Math.Max(1, slider.ActualWidth) * 100.0, 0, 100);
            slider.Value = pct; // IsSeeking setter → live Position preview only
        }
    }

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seekScrubActive) return;
        _seekScrubActive = false;
        var slider = (Slider)sender;
        slider.ReleaseMouseCapture();

        if (_seekScrubPaused && ViewModel != null)
        {
            _seekScrubPaused = false;
            ViewModel.IsSeeking = false;
            ViewModel.PlaybackProgress = slider.Value; // Apply the chosen position
            if (_resumePlaybackAfterSeek)
            {
                _resumePlaybackAfterSeek = false;
                ViewModel.PlayPauseCommand.Execute(null);
            }
        }
    }

    private void SeekSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.IsSeeking = true;
        _resumePlaybackAfterSeek = ViewModel.IsPlaying;
        if (ViewModel.IsPlaying)
            ViewModel.PlayPauseCommand.Execute(null);
    }

    private void SeekSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.IsSeeking = false;
        ViewModel.PlaybackProgress = ((Slider)sender).Value; // Apply the final chosen position
        if (_resumePlaybackAfterSeek)
        {
            _resumePlaybackAfterSeek = false;
            ViewModel.PlayPauseCommand.Execute(null);
        }
    }

    private bool TryGetSeekThumb(Slider slider, out Thumb? thumb)
    {
        thumb = slider.Template?.FindName("PART_Thumb", slider) as Thumb;
        return thumb != null;
    }

    private static bool IsPointInElement(FrameworkElement? element, Point point)
        => element != null
           && point.X >= 0 && point.Y >= 0
           && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ignore keyboard navigation while the back animation is running.
        if (_isBackTransitioning)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12)
        {
            string diag = SmoothScrollHelper.DumpDiagnostics();
            long gc0 = System.GC.CollectionCount(0), gc1 = System.GC.CollectionCount(1), gc2 = System.GC.CollectionCount(2);
            long mem = System.GC.GetTotalMemory(false) / 1024;
            diag += $" GC: gen0={gc0} gen1={gc1} gen2={gc2} memKB={mem}";
            diag += $" win={ActualWidth:F0}x{ActualHeight:F0}";

            try
            {
                int realized = 0;
                if (AllSongsListBox != null)
                {
                    var gen = AllSongsListBox.ItemContainerGenerator;
                    for (int i = 0; i < AllSongsListBox.Items.Count; i++)
                    {
                        if (gen.ContainerFromIndex(i) != null) realized++;
                    }
                }
                double vpH = 0;
                var sv = FindDescendant<ScrollViewer>(AllSongsListBox);
                if (sv != null) vpH = sv.ViewportHeight;
                diag += $" realized={realized} vpH={vpH:F0}";
            }
            catch { }

            try
            {
                var tier = System.Windows.Media.RenderCapability.Tier >> 16;
                diag += $" gpuTier={tier}";
                diag += $" blur={ViewModel?.WallpaperBlur:F1}";
            }
            catch { }

            LogHelper.LogInfo(diag);
            System.Diagnostics.Debug.WriteLine(diag);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_queueIsDragging || _queueDragVisual != null || _queueCapturedElement != null)
            {
                LogHelper.LogInfo("[QUEUE_DRAG] DragCancel.Escape");
                CancelQueueDrag("Escape");
                e.Handled = true;
                return;
            }

            if (ViewModel != null)
            {
                ViewModel.GoBackCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void Window_PreviewMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.ShowLyricsSettings == true && LyricsSettingsPanel != null)
        {
            var pos = e.GetPosition(LyricsSettingsPanel);
            if (pos.X < 0 || pos.Y < 0 || pos.X > LyricsSettingsPanel.ActualWidth || pos.Y > LyricsSettingsPanel.ActualHeight)
            {
                ViewModel.ShowLyricsSettings = false;
                AnimateLyricsSettingsPanel();
            }
        }
    }

    private void DrawerBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null) ViewModel.IsDrawerOpen = false;
    }

    private void DrawerHome_Click(object sender, RoutedEventArgs e) => ViewModel?.NavigateCommand.Execute("home");

    private void LyricLine_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LyricLine dataContext })
            ViewModel?.SeekToLyricCommand.Execute(dataContext);
    }

    private void PlaylistRename_Click(object sender, RoutedEventArgs e)
    {
        var p = GetPlaylistFromMenuItem(sender);
        if (p == null || ViewModel == null) return;
        ViewModel.RenamingPlaylist = p;
        ViewModel.RenamePlaylistName = p.Name;
        ViewModel.ShowRenamePlaylistOverlay = true;
        Dispatcher.BeginInvoke(() =>
        {
            RenamePlaylistNameBox?.SelectAll();
            RenamePlaylistNameBox?.Focus();
        });
    }

    private void PlaylistContextPlay_Click(object sender, RoutedEventArgs e)
    {
        var p = GetPlaylistFromMenuItem(sender);
        if (p != null)
            ViewModel?.PlayPlaylistCommand.Execute(p);
    }

    private void PlaylistContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var p = GetPlaylistFromMenuItem(sender);
        if (p != null)
            ViewModel?.DeletePlaylistCommand.Execute(p);
    }

    private static PlaylistViewModel? GetPlaylistFromMenuItem(object sender)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: PlaylistViewModel vm } } })
            return vm;
        return null;
    }

    private void PlaylistSelect_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlaylistViewModel dataContext })
            ViewModel?.SelectPlaylistForAddCommand.Execute(dataContext);
    }

    private void LrcFile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string dataContext })
            ViewModel?.BindLyricFileCommand.Execute(dataContext);
    }

    private void ThemePreset_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string dataContext })
            ViewModel?.ApplyThemeCommand.Execute(dataContext);
    }

    private void WallpaperItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MusicPlayer.Models.WallpaperItem item })
            ViewModel?.SelectWallpaperCommand.Execute(item);
    }

    private void WallpaperCategory_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string category })
            ViewModel?.SelectWallpaperCategoryCommand.Execute(category);
    }

    private void AudioEditorExport_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Audio Editor: Select a track and use Trim start/end to export a 10s clip (demo: uses NAudio WaveFileWriter).", "Audio Editor");
    }

    // ─── More options popup (⋮ button) ────────────────────────────────
    private void MoreOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        MoreOptionsPanel.BeginAnimation(OpacityProperty, null);
        MoreOptionsPanel.Opacity = 1;
        MoreOptionsTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        MoreOptionsTranslate.Y = 300;
        MoreOptionsPanel.Visibility = Visibility.Visible;
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(280))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        MoreOptionsTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, anim);
    }

    private void TrackMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MusicTrack dataContext })
            ViewModel!.MoreOptionsContextTrack = dataContext;
        MoreOptionsBtn_Click(sender, e);
    }

    private void CloseMoreOptionsPopup()
    {
        if (MoreOptionsPanel.Visibility != Visibility.Visible) return;
        var closeTranslate = new System.Windows.Media.Animation.DoubleAnimation(0, 300, TimeSpan.FromMilliseconds(280))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            MoreOptionsPanel.BeginAnimation(OpacityProperty, null);
            MoreOptionsPanel.Opacity = 1;
            MoreOptionsTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            MoreOptionsTranslate.Y = 300;
            MoreOptionsPanel.Visibility = Visibility.Collapsed;
        };
        MoreOptionsTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, closeTranslate);
        MoreOptionsPanel.BeginAnimation(OpacityProperty, fade);
    }

    private void MoreOptionsBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
        CloseMoreOptionsPopup();
    }

    private void MoreOptionShare_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        var t = ViewModel?.MoreOptionsContextTrack ?? ViewModel?.CurrentTrack;
        if (t == null) return;
        var data = new DataObject(DataFormats.UnicodeText, $"{t.Title} - {t.Artist}");
        System.Windows.Clipboard.SetDataObject(data);
        MessageBox.Show("Track info copied to clipboard.", "Share");
    }

    private void MoreOptionInfo_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        var t = ViewModel?.MoreOptionsContextTrack ?? ViewModel?.CurrentTrack;
        if (t == null) return;
        MessageBox.Show($"Title: {t.Title}\nArtist: {t.Artist}\nAlbum: {t.Album}\nDuration: {t.Duration:mm\\:ss}\nPath: {t.FilePath}", "Track Info");
    }

    private void MoreOptionFavorite_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        var t = ViewModel?.MoreOptionsContextTrack ?? ViewModel?.CurrentTrack;
        ViewModel?.ToggleFavoriteForTrack(t);
    }

    private void MoreOptionPlayNext_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        var t = ViewModel?.MoreOptionsContextTrack ?? ViewModel?.CurrentTrack;
        if (t == null) return;
        MessageBox.Show("Play next: coming soon.", "Play Next");
    }

    private void MoreOptionAddTo_Click(object sender, RoutedEventArgs e)
    {
        var t = ViewModel?.MoreOptionsContextTrack ?? ViewModel?.CurrentTrack;
        if (ViewModel != null && t != null)
            ViewModel.BottomSheetTrack = t;
        CloseMoreOptionsPopup();
        ViewModel?.OpenPlaylistSelectCommand.Execute(null);
    }

    private void MoreOptionEnqueue_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        ViewModel?.ShowQueuePanelCommand.Execute(null);
    }

    private void MoreOptionRingtone_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        MessageBox.Show("Ringtone cutter coming soon.", "Ringtone");
    }

    private void MoreOptionTrim_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        MessageBox.Show("Audio trim coming soon.", "Trim");
    }

    private void MoreOptionArtwork_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
    }

    private void MoreOptionDelete_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        var t = ViewModel?.MoreOptionsContextTrack ?? ViewModel?.CurrentTrack;
        if (t == null) return;
        var result = MessageBox.Show($"Delete \"{t.Title}\"?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            MessageBox.Show("Delete functionality coming soon.", "Delete");
        }
    }

    private void MoreOptionArtist_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        var t = ViewModel?.MoreOptionsContextTrack ?? ViewModel?.CurrentTrack;
        if (t == null) return;
        var artist = t.Artist;
        LogHelper.LogInfo($"MoreOptionArtist: artist='{artist}' ctxTrackSet={ViewModel?.MoreOptionsContextTrack?.Title ?? "(null)"} showFp={ViewModel?.ShowFullPlayer}");
        _pendingNavAction = () => ViewModel?.NavigateToArtist(artist);
        ViewModel!.ShowFullPlayer = false;
    }

    private void MoreOptionAlbum_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        var t = ViewModel?.MoreOptionsContextTrack ?? ViewModel?.CurrentTrack;
        if (t == null) return;
        var album = t.Album;
        var artist = t.Artist;
        _pendingNavAction = () => ViewModel?.NavigateToAlbum(album, artist);
        ViewModel!.ShowFullPlayer = false;
    }

    private Action? _pendingNavAction;
    private bool _closeFullPlayerFadeOnly;

    private void BottomSheetArtist_Click(object sender, RoutedEventArgs e)
    {
        var t = ViewModel?.BottomSheetTrack;
        if (t == null) return;
        var artist = t.Artist;
        ViewModel!.ShowTrackBottomSheet = false;
        Dispatcher.BeginInvoke(() => ViewModel?.NavigateToArtist(artist));
    }

    private void BottomSheetAlbum_Click(object sender, RoutedEventArgs e)
    {
        var t = ViewModel?.BottomSheetTrack;
        if (t == null) return;
        var album = t.Album;
        var artist = t.Artist;
        ViewModel!.ShowTrackBottomSheet = false;
        Dispatcher.BeginInvoke(() => ViewModel?.NavigateToAlbum(album, artist));
    }

    private void BottomSheetPlayNext_Click(object sender, RoutedEventArgs e)
    {
        var t = ViewModel?.BottomSheetTrack;
        ViewModel!.ShowTrackBottomSheet = false;
        if (t == null) return;
        MessageBox.Show("Play next: coming soon.", "Play Next");
    }

    private void BottomSheetAddTo_Click(object sender, RoutedEventArgs e)
    {
        ViewModel!.ShowTrackBottomSheet = false;
        ViewModel?.OpenPlaylistSelectCommand.Execute(null);
    }

    private void BottomSheetEnqueue_Click(object sender, RoutedEventArgs e)
    {
        var t = ViewModel?.BottomSheetTrack;
        ViewModel!.ShowTrackBottomSheet = false;
        if (t == null) return;
        MessageBox.Show("Enqueue: coming soon.", "Enqueue");
    }

    private void BottomSheetRingtone_Click(object sender, RoutedEventArgs e)
    {
        var t = ViewModel?.BottomSheetTrack;
        ViewModel!.ShowTrackBottomSheet = false;
        if (t == null) return;
        MessageBox.Show("Ringtone cutter coming soon.", "Ringtone");
    }

    private void BottomSheetTrim_Click(object sender, RoutedEventArgs e)
    {
        var t = ViewModel?.BottomSheetTrack;
        ViewModel!.ShowTrackBottomSheet = false;
        if (t == null) return;
        MessageBox.Show("Audio trim coming soon.", "Trim");
    }

    private void BottomSheetArtwork_Click(object sender, RoutedEventArgs e)
    {
        ViewModel!.ShowTrackBottomSheet = false;
    }

    private void BottomSheetShare_Click(object sender, RoutedEventArgs e)
    {
        var t = ViewModel?.BottomSheetTrack;
        ViewModel!.ShowTrackBottomSheet = false;
        if (t == null) return;
        var data = new DataObject(DataFormats.UnicodeText, $"{t.Title} - {t.Artist}");
        System.Windows.Clipboard.SetDataObject(data);
        MessageBox.Show("Track info copied to clipboard.", "Share");
    }

    private void BottomSheetDelete_Click(object sender, RoutedEventArgs e)
    {
        var t = ViewModel?.BottomSheetTrack;
        ViewModel!.ShowTrackBottomSheet = false;
        if (t == null) return;
        var result = MessageBox.Show($"Delete \"{t.Title}\"?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            MessageBox.Show("Delete functionality coming soon.", "Delete");
        }
    }

    private void BottomSheetHide_Click(object sender, RoutedEventArgs e)
    {
        ViewModel!.ShowTrackBottomSheet = false;
    }

    private void MoreOptionTimer_Click(object sender, RoutedEventArgs e)
    {
        CloseMoreOptionsPopup();
        ViewModel?.CloseFullPlayerCommand.Execute(null);
        ViewModel?.OpenSleepTimerCommand.Execute(null);
    }

    private void SleepTimerManual_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void SleepTimerManual_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            ViewModel?.SetSleepTimerCommand.Execute("manual");
            System.Windows.Input.Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void SleepTimerGrid_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement grid || !(bool)e.NewValue) return;

        // إعادة تعيين الحالة الأولية
        grid.Opacity = 0;
        var tf = grid.RenderTransform as System.Windows.Media.TranslateTransform;
        if (tf == null)
        {
            tf = new System.Windows.Media.TranslateTransform(0, 14);
            grid.RenderTransform = tf;
        }
        tf.Y = 14;

        // إنشاء Storyboard بـ 60fps وتشغيله
        var sb = new System.Windows.Media.Animation.Storyboard();
        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(sb, 60);

        var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, new System.Windows.Duration(TimeSpan.FromMilliseconds(220)))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        System.Windows.Media.Animation.Storyboard.SetTarget(fadeAnim, grid);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeAnim, new System.Windows.PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(fadeAnim);

        var slideAnim = new System.Windows.Media.Animation.DoubleAnimation(14, 0, new System.Windows.Duration(TimeSpan.FromMilliseconds(260)))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        System.Windows.Media.Animation.Storyboard.SetTarget(slideAnim, grid);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideAnim,
            new System.Windows.PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
        sb.Children.Add(slideAnim);

        sb.Begin();
    }

    private void MoreOptionHide_Click(object sender, RoutedEventArgs e)
    {
        var track = ViewModel?.BottomSheetTrack ?? ViewModel?.CurrentTrack;
        CloseMoreOptionsPopup();
        ViewModel?.CloseFullPlayerCommand.Execute(null);
        if (track != null)
            ViewModel?.HideTrackCommand.Execute(track);
    }

    // ─── Currently Playing swipe page gesture ─────────────────────────
    private Point _swipeStart;
    private bool _swiping;
    private bool _swallowClick;

    private void PlayingPageArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _swiping = false;
        _swallowClick = false;
        _swipeStart = e.GetPosition(PlayingPageArea);
        ((UIElement)sender).CaptureMouse();
    }

    private void PlayingPageArea_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!((UIElement)sender).IsMouseCaptured) return;

        Point current = e.GetPosition(PlayingPageArea);
        double dx = current.X - _swipeStart.X;

        if (!_swiping && Math.Abs(dx) > 28)
        {
            _swiping = true;
            _swallowClick = true;
            ViewModel?.ApplySwipe((int)Math.Sign(dx));
        }
    }

    private void PlayingPageArea_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (((UIElement)sender).IsMouseCaptured)
            ((UIElement)sender).ReleaseMouseCapture();
        if (_swallowClick)
        {
            e.Handled = true;
            _swallowClick = false;
        }
        _swiping = false;
    }

    private DateTime _lastWheelSwipe = DateTime.MinValue;

    private void PlayingPageArea_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel?.CurrentlyPlayingPage == CurrentlyPlayingPage.Lyrics && ViewModel?.Lyrics?.Count > 0)
            return;

        if ((DateTime.Now - _lastWheelSwipe).TotalMilliseconds < 220) return;
        if (Math.Abs(e.Delta) < 25) return;

        _lastWheelSwipe = DateTime.Now;
        ViewModel?.ApplySwipe(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void DotVisualizer_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null) ViewModel.CurrentlyPlayingPage = CurrentlyPlayingPage.Visualizer;
    }

    private void DotAlbumArt_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null) ViewModel.CurrentlyPlayingPage = CurrentlyPlayingPage.AlbumArt;
    }

    private void DotLyrics_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null) ViewModel.CurrentlyPlayingPage = CurrentlyPlayingPage.Lyrics;
    }

    // ─── Lyrics auto-scroll + slide animation ─────────────────────────
    private CurrentlyPlayingPage _previousPlayingPage = CurrentlyPlayingPage.AlbumArt;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentLyricIndex))
            ScrollLyricsToCurrent();
        else if (e.PropertyName == nameof(MainViewModel.CurrentlyPlayingPage))
        {
            AnimateSlideToPage();
            if (ViewModel?.CurrentlyPlayingPage == CurrentlyPlayingPage.Lyrics)
            {
                ViewModel?.RefreshLyricHighlight();
                QueueScrollLyricsToCurrent();
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsSearching))
            AnimateSearchBar();
        else if (e.PropertyName == nameof(MainViewModel.CurrentTrack))
            Dispatcher.BeginInvoke(() => { StartMiniTitleMarquee(); StartFpTitleMarquee(); });
        else if (e.PropertyName == nameof(MainViewModel.IsPlaying))
            QueueRefreshRequested();
        else if (e.PropertyName == nameof(MainViewModel.ShowQueue))
        {
            bool isOpen = ViewModel?.ShowQueue ?? false;
            if (!isOpen)
            {
                CancelQueueDrag("ShowQueueFalse");
            }
            Dispatcher.BeginInvoke(() => AnimateQueueDrawerToggle(isOpen));
        }
        else if (e.PropertyName == nameof(MainViewModel.ShowFullPlayer))
        {
            Dispatcher.BeginInvoke(() => StartFpTitleMarquee());
            if (ViewModel?.ShowFullPlayer == true)
            {
                AnimateFullPlayerIn();
                if (ViewModel?.CurrentlyPlayingPage == CurrentlyPlayingPage.Lyrics)
                {
                    ViewModel?.RefreshLyricHighlight();
                    var lyricsReopenTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                    lyricsReopenTimer.Tick += (_, _) =>
                    {
                        lyricsReopenTimer.Stop();
                        UpdateLyricsPadding();
                        QueueScrollLyricsToCurrent();
                    };
                    lyricsReopenTimer.Start();
                }
            }
            else
                AnimateFullPlayerOut();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsInitializing))
        {
            if (ViewModel?.IsInitializing == false && LoadingOverlay != null)
            {
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                fade.Completed += (_, _) =>
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    // Stop the perpetual pulsing-dot storyboards. Without this the
                    // RepeatBehavior=Forever clocks keep WPF's render thread awake at
                    // vsync forever (~10% idle CPU at 1024x650 that scales with window
                    // area) even when nothing on screen is animating.
                    LoadingDot1.BeginAnimation(OpacityProperty, null);
                    LoadingDot2.BeginAnimation(OpacityProperty, null);
                    LoadingDot3.BeginAnimation(OpacityProperty, null);
                };
                LoadingOverlay.BeginAnimation(OpacityProperty, fade);
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsDrawerOpen))
            AnimateDrawerToggle(ViewModel?.IsDrawerOpen ?? false);
        else if (e.PropertyName == nameof(MainViewModel.IsStatusBarLoading))
        {
            // Stop the perpetual status-bar pulsing dots when a background
            // operation ends, so their RepeatBehavior=Forever clocks don't keep
            // the render thread awake after the loading indicator is hidden.
            if (ViewModel?.IsStatusBarLoading == false)
            {
                StatusDot1.BeginAnimation(OpacityProperty, null);
                StatusDot2.BeginAnimation(OpacityProperty, null);
                StatusDot3.BeginAnimation(OpacityProperty, null);
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentView))
        {
            // Clear hero flag when leaving artist detail so re-entry via
            // non-hero paths (e.g. Full Player -> Artist) won't trigger
            // the reverse hero animation on back.
            var cv = ViewModel?.CurrentView;
            if (cv != "artist_detail") _artistDetailEnteredViaHero = false;

            EnsureDetailPageOpacity();
            var enteredView = ViewModel?.CurrentView;
            if (enteredView == "songs" && !_heroTransitionPending)
            {
                // Two-phase songs entry — mirrors SwitchTab: the songs list pays
                // its one-time container-generation + full-subtree layout BEFORE
                // the zoom starts, so the zoom runs on clean frames. Starting the
                // zoom first makes the first layout, first raster and pending
                // bindings all land inside the 300ms animation (frozen start +
                // hitches) — the documented stutter cause in SwitchTab.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
                {
                    if (ViewModel?.CurrentView != enteredView) return;
                    try { TabContent_Songs.UpdateLayout(); } catch { }
                    AnimatePageZoom();
                }));
            }
            else
            {
                AnimatePageZoom();
            }
            if (!_heroTransitionPending)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    DumpNavState($"arrived-{ViewModel?.CurrentView}");
                    var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                    t.Tick += (_, _) => { t.Stop(); DumpNavState("settled-p800"); };
                    t.Start();
                }));
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.ShowOverflowMenu))
            AnimateOverlay(OverflowMenuOverlay, OverflowMenuPanel, ViewModel?.ShowOverflowMenu ?? false);
        else if (e.PropertyName == nameof(MainViewModel.ShowSortMenu))
            AnimateOverlay(SortMenuOverlay, SortMenuPanel, ViewModel?.ShowSortMenu ?? false);
        else if (e.PropertyName == nameof(MainViewModel.ShowTrackBottomSheet))
            AnimateOverlay(TrackBottomSheetOverlay, TrackBottomSheetPanel, ViewModel?.ShowTrackBottomSheet ?? false);
        else if (e.PropertyName == nameof(MainViewModel.ShowTrackInfo))
            AnimateOverlay(TrackInfoOverlay, TrackInfoPanel, ViewModel?.ShowTrackInfo ?? false);
        else if (e.PropertyName == nameof(MainViewModel.ShowNewPlaylistOverlay))
            AnimateOverlay(NewPlaylistOverlay, NewPlaylistPanel, ViewModel?.ShowNewPlaylistOverlay ?? false);
        else if (e.PropertyName == nameof(MainViewModel.ShowRenamePlaylistOverlay))
            AnimateOverlay(RenamePlaylistOverlay, RenamePlaylistPanel, ViewModel?.ShowRenamePlaylistOverlay ?? false);
    }

    private System.Windows.Media.BitmapCache? _fullPlayerCache;
    private bool _fullPlayerTransitionCached;

    // Freezes the Full Player into a single off-screen texture so a transition can
    // slide/fade that texture instead of re-rasterizing the whole subtree each frame.
    // Anything that invalidates inside the cached subtree (visualizer OnRender frames,
    // the FP title marquee transform) would re-raster the cache every frame, so those
    // are suspended for the duration of the transition.
    private void BeginFullPlayerCachedTransition()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST_NOCACHE") == "1")
            {
                _fullPlayerTransitionCached = false;
                StopFpTitleMarquee();
                return;
            }
            _fullPlayerTransitionCached = true;
            _fullPlayerCache ??= new System.Windows.Media.BitmapCache();
            if (FullPlayerGrid != null && FullPlayerGrid.CacheMode == null)
                FullPlayerGrid.CacheMode = _fullPlayerCache;
            StopFpTitleMarquee();
            var viz = VisualizerPageGrid == null ? null : FindVisualizerControl(VisualizerPageGrid);
            viz?.PauseRendering();
        }
        catch { }
    }

    private void EndFullPlayerCachedTransition()
    {
        try
        {
            _fullPlayerTransitionCached = false;
            if (FullPlayerGrid != null)
                FullPlayerGrid.CacheMode = null;
            var viz = VisualizerPageGrid == null ? null : FindVisualizerControl(VisualizerPageGrid);
            viz?.ResumeRendering();
            if (ViewModel?.ShowFullPlayer == true) StartFpTitleMarquee();
        }
        catch { }
    }

    private static readonly System.Windows.Media.BitmapCache _fpBitmapCache = new();
    private void AnimateFullPlayerIn()
    {
        if (FullPlayerGrid == null) return;
        var height = Math.Max(FullPlayerGrid.ActualHeight, ActualHeight);
        if (height <= 0) height = 600;
        if (FullPlayerGrid.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform(0, height);
            FullPlayerGrid.RenderTransform = tt;
        }
        FullPlayerGrid.Opacity = 0;
        tt.Y = height;
        FullPlayerGrid.Visibility = Visibility.Visible;
        FullPlayerGrid.IsHitTestVisible = false;
        FullPlayerGrid.CacheMode = _fpBitmapCache;
        var dur = TimeSpan.FromMilliseconds(380);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(height, 0, dur) { EasingFunction = ease });
        var fade = new DoubleAnimation(0, 1, dur) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            FullPlayerGrid.CacheMode = null;
            FullPlayerGrid.IsHitTestVisible = true;
        };
        FullPlayerGrid.BeginAnimation(OpacityProperty, fade);
    }

    private void AnimateFullPlayerOut()
    {
        if (FullPlayerGrid == null)
        {
            RunPendingNavigation();
            return;
        }
        FullPlayerGrid.IsHitTestVisible = false;
        // Artist/Album close uses a plain fade instead of sliding down.
        bool slide = !_closeFullPlayerFadeOnly;
        _closeFullPlayerFadeOnly = false;
        // Flatten any stale animation clocks first so the last one can never
        // keep the player visible.
        FullPlayerGrid.BeginAnimation(OpacityProperty, null);
        if (FullPlayerGrid.RenderTransform is TranslateTransform tt0)
            tt0.BeginAnimation(TranslateTransform.YProperty, null);

        if (FullPlayerGrid.Visibility != Visibility.Visible)
        {
            FullPlayerGrid.Visibility = Visibility.Collapsed;
            RunPendingNavigation();
            return;
        }
        var dur = TimeSpan.FromMilliseconds(slide ? 280 : 220);
        var ease = new CubicEase { EasingMode = slide ? EasingMode.EaseIn : EasingMode.EaseInOut };

        if (slide)
        {
            var height = Math.Max(FullPlayerGrid.ActualHeight, ActualHeight);
            if (height <= 0) height = 600;
            if (FullPlayerGrid.RenderTransform is not TranslateTransform tt)
            {
                tt = new TranslateTransform(0, 0);
                FullPlayerGrid.RenderTransform = tt;
            }
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, height, dur) { EasingFunction = ease });
        }

        FullPlayerGrid.CacheMode = _fpBitmapCache;
        var fade = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            FullPlayerGrid.CacheMode = null;
            FullPlayerGrid.BeginAnimation(OpacityProperty, null);
            FullPlayerGrid.Visibility = Visibility.Collapsed;
            FullPlayerGrid.Opacity = 1;
            RunPendingNavigation();
        };
        FullPlayerGrid.BeginAnimation(OpacityProperty, fade);
    }

    private void RunFullPlayerTransitionProbe()
    {
        // Self-measured frame-pacing probe for Mini ⇄ Full Player. Records the real
        // per-frame interval (with absolute timestamps), slices the capture into
        // BEFORE/DURING/AFTER windows of the actual transition and reports avg/p95/
        // worst frame + counts of missed (16.67ms) and janky (>33.33ms) frames, plus
        // GC activity for the whole transition. Test A/B/C: idle (no extra env),
        // CPU-contended (MUSICPLAYER_PERFTEST_CPU=1 hogs a core with a busy loop).
        try
        {
            const int MAXFRAMES = 2400;
            var times = new long[MAXFRAMES];
            int count = 0;
            long lastTick = 0;
            int phase = 0;
            long t0 = 0;

            bool cpuHog = Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST_CPU") == "1";
            var cpuCancel = new System.Threading.CancellationTokenSource();
            System.Threading.Tasks.Task? hog = null;
            if (cpuHog)
                hog = System.Threading.Tasks.Task.Run(() =>
                {
                    System.Threading.SpinWait sw;
                    while (!cpuCancel.IsCancellationRequested) { sw = new System.Threading.SpinWait(); sw.SpinOnce(); }
                });

            EventHandler? sampler = null;
            sampler = (_, _) =>
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (lastTick > 0 && count < MAXFRAMES) times[count++] = now;
                lastTick = now;
            };

            void SliceAndLog(string tag, long startTicks, long endTicks, long phaseT0) // ticks, 64-bit
            {
                // Frames whose END timestamp lies within [start,end) are attributed to
                // that window; dtNs converted to ms per frame.
                var dts = new System.Collections.Generic.List<double>(512);
                long freq = System.Diagnostics.Stopwatch.Frequency;
                for (int i = 0; i < count - 1; i++)
                {
                    if (times[i + 1] >= startTicks && times[i] < endTicks)
                    {
                        double ms = (times[i + 1] - times[i]) / (double)freq * 1000.0;
                        if (ms > 0 && ms < 5000) dts.Add(ms);
                    }
                }
                if (dts.Count < 1) { LogHelper.LogInfo($"PERF-FP {tag}: no frames"); return; }
                dts.Sort();
                int n = dts.Count;
                double sum = 0, worst = 0;
                int gt16 = 0, gt33 = 0;
                foreach (var d in dts)
                {
                    sum += d;
                    if (d > worst) worst = d;
                    if (d > 16.67) gt16++;
                    if (d > 33.33) gt33++;
                }
                double avg = sum / n;
                double p95 = dts[Math.Min(n - 1, (int)Math.Ceiling(0.95 * n) - 1)];
                double prepMs = (phaseT0 == 0) ? 0.0 : (times[0] - phaseT0) / (double)freq * 1000.0;
                LogHelper.LogInfo($"PERF-FP {tag}: frames={n} prepMs={prepMs:0.0} avgMs={avg:0.00} p95Ms={p95:0.00} worstMs={worst:0.00} gt16_67ms={gt16} gt33_33ms={gt33} avgFps={1000.0 / avg:0.0}");
            }

            void LogTransition(bool entry)
            {
                long freq = System.Diagnostics.Stopwatch.Frequency;
                double msTick = 1000.0 / freq;
                long wPreS = t0 + (long)(60.0 / msTick + 0.5);            // 0-60ms: prep
                long wAnimE = t0 + (long)((entry ? 460.0 : 360.0) / msTick + 0.5); // animation window
                long wEnd = t0 + (long)(1000.0 / msTick + 0.5);
                SliceAndLog($"{t0} {(entry ? "ENTRY" : "EXIT")} prep(before-anim)", t0, wPreS, t0);
                SliceAndLog($"{t0} {(entry ? "ENTRY" : "EXIT")} animation(during)", wPreS, wAnimE, 0);
                SliceAndLog($"{t0} {(entry ? "ENTRY" : "EXIT")} settle(after-anim)", wAnimE, wEnd, 0);
                SliceAndLog($"{t0} {(entry ? "ENTRY" : "EXIT")} overall(user-perceived)", t0, wEnd, t0);
                // GC snapshot taken right after the transition window
                long mem0 = 0; int g0 = 0, g1 = 0, g2 = 0;
                try
                {
                    mem0 = System.GC.GetTotalMemory(false);
                    g0 = System.GC.CollectionCount(0); g1 = System.GC.CollectionCount(1); g2 = System.GC.CollectionCount(2);
                }
                catch { }
                LogHelper.LogInfo($"PERF-FP {(entry ? "ENTRY" : "EXIT")} gc: memAfter={mem0 / 1024.0 / 1024.0:0.0}MB gen0={g0} gen1={g1} gen2={g2} cpuHog={cpuHog}");
            }

            System.Windows.Media.CompositionTarget.Rendering += sampler;

            // Same 1s windows around each toggle as before, now sliced inside/after.
            var sched = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(100) };
            sched.Tick += (_, _) =>
            {
                switch (phase)
                {
                    case 0:
                        phase = 1;
                        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        LogHelper.LogInfo("PERF-FP opening");
                        if (ViewModel != null) ViewModel.ShowFullPlayer = true;
                        break;
                    case 1:
                        if ((System.Diagnostics.Stopwatch.GetTimestamp() - t0) / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0 >= 1000.0)
                        {
                            LogTransition(true);
                            phase = 2;
                            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                            LogHelper.LogInfo("PERF-FP closing");
                            if (ViewModel != null) ViewModel.ShowFullPlayer = false;
                        }
                        break;
                    case 2:
                        if ((System.Diagnostics.Stopwatch.GetTimestamp() - t0) / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0 >= 1000.0)
                        {
                            LogTransition(false);
                            phase = 3;
                            sched.Stop();
                            System.Windows.Media.CompositionTarget.Rendering -= sampler;
                            if (hog != null) { cpuCancel.Cancel(); try { hog.Wait(); } catch { } }
                            LogHelper.LogInfo("PERF-FP probe finished");
                        }
                        break;
                }
            };
            sched.Start();
        }
        catch (System.Exception ex)
        {
            LogHelper.LogInfo($"PERF-FP probe failed: {ex.Message}");
        }
    }

    private void RunPendingNavigation()
    {
        var nav = _pendingNavAction;
        _pendingNavAction = null;
        nav?.Invoke();
        LogHelper.LogInfo($"RunPendingNavigation: executed={(nav != null)}");
    }

    private void AnimateOverlay(FrameworkElement overlay, FrameworkElement panel, bool show)
    {
        if (overlay == null || panel == null) return;
        var dur = TimeSpan.FromMilliseconds(220);
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        if (panel.RenderTransform is TranslateTransform tr)
        {
            if (show)
            {
                tr.BeginAnimation(TranslateTransform.YProperty, null);
                tr.Y = 300;
                overlay.BeginAnimation(OpacityProperty, null);
                overlay.Opacity = 1;
                overlay.Visibility = Visibility.Visible;
                tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(300, 0, dur) { EasingFunction = easeOut });
                overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = easeOut });
            }
            else
            {
                if (overlay.Visibility != Visibility.Visible) return;
                tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 300, dur) { EasingFunction = easeIn });
                var fade = new DoubleAnimation(1, 0, dur) { EasingFunction = easeIn };
                fade.Completed += (_, _) =>
                {
                    overlay.BeginAnimation(OpacityProperty, null);
                    overlay.Opacity = 1;
                    tr.BeginAnimation(TranslateTransform.YProperty, null);
                    tr.Y = 300;
                    overlay.Visibility = Visibility.Collapsed;
                };
                overlay.BeginAnimation(OpacityProperty, fade);
            }
        }
        else
        {
            if (show)
            {
                if (panel.RenderTransform is ScaleTransform sc)
                {
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    sc.ScaleX = 0.92;
                    sc.ScaleY = 0.92;
                }
                overlay.BeginAnimation(OpacityProperty, null);
                overlay.Opacity = 1;
                overlay.Visibility = Visibility.Visible;
                if (panel.RenderTransform is ScaleTransform s)
                {
                    s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, dur) { EasingFunction = easeOut });
                    s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, dur) { EasingFunction = easeOut });
                }
                overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = easeOut });
            }
            else
            {
                if (overlay.Visibility != Visibility.Visible) return;
                if (panel.RenderTransform is ScaleTransform sc2)
                {
                    sc2.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.92, dur) { EasingFunction = easeIn });
                    sc2.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.92, dur) { EasingFunction = easeIn });
                }
                var fade2 = new DoubleAnimation(1, 0, dur) { EasingFunction = easeIn };
                fade2.Completed += (_, _) =>
                {
                    overlay.BeginAnimation(OpacityProperty, null);
                    overlay.Opacity = 1;
                    if (panel.RenderTransform is ScaleTransform sc3)
                    {
                        sc3.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        sc3.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        sc3.ScaleX = 1;
                        sc3.ScaleY = 1;
                    }
                    overlay.Visibility = Visibility.Collapsed;
                };
                overlay.BeginAnimation(OpacityProperty, fade2);
            }
        }
    }

    private ScaleTransform EnsureZoomTransform()
    {
        if (ContentGrid.RenderTransform is ScaleTransform st) return st;
        st = new ScaleTransform(1, 1);
        ContentGrid.RenderTransform = st;
        ContentGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        return st;
    }

    private void DumpNavState(string tag)
    {
        try
        {
            string yy = "n/a";
            var tt0 = FullPlayerGrid?.RenderTransform as TranslateTransform;
            if (tt0 != null) yy = tt0.Y.ToString("0.0");
            LogHelper.LogInfo($"[{tag}] view='{ViewModel?.CurrentView}' " +
                $"AD isVisible={ArtistDetailView.IsVisible} vis={ArtistDetailView.Visibility} op={ArtistDetailView.Opacity:0.00} size={ArtistDetailView.ActualWidth:0}x{ArtistDetailView.ActualHeight:0} " +
                $"backRow vis={ArtistDetailBackRow?.Visibility} op={ArtistDetailBackRow?.Opacity:0.00} isVis={ArtistDetailBackRow?.IsVisible} " +
                $"info op={ArtistDetailInfo?.Opacity:0.00} isVis={ArtistDetailInfo?.IsVisible} " +
                $"circle op={ArtistDetailCircle?.Opacity:0.00} isVis={ArtistDetailCircle?.IsVisible} " +
                $"CG isVisible={ContentGrid.IsVisible} vis={ContentGrid.Visibility} op={ContentGrid.Opacity:0.00} size={ContentGrid.ActualWidth:0}x{ContentGrid.ActualHeight:0} " +
                $"FP isVisible={FullPlayerGrid?.IsVisible} vis={FullPlayerGrid?.Visibility} op={FullPlayerGrid?.Opacity:0.00} y={yy} " +
                $"MOPanel vis={MoreOptionsPanel?.Visibility} op={MoreOptionsPanel?.Opacity:0.00}");
        }
        catch (Exception ex)
        {
            LogHelper.LogInfo($"[{tag}] dump error: {ex.Message}");
        }
    }

    private void EnsureDetailPageOpacity()
    {
        if (_heroTransitionPending || ContentGrid == null) return;

        // Reset inner children that PrepareDetailMixReveal hides during hero forward
        // but never restores on non-hero navigation paths.
        void ResetChild(FrameworkElement? fe)
        {
            if (fe == null) return;
            fe.BeginAnimation(OpacityProperty, null);
            fe.Opacity = 1;
            if (fe.RenderTransform is TranslateTransform tr)
                tr.BeginAnimation(TranslateTransform.YProperty, null);
        }
        ResetChild(ArtistDetailBackRow);
        ResetChild(ArtistDetailInfo);
        ResetChild(ArtistDetailCircle);

        foreach (var child in ContentGrid.Children)
        {
            if (child is not FrameworkElement fe) continue;
            if (fe != ArtistDetailView && fe != AlbumDetailView) continue;
            fe.BeginAnimation(OpacityProperty, null);
            fe.Opacity = 1;
            var imgs = new List<Image>();
            CollectImages(fe, imgs);
            foreach (var img in imgs)
            {
                img.BeginAnimation(OpacityProperty, null);
                img.Opacity = 1;
            }
        }
        LogHelper.LogInfo($"EnsureDetailPageOpacity: reset detail page + inner children");
    }


    private void AnimatePageZoom()
    {
        if (ContentGrid == null) return;
        var st = EnsureZoomTransform();
        var dur = TimeSpan.FromMilliseconds(300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.03, 1, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.03, 1, dur) { EasingFunction = ease });
        ContentGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.6, 1, dur) { EasingFunction = ease });
    }

    private void AnimateCloseZoom()
    {
        FrameworkElement? target = ContentGrid != null ? ContentGrid : RootBorder;
        if (target == null)
        {
            Close();
            return;
        }
        if (target.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            target.RenderTransform = st;
            target.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        var dur = TimeSpan.FromMilliseconds(300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.95, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.95, dur) { EasingFunction = ease });
        var op = new DoubleAnimation(1, 0.3, dur) { EasingFunction = ease };
        op.Completed += (_, _) => Close();
        target.BeginAnimation(OpacityProperty, op);
    }

    private void AnimateDrawerToggle(bool open)
    {
        if (DrawerOverlay == null || DrawerPanel == null || DrawerBackdrop == null) return;

        var duration = TimeSpan.FromMilliseconds(300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (open)
        {
            DrawerOverlay.Visibility = Visibility.Visible;
            DrawerPanel.RenderTransform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(-300, 0, duration) { EasingFunction = ease });
            DrawerBackdrop.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        }
        else
        {
            var slide = new DoubleAnimation(0, -300, duration) { EasingFunction = ease };
            bool handled = false;
            slide.Completed += (_, _) =>
            {
                if (handled) return;
                handled = true;
                DrawerOverlay.Visibility = Visibility.Collapsed;
            };
            DrawerPanel.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slide);
            DrawerBackdrop.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(1, 0, duration) { EasingFunction = ease });
        }
    }

    private void AnimateSlideToPage()
    {
        var vm = ViewModel;
        if (vm == null) return;

        var targetPage = vm.CurrentlyPlayingPage;
        var prevPage = _previousPlayingPage;
        if (targetPage == prevPage) return;
        _previousPlayingPage = targetPage;

        double w = PlayingPageArea?.ActualWidth ?? ActualWidth;
        if (w <= 0) w = 560;

        var ease = new System.Windows.Media.Animation.QuarticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(220);

        FrameworkElement? GetGrid(CurrentlyPlayingPage page) => page switch
        {
            CurrentlyPlayingPage.Visualizer => VisualizerPageGrid,
            CurrentlyPlayingPage.AlbumArt => AlbumArtPageGrid,
            CurrentlyPlayingPage.Lyrics => LyricsPageGrid,
            _ => AlbumArtPageGrid
        };

        TranslateTransform? GetTransform(CurrentlyPlayingPage page) => page switch
        {
            CurrentlyPlayingPage.Visualizer => VisualizerTransform,
            CurrentlyPlayingPage.AlbumArt => AlbumArtTransform,
            CurrentlyPlayingPage.Lyrics => LyricsTransform,
            _ => AlbumArtTransform
        };

        // Logical page layout: Visualizer = -1 (left), AlbumArt = 0 (center), Lyrics = +1 (right)
        int GetPageIndex(CurrentlyPlayingPage page) => page switch
        {
            CurrentlyPlayingPage.Visualizer => -1,
            CurrentlyPlayingPage.AlbumArt => 0,
            CurrentlyPlayingPage.Lyrics => 1,
            _ => 0
        };

        int prevIndex = GetPageIndex(prevPage);
        int targetIndex = GetPageIndex(targetPage);
        bool movingRight = targetIndex > prevIndex; // Visualizer -> AlbumArt or AlbumArt -> Lyrics

        var outgoingGrid = GetGrid(prevPage);
        var outgoingTrans = GetTransform(prevPage);
        var incomingGrid = GetGrid(targetPage);
        var incomingTrans = GetTransform(targetPage);

        var allGrids = new[] { VisualizerPageGrid, AlbumArtPageGrid, LyricsPageGrid };
        foreach (var g in allGrids)
        {
            if (g != null && g != outgoingGrid && g != incomingGrid)
            {
                ClearAnimations(g);
                g.Visibility = Visibility.Collapsed;
            }
        }

        if (outgoingGrid != null && outgoingTrans != null && incomingGrid != null && incomingTrans != null)
        {
            ClearAnimations(outgoingGrid);
            ClearAnimations(incomingGrid);

            outgoingGrid.Visibility = Visibility.Visible;
            incomingGrid.Visibility = Visibility.Visible;
            outgoingGrid.IsHitTestVisible = false;
            incomingGrid.IsHitTestVisible = true;

            var vizControl = FindVisualizerControl(incomingGrid);
            var vizControlOut = FindVisualizerControl(outgoingGrid);
            vizControl?.PauseRendering();
            vizControlOut?.PauseRendering();

            double incomingStart = movingRight ? w : -w;
            double outgoingEnd = movingRight ? -w : w;

            incomingTrans.X = incomingStart;
            outgoingTrans.X = 0;

            var inAnim = new System.Windows.Media.Animation.DoubleAnimation(incomingStart, 0, dur) { EasingFunction = ease };
            var outAnim = new System.Windows.Media.Animation.DoubleAnimation(0, outgoingEnd, dur) { EasingFunction = ease };

            outAnim.Completed += (s, ev) =>
            {
                if (ViewModel?.CurrentlyPlayingPage == targetPage)
                {
                    outgoingGrid.Visibility = Visibility.Collapsed;
                    outgoingTrans.X = 0;
                    outgoingGrid.IsHitTestVisible = true;
                    vizControlOut?.ResumeRendering();
                    vizControl?.ResumeRendering();
                }
            };

            outgoingTrans.BeginAnimation(TranslateTransform.XProperty, outAnim);
            incomingTrans.BeginAnimation(TranslateTransform.XProperty, inAnim);

            if (SmallAlbumArtBorder != null)
            {
                bool showSmallArt = targetPage != CurrentlyPlayingPage.AlbumArt;
                var fadeDur = TimeSpan.FromMilliseconds(300);
                var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation(
                    SmallAlbumArtBorder.Opacity, showSmallArt ? 1.0 : 0.0, fadeDur);
                SmallAlbumArtBorder.BeginAnimation(OpacityProperty, fadeAnim);
            }

            AnimateLyricsButtons(targetPage == CurrentlyPlayingPage.Lyrics);
        }
        else
        {
            if (VisualizerPageGrid != null) VisualizerPageGrid.Visibility = targetPage == CurrentlyPlayingPage.Visualizer ? Visibility.Visible : Visibility.Collapsed;
            if (AlbumArtPageGrid != null) AlbumArtPageGrid.Visibility = targetPage == CurrentlyPlayingPage.AlbumArt ? Visibility.Visible : Visibility.Collapsed;
            if (LyricsPageGrid != null) LyricsPageGrid.Visibility = targetPage == CurrentlyPlayingPage.Lyrics ? Visibility.Visible : Visibility.Collapsed;

            if (SmallAlbumArtBorder != null)
                SmallAlbumArtBorder.Opacity = targetPage != CurrentlyPlayingPage.AlbumArt ? 1.0 : 0.0;
        }
    }

    private System.Windows.Threading.DispatcherTimer? _resizeMarqueeTimer;
    private double _lyricsScrollFrom;
    private void AnimateLyricsButtons(bool show)
    {
        var fadeDur = TimeSpan.FromMilliseconds(250);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        double appear = show ? 1.0 : 0.0;
        double disappear = show ? 0.0 : 1.0;

        if (FpLyricsTextBtn != null)
        {
            FpLyricsTextBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(FpLyricsTextBtn.Opacity, appear, fadeDur) { EasingFunction = ease });
            FpLyricsTextBtn.IsHitTestVisible = show;
        }
        if (FpLrcSearchBtn != null)
        {
            FpLrcSearchBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(FpLrcSearchBtn.Opacity, appear, fadeDur) { EasingFunction = ease });
            FpLrcSearchBtn.IsHitTestVisible = show;
        }
        if (FpSoundEffectsBtn != null)
        {
            FpSoundEffectsBtn.BeginAnimation(OpacityProperty, new DoubleAnimation(FpSoundEffectsBtn.Opacity, disappear, fadeDur) { EasingFunction = ease });
        }
    }

    private void FpLyricsTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ViewModel!.ShowLyricsSpeedPanel = false;
        ViewModel!.ShowLyricsSettings = !ViewModel!.ShowLyricsSettings;
        AnimateLyricsSettingsPanel();
    }

    private void AnimateLyricsSettingsPanel()
    {
        if (LyricsSettingsBackdrop == null || LyricsSettingsPanel == null) return;
        var dur = TimeSpan.FromMilliseconds(280);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (ViewModel?.ShowLyricsSettings == true)
        {
            LyricsSettingsBackdrop.Visibility = Visibility.Visible;
            LyricsSettingsPanel.Visibility = Visibility.Visible;
            LyricsSettingsTransform.Y = 300;
            LyricsSettingsPanel.Opacity = 0;
            LyricsSettingsPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            LyricsSettingsTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(300, 0, dur) { EasingFunction = ease });
        }
        else
        {
            var animOut = new DoubleAnimation(0, 300, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var fadeOut = new DoubleAnimation(1, 0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fadeOut.Completed += (_, _) => { LyricsSettingsPanel.Visibility = Visibility.Collapsed; LyricsSettingsBackdrop.Visibility = Visibility.Collapsed; };
            LyricsSettingsPanel.BeginAnimation(OpacityProperty, fadeOut);
            LyricsSettingsTransform.BeginAnimation(TranslateTransform.YProperty, animOut);
        }
    }

    private void LyricsSettingsBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
    }

    private void LyricsSettingsPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void LyricsSpeedAdjust_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && ViewModel != null)
        {
            if (double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var delta))
                ViewModel.LyricsSpeedOffset = Math.Round(ViewModel.LyricsSpeedOffset + delta, 1);
        }
    }

    private void LyricsSpeedUndo_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.LyricsSpeedOffset = 0;
    }

    private void LyricsSpeedClose_Click(object sender, RoutedEventArgs e)
    {
        ViewModel!.ShowLyricsSpeedPanel = false;
        if (LyricsSpeedPanel == null) return;
        var dur = TimeSpan.FromMilliseconds(250);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fadeOut = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        fadeOut.Completed += (_, _) => LyricsSpeedPanel.Visibility = Visibility.Collapsed;
        LyricsSpeedPanel.BeginAnimation(OpacityProperty, fadeOut);
        LyricsSpeedTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.85, dur) { EasingFunction = ease });
        LyricsSpeedTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.85, dur) { EasingFunction = ease });
    }

    private void LyricsColor_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string color } && ViewModel != null)
            ViewModel.LyricsColor = color;
    }

    private void LyricsStyle_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string style } && ViewModel != null)
        {
            ViewModel.LyricsFontStyle = style;
            UpdateLyricsStyleButtons(style);
        }
    }

    private void UpdateLyricsStyleButtons(string selected)
    {
        FpStyleNormal!.Background = new SolidColorBrush(selected == "Normal" ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        FpStyleItalic!.Background = new SolidColorBrush(selected == "Italic" ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        FpStyleMedium!.Background = new SolidColorBrush(selected == "Medium" ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        FpStyleBold!.Background = new SolidColorBrush(selected == "Bold" ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    }

    private void LyricsDesktopToggle_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.DesktopLyricsToggle = !ViewModel.DesktopLyricsToggle;
        UpdateDesktopLyricsToggle();
    }

    private void UpdateDesktopLyricsToggle()
    {
        if (DesktopLyricsToggleDot == null || ViewModel == null) return;
        DesktopLyricsToggleDot.HorizontalAlignment = ViewModel.DesktopLyricsToggle ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    private void LyricsSpeedOpen_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel!.ShowLyricsSettings = false;
        AnimateLyricsSettingsPanel();
        ViewModel!.ShowLyricsSpeedPanel = true;
        if (LyricsSpeedPanel == null) return;
        LyricsSpeedPanel.Visibility = Visibility.Visible;
        LyricsSpeedPanel.Opacity = 0;
        LyricsSpeedTransform.ScaleX = 0.85;
        LyricsSpeedTransform.ScaleY = 0.85;
        var dur = TimeSpan.FromMilliseconds(250);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        LyricsSpeedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        LyricsSpeedTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1, dur) { EasingFunction = ease });
        LyricsSpeedTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1, dur) { EasingFunction = ease });
    }

    private void FpLrcSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowRelatedLyricsPopup();
    }

    private void ShowRelatedLyricsPopup()
    {
        if (RelatedLyricsPopup == null) return;
        RelatedLyricsPopup.Visibility = Visibility.Visible;
        RelatedLyricsPopup.Opacity = 0;
        RelatedLyricsTransform.ScaleX = 0.85;
        RelatedLyricsTransform.ScaleY = 0.85;
        var dur = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        RelatedLyricsPopup.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        RelatedLyricsTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1, dur) { EasingFunction = ease });
        RelatedLyricsTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1, dur) { EasingFunction = ease });
    }

    private void HideRelatedLyricsPopup()
    {
        if (RelatedLyricsPopup == null || RelatedLyricsPopup.Visibility != Visibility.Visible) return;
        var dur = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fadeOut = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        var scaleX = new DoubleAnimation(1, 0.85, dur) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(1, 0.85, dur) { EasingFunction = ease };
        fadeOut.Completed += (_, _) => RelatedLyricsPopup.Visibility = Visibility.Collapsed;
        RelatedLyricsPopup.BeginAnimation(OpacityProperty, fadeOut);
        RelatedLyricsTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        RelatedLyricsTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    private void RelatedLyricsBackdrop_Click(object sender, MouseButtonEventArgs e) => HideRelatedLyricsPopup();

    private void RelatedLyricsEdit_Click(object sender, RoutedEventArgs e)
    {
        HideRelatedLyricsPopup();
        ViewModel!._navigatedFromFullPlayer = true;
        ViewModel!.ShowFullPlayer = false;
        ViewModel?.OpenLrcEditCommand.Execute(null);
    }

    private void RelatedLyricsOnline_Click(object sender, RoutedEventArgs e)
    {
        var query = RelatedLyricsTrackName?.Text?.Trim();
        HideRelatedLyricsPopup();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var url = $"https://www.google.com/search?q={Uri.EscapeDataString(query + " lyrics")}";
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }

    private void RelatedLyricsLocal_Click(object sender, RoutedEventArgs e)
    {
        HideRelatedLyricsPopup();
        ViewModel!._navigatedFromFullPlayer = true;
        ViewModel!.ShowFullPlayer = false;
        ViewModel?.OpenLrcBrowserCommand.Execute(null);
    }

    private double _lyricsScrollTo;
    private DateTime _lyricsScrollStart;
    private static readonly TimeSpan LyricsScrollDuration = TimeSpan.FromMilliseconds(200);
    private bool _lyricsScrollProgrammatic;
    private System.Windows.Threading.DispatcherTimer? _lyricsUserScrollPause;

    private void LyricsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_lyricsScrollProgrammatic) return;

        // Single reused timer (created in the constructor) — no per-event allocation.
        _lyricsUserScrollPause?.Stop();
        _lyricsUserScrollPause?.Start();
    }

    // Driven from the rendering loop; returns false once the glide is complete.
    private bool AdvanceLyricsScroll()
    {
        if (LyricsScrollViewer == null) return false;

        var elapsed = DateTime.UtcNow - _lyricsScrollStart;
        var t = Math.Min(1.0, elapsed.TotalMilliseconds / LyricsScrollDuration.TotalMilliseconds);
        var eased = t < 0.5 ? 4.0 * t * t * t : 1.0 - Math.Pow(-2.0 * t + 2.0, 3) / 2.0;
        _lyricsScrollProgrammatic = true;
        LyricsScrollViewer.ScrollToVerticalOffset(_lyricsScrollFrom + (_lyricsScrollTo - _lyricsScrollFrom) * eased);
        _lyricsScrollProgrammatic = false;
        return t < 1.0;
    }

    private void UpdateLyricsPadding()
    {
        if (LyricsScrollViewer == null || LyricsStack == null) return;
        var h = LyricsScrollViewer.ActualHeight;
        if (h <= 0) return;
        var pad = h / 2.0;
        LyricsStack.Margin = new Thickness(0, pad, 0, pad);
    }

    private int _lyricsScrollRetryCount;
    private bool _scrollLyricsQueued;
    // Coalesces bursted lyrics-scroll requests (page change, reopen, lyric index
    // change) so they all collapse into one glide instead of rebuilding/restarting
    // the animation repeatedly.
    private void QueueScrollLyricsToCurrent()
    {
        if (_scrollLyricsQueued) return;
        _scrollLyricsQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _scrollLyricsQueued = false;
            ScrollLyricsToCurrent();
        }));
    }

    private void ScrollLyricsToCurrent()
    {
        var vm = ViewModel;
        if (vm == null || LyricsList == null || LyricsScrollViewer == null) return;
        if (vm.CurrentLyricIndex < 0 || vm.CurrentLyricIndex >= LyricsList.Items.Count) return;

        var idx = vm.CurrentLyricIndex;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (LyricsScrollViewer.ActualHeight <= 0)
            {
                if (_lyricsScrollRetryCount < 5)
                {
                    _lyricsScrollRetryCount++;
                    var retryTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    retryTimer.Tick += (_, _) => { retryTimer.Stop(); ScrollLyricsToCurrent(); };
                    retryTimer.Start();
                }
                return;
            }
            _lyricsScrollRetryCount = 0;

            UpdateLyricsPadding();

            var container = LyricsList.ItemContainerGenerator.ContainerFromIndex(idx) as FrameworkElement;
            if (container == null)
            {
                LyricsList.UpdateLayout();
                container = LyricsList.ItemContainerGenerator.ContainerFromIndex(idx) as FrameworkElement;
                if (container == null) return;
            }

            container.UpdateLayout();

            var containerTop = container.TranslatePoint(new Point(0, 0), LyricsScrollViewer);
            var containerHeight = container.ActualHeight;
            var visibleHeight = LyricsScrollViewer.ActualHeight;

            var targetOffset = LyricsScrollViewer.VerticalOffset + containerTop.Y + containerHeight / 2.0 - visibleHeight / 2.0;
            targetOffset = Math.Max(0, targetOffset);

            _lyricsScrollFrom = LyricsScrollViewer.VerticalOffset;
            _lyricsScrollTo = targetOffset;
            _lyricsScrollStart = DateTime.UtcNow;

            if (Math.Abs(_lyricsScrollFrom - _lyricsScrollTo) < 0.5) return;

            _lyricsScrollActive = true;
            EnsurePerfRenderActive();
        }));
    }

    private static void ClearAnimations(FrameworkElement? element)
    {
        if (element?.RenderTransform is TranslateTransform t)
            t.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
    }

    private void AnimateSearchBar()
    {
        var vm = ViewModel;
        if (vm == null) return;

        var dur = TimeSpan.FromMilliseconds(200);
        var ease = new System.Windows.Media.Animation.QuarticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

        var allBorders = new Border?[] { SearchBarBorder, RecentSearchBarBorder, RecentAddSearchBarBorder, MostPlayedSearchBarBorder, FavoritesSearchBarBorder, FolderDetailSearchBarBorder };
        var allBoxes = new FrameworkElement?[] { SearchBox, RecentSearchBox, RecentAddSearchBox, MostPlayedSearchBox, FavoritesSearchBox, FolderDetailSearchBox };

        if (vm.IsSearching)
        {
            int idx = vm.CurrentView switch
            {
                "recent" => 1,
                "recent_add" => 2,
                "most_played" => 3,
                "favorites" => 4,
                "folder_detail" => 5,
                _ => 0
            };
            for (int i = 0; i < allBorders.Length; i++)
            {
                var b = allBorders[i];
                if (b == null) continue;
                if (i == idx)
                {
                    b.BeginAnimation(OpacityProperty, null);
                    b.BeginAnimation(MaxHeightProperty, null);
                    b.Visibility = Visibility.Visible;
                    var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, dur) { EasingFunction = ease };
                    var grow = new System.Windows.Media.Animation.DoubleAnimation(0, 50, dur) { EasingFunction = ease };
                    b.BeginAnimation(OpacityProperty, fadeIn);
                    b.BeginAnimation(MaxHeightProperty, grow);
                    allBoxes[i]?.Focus();
                }
                else
                {
                    b.Visibility = Visibility.Collapsed;
                    b.Opacity = 0;
                    b.MaxHeight = 0;
                    b.BeginAnimation(OpacityProperty, null);
                    b.BeginAnimation(MaxHeightProperty, null);
                }
            }
        }
        else
        {
            foreach (var b in allBorders)
            {
                if (b == null || b.Visibility == Visibility.Collapsed) continue;
                b.BeginAnimation(OpacityProperty, null);
                b.BeginAnimation(MaxHeightProperty, null);
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, dur) { EasingFunction = ease };
                var shrink = new System.Windows.Media.Animation.DoubleAnimation(50, 0, dur) { EasingFunction = ease };
                var captured = b;
                fadeOut.Completed += (_, _) => { captured.Visibility = Visibility.Collapsed; };
                b.BeginAnimation(OpacityProperty, fadeOut);
                b.BeginAnimation(MaxHeightProperty, shrink);
            }
            foreach (var b in allBorders)
            {
                if (b == null) continue;
                b.BeginAnimation(OpacityProperty, null);
                b.BeginAnimation(MaxHeightProperty, null);
                b.Visibility = Visibility.Collapsed;
                b.Opacity = 0;
                b.MaxHeight = 0;
            }
        }
    }

    private static AudioVisualizerControl? FindVisualizerControl(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is AudioVisualizerControl viz) return viz;
            var found = FindVisualizerControl(child);
            if (found != null) return found;
        }
        return null;
    }

    private void OnViewModelTrackChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentTrack) && ViewModel?.CurrentTrack != null)
        {
            var track = ViewModel.CurrentTrack;
            _trayService?.UpdateTooltip($"{track.Title} - {track.Artist}");
            if (Visibility == Visibility.Visible) return;
            _trayService?.ShowBalloon("Now Playing", $"{track.Title} - {track.Artist}");
        }
    }

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".wav", ".flac", ".m4a", ".ogg", ".wma", ".opus", ".aiff", ".ape", ".ac3" };

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Any(f => SupportedExtensions.Contains(Path.GetExtension(f))))
                e.Effects = DragDropEffects.Copy;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null) return;

        var audioFiles = files.Where(f => SupportedExtensions.Contains(Path.GetExtension(f))).ToList();
        if (audioFiles.Count == 0) return;

        if (audioFiles.Count == 1)
            ViewModel?.PlayFileCommand.Execute(audioFiles[0]);
        else
            ViewModel?.PlayFilesCommand.Execute(audioFiles);

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void OpenFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        if (!SupportedExtensions.Contains(Path.GetExtension(filePath))) return;
        ViewModel?.PlayFileCommand.Execute(filePath);
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private double _marqueeOffset;
    private double _marqueeSingleTextWidth;
    private double _marqueePauseRemainingMs;
    private const double MarqueeSpeed = 40;

    private void StartMiniTitleMarquee()
    {
        StopMiniTitleMarquee();
        if (MiniTitleTransform != null) MiniTitleTransform.X = 0;

        if (MiniTitleText == null || MiniTitleCanvas == null || MiniTitleBorder == null) return;

        MiniTitleText.UpdateLayout();
        double textWidth = MiniTitleText.ActualWidth;
        double containerWidth = MiniTitleBorder.ActualWidth;

        if (textWidth <= 0 || containerWidth <= 0) return;

        MiniTitleCanvas.Height = MiniTitleText.ActualHeight;
        Canvas.SetLeft(MiniTitleTextCopy, textWidth + 60);

        if (textWidth <= containerWidth + 2)
        {
            MiniTitleTextCopy.Visibility = Visibility.Collapsed;
            return;
        }

        MiniTitleTextCopy.Visibility = Visibility.Visible;
        _marqueeSingleTextWidth = textWidth;
        _marqueeOffset = 0;
        _marqueePauseRemainingMs = MarqueePauseMs;
        _miniMarqueeActive = true;
        EnsurePerfRenderActive();
    }

    private void StopMiniTitleMarquee()
    {
        _miniMarqueeActive = false;
        UpdatePerfRenderAfterMarqueeStop();
        if (MiniTitleTransform != null) MiniTitleTransform.X = 0;
        if (MiniTitleTextCopy != null) MiniTitleTextCopy.Visibility = Visibility.Collapsed;
    }

    private double _fpMarqueeOffset;
    private double _fpMarqueeSingleTextWidth;
    private double _fpMarqueePauseRemainingMs;

    private void StartFpTitleMarquee()
    {
        if (_fullPlayerTransitionCached) return; // deferred by EndFullPlayerCachedTransition
        StopFpTitleMarquee();
        if (FpTitleTransform != null) FpTitleTransform.X = 0;

        if (FpTitleText == null || FpTitleCanvas == null || FpTitleBorder == null) return;

        FpTitleText.UpdateLayout();
        double textWidth = FpTitleText.ActualWidth;
        double containerWidth = FpTitleBorder.ActualWidth;

        if (textWidth <= 0 || containerWidth <= 0) return;

        FpTitleCanvas.Height = FpTitleText.ActualHeight;
        Canvas.SetLeft(FpTitleTextCopy, textWidth + 60);

        if (textWidth <= containerWidth + 2)
        {
            FpTitleTextCopy.Visibility = Visibility.Collapsed;
            return;
        }

        FpTitleTextCopy.Visibility = Visibility.Visible;
        _fpMarqueeSingleTextWidth = textWidth;
        _fpMarqueeOffset = 0;
        _fpMarqueePauseRemainingMs = MarqueePauseMs;
        _fpMarqueeActive = true;
        EnsurePerfRenderActive();
    }

    private void UpdatePerfRenderAfterMarqueeStop()
    {
        if (!_miniMarqueeActive && !_fpMarqueeActive && !_lyricsScrollActive && !_queueAutoScrollActive)
            StopPerfRender();
    }

    private void StopFpTitleMarquee()
    {
        _fpMarqueeActive = false;
        UpdatePerfRenderAfterMarqueeStop();
        if (FpTitleTransform != null) FpTitleTransform.X = 0;
        if (FpTitleTextCopy != null) FpTitleTextCopy.Visibility = Visibility.Collapsed;
    }

    private static void CountVisuals(System.Windows.DependencyObject node, ref int total, ref int scrollable)
    {
        if (node == null) return;
        total++;
        if (node is System.Windows.Controls.ScrollViewer sv && sv.ScrollableHeight > 0)
            scrollable++;
        if (node is System.Windows.Controls.Panel || node is System.Windows.Controls.ContentPresenter)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                CountVisuals(System.Windows.Media.VisualTreeHelper.GetChild(node, i), ref total, ref scrollable);
        }
    }

    private static T? FindDescendant<T>(System.Windows.DependencyObject? start) where T : System.Windows.DependencyObject
    {
        if (start == null) return null;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(start); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(start, i);
            if (child is T match) return match;
            var deep = FindDescendant<T>(child);
            if (deep != null) return deep;
        }
        return null;
    }
}
