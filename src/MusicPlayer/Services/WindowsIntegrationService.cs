using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using MusicPlayer.Library;
using MusicPlayer.Models;
using MusicPlayer.Utilities;
using Windows.Media;
using Windows.Storage.Streams;

namespace MusicPlayer.Services;

public sealed class WindowsIntegrationService : IDisposable
{
    private readonly ILogger<WindowsIntegrationService>? _logger;
    private nint _hwnd;
    private bool _disposed;
    private Dispatcher? _dispatcher;
    private SystemMediaTransportControls? _smtc;
    private Func<(TimeSpan position, TimeSpan duration)?>? _timelineProvider;
    private int _positionTickCount;

    private const int HOTKEY_PLAY_PAUSE = 1;
    private const int HOTKEY_NEXT = 2;
    private const int HOTKEY_PREV = 3;
    private const int HOTKEY_F_PREV = 4;
    private const int HOTKEY_F_PLAY_PAUSE = 5;
    private const int HOTKEY_F_NEXT = 6;
    private const int WM_HOTKEY = 0x0312;

    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const int WM_KEYDOWN = 0x0100;
    private const uint VK_MEDIA_PREV_TRACK = 0xB0;
    private const uint VK_MEDIA_NEXT_TRACK = 0xB1;
    private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint LLKHF_INJECTED = 0x00000010;

    private nint _hookHandle;
    private LowLevelKeyboardProc? _hookProc;
    private long _hookHandledAtMs;
    private uint _hookHandledVk;
    private const int _hookRepeatGraceMs = 250;
    private const int _smtcSamePressMaxMs = 400;
    private const int _hookToggleDelayMs = 0;   // لا تأخير - الاستجابة فورية لمفاتيح الوسائط
    private DispatcherTimer? _delayedToggleTimer;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;

    public WindowsIntegrationService(ILogger<WindowsIntegrationService>? logger = null)
    {
        _logger = logger;
    }

    public void SetTimelineProvider(Func<(TimeSpan position, TimeSpan duration)?> provider)
    {
        _timelineProvider = provider;
    }

    public void SetWindowHandle(nint hwnd)
    {
        _hwnd = hwnd;
        try { _dispatcher = Dispatcher.CurrentDispatcher; } catch { }

        // NOTE: media VKs (0xB0/0xB1/0xB3) are intentionally NOT registered.
        // Registering them steals the press before Windows' own media-key
        // monitor sees it, which suppresses the native media overlay card.
        // Hardware media keys now flow through WM_APPCOMMAND (HandleAppCommand)
        // + SMTC, letting Windows show its overlay as it does for volume keys.
        bool rF9 = RegisterHotKey(hwnd, HOTKEY_F_PREV, 0, 0x78);
        bool rF10 = RegisterHotKey(hwnd, HOTKEY_F_PLAY_PAUSE, 0, 0x79);
        bool rF11 = RegisterHotKey(hwnd, HOTKEY_F_NEXT, 0, 0x7A);
        int winErr = Marshal.GetLastWin32Error();
        LogHelper.LogInfo($"[HotKey] register results: F9={rF9} F10={rF10} F11={rF11} lastErr={winErr}");

        // Low-level keyboard hook: catches hardware media keys (0xB0/0xB1/0xB3)
        // even while the window is minimized or in the background. WM_HOTKEY never
        // fires for these keys because laptops send a media command, not a plain
        // F-key VK. When our window IS the foreground window the hook passes the
        // key through untouched so SMTC (and the native overlay card) keeps working.
        try
        {
            _hookProc = LowLevelKeyboardProcCallback;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
            if (_hookHandle == IntPtr.Zero)
                LogHelper.LogInfo($"[Hook] install failed lastErr={Marshal.GetLastWin32Error()}");
            else
                LogHelper.LogInfo("[Hook] keyboard hook installed (media keys work when minimized)");
        }
        catch (Exception ex)
        {
            LogHelper.LogInfo($"[Hook] install exception: {ex.Message}");
        }

        InitSmtc(hwnd);

        _logger?.LogInformation("Global media hotkeys registered");
    }

    private nint LowLevelKeyboardProcCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode == HC_ACTION && wParam == WM_KEYDOWN)
        {
            try
            {
                var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((kbd.flags & LLKHF_INJECTED) == 0)
                {
                    Action? raise = KeyToAction(kbd.vkCode);
                    if (raise != null)
                    {
                        bool foreground = GetForegroundWindow() == _hwnd;
                        if (!foreground)
                        {
                            long now = Environment.TickCount64;
                            bool isRepeat = kbd.vkCode == _hookHandledVk && now - _hookHandledAtMs < _hookRepeatGraceMs;
                            if (!isRepeat)
                            {
                                _hookHandledAtMs = now;
                                _hookHandledVk = kbd.vkCode;
                                LogHelper.LogInfo($"[Hook] media key 0x{kbd.vkCode:X2} handled (not foreground)");
                                // Do NOT toggle synchronously. Falling through to
                                // CallNextHookEx lets Windows' media monitor see the
                                // press first so its native overlay card appears even
                                // while minimized. The actual toggle is deferred a few
                                // ms so the two don't race; OnSmtcButtonPressed dedups
                                // the echo so a single press is never toggled twice.
                                var d = _dispatcher;
                                if (d != null && !d.CheckAccess()) d.BeginInvoke(delegate
                                {
                                    ScheduleDelayedToggle(raise);
                                });
                                else ScheduleDelayedToggle(raise);
                            }
                            // falls through to CallNextHookEx
                        }
                    }
                }
            }
            catch { }
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private Action? KeyToAction(uint vkCode)
    {
        return vkCode switch
        {
            VK_MEDIA_PREV_TRACK => PreviousRequested,
            VK_MEDIA_NEXT_TRACK => NextRequested,
            VK_MEDIA_PLAY_PAUSE => PlayPauseRequested,
            _ => null,
        };
    }

    private void ScheduleDelayedToggle(Action? raise)
    {
        if (raise == null) return;
        try
        {
            _delayedToggleTimer?.Stop();
            _delayedToggleTimer = new DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(_hookToggleDelayMs)
            };
            _delayedToggleTimer.Tick += delegate
            {
                _delayedToggleTimer?.Stop();
                raise();
            };
            _delayedToggleTimer.Start();
        }
        catch
        {
            raise();
        }
    }

    private void InitSmtc(nint hwnd)
    {
        try
        {
            _smtc = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
            _smtc.IsPlayEnabled = true;
            _smtc.IsPauseEnabled = true;
            _smtc.IsNextEnabled = true;
            _smtc.IsPreviousEnabled = true;
            _smtc.ButtonPressed += OnSmtcButtonPressed;
            _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            _logger?.LogInformation("SMTC initialized: Windows media overlay enabled");
            LogHelper.LogInfo("[SMTC] initialized: Windows media overlay enabled");
        }
        catch (Exception ex)
        {
            _smtc = null;
            _logger?.LogWarning(ex, "SMTC init failed; media hotkeys remain as fallback");
            LogHelper.LogInfo("[SMTC] init failed; media hotkeys remain as fallback");
        }
    }

    private void OnSmtcButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        try
        {
            long now = Environment.TickCount64;
            if (now - _hookHandledAtMs < _smtcSamePressMaxMs)
            {
                // The low-level keyboard hook already handled this press (it passes
                // the key through so the media overlay still appears). Swallow the
                // SMTC echo to avoid toggling twice.
                LogHelper.LogInfo($"[SMTC] ButtonPressed: {args.Button} (echo swallowed, hook recent)");
                return;
            }
            LogHelper.LogInfo($"[SMTC] ButtonPressed: {args.Button}");
            Action? raise = args.Button switch
            {
                SystemMediaTransportControlsButton.Play => PlayPauseRequested,
                SystemMediaTransportControlsButton.Pause => PlayPauseRequested,
                SystemMediaTransportControlsButton.Next => NextRequested,
                SystemMediaTransportControlsButton.Previous => PreviousRequested,
                _ => null,
            };
            if (raise == null) return;
            var d = _dispatcher;
            if (d != null && !d.CheckAccess()) d.BeginInvoke(raise);
            else raise();
        }
        catch { }
    }

    public void HandleHotKey(int hotkeyId)
    {
        LogHelper.LogInfo($"[HotKey] pressed id={hotkeyId}");
        // تنفيذ أوامر الصوت على خيط خلفي لتجنب تعليق خيط UI
        // Play/Pause/Next/Previous هي عمليات NAudio ولا تحتاج إلى خيط UI
        Action? raise = hotkeyId switch
        {
            HOTKEY_PLAY_PAUSE or HOTKEY_F_PLAY_PAUSE => PlayPauseRequested,
            HOTKEY_NEXT or HOTKEY_F_NEXT => NextRequested,
            HOTKEY_PREV or HOTKEY_F_PREV => PreviousRequested,
            _ => null
        };
        if (raise != null)
            Task.Run(raise);
    }

    public void UpdateTrackInfo(MusicTrack track)
    {
        var smtc = _smtc;
        if (smtc == null || track == null) return;
        try
        {
            var updater = smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = string.IsNullOrWhiteSpace(track.Title) ? "Unknown Title" : track.Title;
            updater.MusicProperties.Artist = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown Artist" : track.Artist;
            updater.MusicProperties.AlbumTitle = track.Album ?? string.Empty;
            updater.Update();
            UpdateTimeline(track);
            _ = SetThumbnailAsync(track);
        }
        catch { }
    }

    public void ReportPlaying() => SetStatus(MediaPlaybackStatus.Playing);
    public void ReportPaused() => SetStatus(MediaPlaybackStatus.Paused);
    public void ReportStopped() => SetStatus(MediaPlaybackStatus.Stopped);

    public void RefreshTimeline() => UpdateTimeline(null);

    public void ReportPositionTick()
    {
        // PositionChanged fires ~4x/sec; refresh the overlay progress ~every 4s.
        if (++_positionTickCount % 16 == 0) UpdateTimeline(null);
    }

    private void SetStatus(MediaPlaybackStatus status)
    {
        var smtc = _smtc;
        if (smtc == null) return;
        try
        {
            smtc.PlaybackStatus = status;
            UpdateTimeline(null);
        }
        catch { }
    }

    private void UpdateTimeline(MusicTrack? trackHint)
    {
        var smtc = _smtc;
        if (smtc == null) return;
        try
        {
            var tp = _timelineProvider?.Invoke();
            if (tp == null) return;
            var (pos, dur) = tp.Value;
            if (dur <= TimeSpan.Zero && trackHint != null && trackHint.Duration > 0)
                dur = TimeSpan.FromMilliseconds(trackHint.Duration);
            if (dur <= TimeSpan.Zero) return;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (pos > dur) pos = dur;
            smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                EndTime = dur,
                Position = pos,
                MinSeekTime = TimeSpan.Zero,
                MaxSeekTime = dur
            });
        }
        catch { }
    }

    private async Task SetThumbnailAsync(MusicTrack track)
    {
        try
        {
            var smtc = _smtc;
            if (smtc == null || track == null) return;
            RandomAccessStreamReference? thumb = null;
            // AlbumArtPath is never populated; the real art lives in the loader's
            // disk cache ({md5}.jpg). Resolve it off the UI thread (TagLib fallback).
            string? artFile = null;
            if (!string.IsNullOrWhiteSpace(track.AlbumArtPath) && File.Exists(track.AlbumArtPath))
                artFile = track.AlbumArtPath;
            else
                artFile = await Task.Run(() => AlbumArtLoader.Instance?.GetCachedArtFilePath(track.FilePath));
            if (!string.IsNullOrWhiteSpace(artFile) && File.Exists(artFile))
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(artFile);
                thumb = RandomAccessStreamReference.CreateFromFile(file);
                LogHelper.LogInfo("[SMTC] thumbnail: album art");
            }
            else
            {
                thumb = await GetDefaultArtThumbnailAsync();
                LogHelper.LogInfo("[SMTC] thumbnail: default art (no embedded art)");
            }
            if (thumb == null) return;
            var current = _smtc;
            if (current == null) return;
            current.DisplayUpdater.Thumbnail = thumb;
            current.DisplayUpdater.Update();
        }
        catch { }
    }

    /// <summary>
    /// Rasterizes the app's real default album art (dark vinyl + blue note,
    /// the same image shown in Full Player) to a PNG stream for SMTC.
    /// Falls back to the app logo only if rendering fails.
    /// Must run on the UI thread (RenderTargetBitmap requirement).
    /// </summary>
    private static async Task<RandomAccessStreamReference?> GetDefaultArtThumbnailAsync()
    {
        try
        {
            var loader = AlbumArtLoader.Instance;
            var src = loader?.GetDefaultAlbumArt();
            if (src == null) return await GetEmbeddedLogoThumbnailAsync();
            const int size = 300;
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(src, new System.Windows.Rect(0, 0, size, size));
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            var mem = new InMemoryRandomAccessStream();
            using (var outStream = mem.GetOutputStreamAt(0))
            {
                var writer = new DataWriter(outStream);
                var buf = new byte[8192];
                int n;
                while ((n = ms.Read(buf, 0, buf.Length)) > 0)
                    writer.WriteBytes(buf.AsSpan(0, n).ToArray());
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            return RandomAccessStreamReference.CreateFromStream(mem);
        }
        catch { return await GetEmbeddedLogoThumbnailAsync(); }
    }

    private static async Task<RandomAccessStreamReference?> GetEmbeddedLogoThumbnailAsync()
    {
        try
        {
            var sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app_logo.png"));
            if (sri?.Stream == null) return null;
            using var src = sri.Stream;
            var mem = new InMemoryRandomAccessStream();
            using (var outStream = mem.GetOutputStreamAt(0))
            {
                var writer = new DataWriter(outStream);
                var buf = new byte[8192];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    writer.WriteBytes(buf.AsSpan(0, n).ToArray());
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            return RandomAccessStreamReference.CreateFromStream(mem);
        }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_smtc != null)
            {
                _smtc.ButtonPressed -= OnSmtcButtonPressed;
                _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                _smtc = null;
            }
        }
        catch { }

        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_F_PREV);
            UnregisterHotKey(_hwnd, HOTKEY_F_PLAY_PAUSE);
            UnregisterHotKey(_hwnd, HOTKEY_F_NEXT);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _hookProc = null;
        }

        try { _delayedToggleTimer?.Stop(); } catch { }
        _delayedToggleTimer = null;
    }
}
