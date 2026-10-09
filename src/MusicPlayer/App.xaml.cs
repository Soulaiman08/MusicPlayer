using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicPlayer.Database;
using MusicPlayer.Services;
using MusicPlayer.Audio;
using MusicPlayer.Library;
using MusicPlayer.Models;
using MusicPlayer.ViewModels;
using MusicPlayer.Views;
using MusicPlayer.Utilities;

namespace MusicPlayer;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }
    private MusicPlayerServiceHost? _serviceHost;
    private Mutex? _singleInstanceMutex;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public System.Windows.Rect rcMonitor;
        public System.Windows.Rect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public uint cb;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private static void ForceHighPerformanceGpu()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\DirectX\UserGpuPreferences");
            key?.SetValue(exePath, "GpuPreference=2",
                Microsoft.Win32.RegistryValueKind.String);
        }
        catch { }
    }

    public App()
    {
        ForceHighPerformanceGpu();
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
        // Raise scheduling priority so the UI thread gets CPU time ahead of background
        // services — reduces the OS-scheduler jitter that causes dropped frames on
        // integrated graphics hardware (Intel HD) where the render budget is tight.
        try
        {
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass =
                System.Diagnostics.ProcessPriorityClass.AboveNormal;
        }
        catch { }

        // Recover from sleep/resume: WPF's GPU rendering context becomes invalid
        // after the system sleeps. Toggle render mode on resume to force WPF
        // to reinitialize its renderer. Also handle app launch after sleep.
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                try
                {
                    RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                    {
                        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
                        if (MainWindow is { IsLoaded: true })
                        {
                            MainWindow.InvalidateArrange();
                            MainWindow.InvalidateVisual();
                            MainWindow.Background = new System.Windows.Media.SolidColorBrush(
                                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF15181F"));
                        }
                    });
                }
                catch { }
            }
        };

        // Log (never silently swallow) exceptions so the real root cause is visible.
        // A swallowed exception on the audio thread produces a silent freeze with no
        // diagnostic — always record type + stack, and do NOT auto-handle by default.
        DispatcherUnhandledException += (_, args) =>
        {
            LogHelper.LogError("DispatcherUnhandledException", args.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogHelper.LogError("UnobservedTaskException",
                args.Exception ?? new Exception("Unobserved task exception"));
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogHelper.LogError("AppDomainUnhandledException", ex);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "MusicPlayer_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Music Player is already running.", "Music Player", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppPaths.EnsureDirectories();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        SessionEnding += (_, _) => { Shutdown(); };

        var persisted = ReadPersistedWindowBounds();
        var mainWindow = new MainWindow();
        mainWindow.ApplyPersistedWindowState(persisted.W, persisted.H, persisted.X, persisted.Y, persisted.Maximized);
        Application.Current.MainWindow = mainWindow;
        mainWindow.Show();

        try
        {
            var tierValue = System.Windows.Media.RenderCapability.Tier >> 16;
            var refreshHz = 0.0;
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
                var monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));
                if (System.Windows.Interop.HwndSource.FromHwnd(hwnd).CompositionTarget != null && MonitorFromWindow(hwnd, 2) != IntPtr.Zero)
                {
                }
                var devmode = new DEVMODE();
                devmode.dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(null, -1, ref devmode))
                    refreshHz = devmode.dmDisplayFrequency;
            }
            catch { }

            // Inform the scroll engine of the real refresh rate so the
            // DispatcherTimer interval matches the monitor's vsync period.
            if (refreshHz > 0)
                MusicPlayer.Views.SmoothScrollHelper.SetRefreshRate(refreshHz);

            var adapterName = "unknown";
            try
            {
                var dd = new DISPLAY_DEVICE();
                dd.cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                for (uint i = 0; i < 4; i++)
                {
                    if (EnumDisplayDevices(null, i, ref dd, 0)
                        && (dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                    {
                        adapterName = $"{dd.DeviceString} | {dd.DeviceID}";
                        break;
                    }
                    dd.cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                }
            }
            catch { }

            var hwndRenderMode = "unknown";
            if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1")
            {
                // Idle + synthetic-motion FPS are measured only once the window has
                // presented its first frame (WPF renders on demand; starting the
                // probe at t=0 yields zero because nothing was dirty yet). We
                // schedule the measurement ~10s after startup, on the UI thread,
                // which also lets the music library finish initializing first.
                var idleFps5s = 0.0;
                var animFps3s = 0.0;
                var flInited = false;
                var measureTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = System.TimeSpan.FromSeconds(10)
                };
                measureTimer.Tick += (_, _) =>
                {
                    measureTimer.Stop();
                    try
                    {
                        var hwndSource = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle);
                        if (hwndSource?.CompositionTarget is System.Windows.Interop.HwndTarget targetComposition)
                            hwndRenderMode = targetComposition.RenderMode.ToString();
                    }
                    catch { }

                    // 1) Idle: nothing dirty → frames should be ~zero (on-demand rendering).
                    var idleFrameCount = 0;
                    var idleTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    EventHandler? idleSub = null;
                    idleSub = (_, _) =>
                    {
                        idleFrameCount++;
                        var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - idleTicks) / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;
                        if (elapsedMs >= 5000.0)
                        {
                            System.Windows.Media.CompositionTarget.Rendering -= idleSub;
                            idleFps5s = idleFrameCount / (elapsedMs / 1000.0);
                            StartMotionProbe();
                        }
                    };
                    System.Windows.Media.CompositionTarget.Rendering += idleSub;

                    void StartMotionProbe()
                    {
                        try
                        {
                            // 2) Synthetic motion: animate the blurred background's opacity
                            // by an invisible 1% for 3s — keeps composition dirty each vsync
                            // so we count real delivered frames (frame-pacing headroom).
                            var animFrameCount = 0;
                            var animTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                            EventHandler? animSub = null;
                            animSub = (_, _) =>
                            {
                                animFrameCount++;
                                var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - animTicks) / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;
                                if (elapsedMs >= 3000.0)
                                {
                                    System.Windows.Media.CompositionTarget.Rendering -= animSub;
                                    animFps3s = animFrameCount / (elapsedMs / 1000.0);
                                    WriteDiag();
                                }
                            };
                            if (mainWindow.FindName("BackgroundImage") is System.Windows.Controls.Image bgImg)
                            {
                                System.Windows.Media.CompositionTarget.Rendering += animSub;
                                bgImg.Opacity = 1.0;
                                var probeAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.99,
                                    System.TimeSpan.FromMilliseconds(3000))
                                {
                                    FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
                                };
                                probeAnim.Completed += (_, _) => bgImg.Opacity = 1.0;
                                bgImg.BeginAnimation(System.Windows.Controls.Image.OpacityProperty, probeAnim);
                                mainWindow.InvalidateVisual(); // kick-start the first frame
                            }
                            else
                            {
                                WriteDiag();
                            }
                        }
                        catch { WriteDiag(); }
                    }

                    void WriteDiag()
                    {
                        if (flInited) return;
                        flInited = true;
                        LogHelper.LogInfo($"RenderCapability.Tier = {tierValue} (2 = GPU hardware, <2 = software), MonitorHz = {(int)refreshHz}, HwndTarget.RenderMode = {hwndRenderMode}, IdleFps5s = {idleFps5s:0.0}, AnimationFps3s = {animFps3s:0.0}, RenderMode = {RenderOptions.ProcessRenderMode}, Adapter = {adapterName}");
                    }
                };
                measureTimer.Start();
            }
            else
            {
                LogHelper.LogInfo($"RenderCapability.Tier = {tierValue} (2 = GPU hardware, <2 = software), MonitorHz = {(int)refreshHz}, RenderMode = {RenderOptions.ProcessRenderMode}, Adapter = {adapterName}");
            }
        }
        catch { }

        try
        {
            // Step 1: Build DI container on background thread
            var services = new ServiceCollection();
            await Task.Run(() =>
            {
                ConfigureServices(services);
                ServiceProvider = services.BuildServiceProvider();
            });

            // Step 2: Start service host (SQLite init)
            await Task.Run(() =>
            {
                var provider = ServiceProvider!;

                _serviceHost = provider.GetRequiredService<MusicPlayerServiceHost>();
                _serviceHost.Start();

                // Pre-create all singleton services on background thread to avoid UI thread blocking
                provider.GetRequiredService<PlaybackService>();
                provider.GetRequiredService<MusicScanner>();
                provider.GetRequiredService<AlbumArtLoader>();
                provider.GetRequiredService<NavigationService>();
                provider.GetRequiredService<WindowsIntegrationService>();
                provider.GetRequiredService<SystemTrayService>();
                provider.GetRequiredService<SettingsService>();

                // Pre-warm the audio analyzer's singleton (its constructor starts a
                // Timer thread). Creating it here on a background thread during startup
                // avoids paying the one-time cost on the UI thread when the visualizer
                // row is first reached (the "first click after launch" freeze).
                try { _ = AudioAnalyzer.Instance; } catch { }

                // Pre-warm album-art image decoding (PNG/thumbnail pipeline) on a
                // background thread so the first row that needs an album cover does not
                // pay the one-time image-decoder init on the UI thread at first play.
                try { provider.GetRequiredService<AlbumArtLoader>().GetDefaultAlbumArt(); } catch { }

                // Pre-warm TagLib's first native parse + the full-quality art decode on
                // a background thread. Both are one-time per process and are otherwise
                // paid synchronously ON the UI thread at the very first track click
                // (the "first click" freeze): TagLib.File.Create lazily boots its native
                // tag registry and the full cover decode is a heavy first decode.
                try
                {
                    var db = provider.GetRequiredService<DatabaseService>();
                    var dbTracks = db.GetAllTracks();
                    if (dbTracks != null)
                    {
                        foreach (var t in dbTracks)
                        {
                            if (t == null || string.IsNullOrEmpty(t.FilePath) || !File.Exists(t.FilePath))
                                continue;
                            provider.GetRequiredService<AlbumArtLoader>().LoadFullAlbumArt(t);
                            break;
                        }
                    }
                }
                catch { }
            });

            AlbumArtLoader.Instance = ServiceProvider!.GetRequiredService<AlbumArtLoader>();

            // Step 3: Create ViewModel and set DataContext
            var vm = ServiceProvider!.GetRequiredService<MainViewModel>();
            mainWindow.DataContext = vm;

            // Step 4: Show content grid behind loading overlay (so layout starts while overlay covers it)
            mainWindow.LoadContent();

            // Step 5: Load all core data on background thread (loading overlay stays visible)
            await vm.InitializeAsync(status =>
            {
                mainWindow.Dispatcher.BeginInvoke(() =>
                {
                    try { mainWindow.LoadingStatusText.Text = status; } catch { }
                });
            });

            // Step 6: Initialize tray/integration services
            var hwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
            mainWindow.InitializeServices(hwnd);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup failed: {ex.Message}", "Music Player", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }

        base.OnStartup(e);
    }

    private static (double W, double H, double X, double Y, bool Maximized) ReadPersistedWindowBounds()
    {
        double w = 1280, h = 720, x = double.NaN, y = double.NaN;
        bool maximized = false;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            var db = new DatabaseService(AppPaths.DatabasePath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseService>.Instance);
            try
            {
                var s = db.GetSetting("WindowWidth", string.Empty);
                if (double.TryParse(s, out var wv) && wv > 0) w = wv;
                s = db.GetSetting("WindowHeight", string.Empty);
                if (double.TryParse(s, out var hv) && hv > 0) h = hv;
                s = db.GetSetting("WindowX", string.Empty);
                if (double.TryParse(s, out var xv)) x = xv;
                s = db.GetSetting("WindowY", string.Empty);
                if (double.TryParse(s, out var yv)) y = yv;
                maximized = db.GetSettingBool("WindowMaximized", false);
            }
            finally { db.Dispose(); }
        }
        catch { }
        return (w, h, x, y, maximized);
    }

    public static void PreventSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
    }

    public static void AllowSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<DatabaseService>(sp =>
            new DatabaseService(AppPaths.DatabasePath, sp.GetRequiredService<ILogger<DatabaseService>>()));
        
        services.AddSingleton<AudioEngine>();
        services.AddSingleton<MusicScanner>(sp =>
            new MusicScanner(sp.GetRequiredService<DatabaseService>(), sp.GetRequiredService<ILogger<MusicScanner>>()));
        
        services.AddSingleton<AlbumArtLoader>(sp =>
            new AlbumArtLoader(sp.GetRequiredService<DatabaseService>(), sp.GetRequiredService<ILogger<AlbumArtLoader>>()));
        
        services.AddSingleton<SettingsService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<PlaybackService>();
        services.AddSingleton<MusicPlayerServiceHost>();
        services.AddSingleton<WindowsIntegrationService>();
        services.AddSingleton<SystemTrayService>();
        services.AddSingleton<ArtistImageService>();
        services.AddSingleton<MainViewModel>();
        
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AllowSleep();
        try { _serviceHost?.Stop(); } catch { }
        try { ServiceProvider?.GetService<WindowsIntegrationService>()?.Dispose(); } catch { }
        try { ServiceProvider?.GetService<SystemTrayService>()?.Dispose(); } catch { }
        try { ServiceProvider?.GetService<AudioEngine>()?.Dispose(); } catch { }
        try { ServiceProvider?.GetService<DatabaseService>()?.Dispose(); } catch { }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        try { _singleInstanceMutex?.Dispose(); } catch { }
        try { (ServiceProvider as IDisposable)?.Dispose(); } catch { }
        base.OnExit(e);
        Environment.Exit(0);
    }
}

internal class MusicPlayerServiceHost
{
    private readonly DatabaseService _db;
    private readonly AudioEngine _engine;

    public MusicPlayerServiceHost(DatabaseService db, AudioEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    public void Start()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public void Stop()
    {
        _engine?.Dispose();
        _db?.Dispose();
    }
}
