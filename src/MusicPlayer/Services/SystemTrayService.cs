using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Services;

public sealed class SystemTrayService : IDisposable
{
    private readonly ILogger<SystemTrayService> _logger;
    private readonly Dispatcher _dispatcher;
    private nint _hwnd;
    private nint _hIcon;
    private bool _disposed;
    private bool _isVisible;

    private const int WM_TRAYICON = 0x800;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONUP = 0x0202;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int IDI_APPLICATION = 32512;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint ExtractIcon(nint hInst, string lpszExeFileName, uint nIconIndex);

    private static nint LoadAppIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                var hEx = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (hEx != IntPtr.Zero && hEx != (nint)1) return hEx;
            }
        }
        catch { }
        try
        {
            var hMod = GetModuleHandle(null);
            if (hMod != IntPtr.Zero)
            {
                var hApp = LoadIcon(hMod, (nint)1);
                if (hApp != IntPtr.Zero) return hApp;
            }
        }
        catch { }
        return LoadIcon(IntPtr.Zero, (nint)IDI_APPLICATION);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint tpmparams);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, nint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, int Msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;

    private const int CMD_PLAY_PAUSE = 1001;
    private const int CMD_NEXT = 1002;
    private const int CMD_PREVIOUS = 1003;
    private const int CMD_OPEN = 1004;
    private const int CMD_EXIT = 1005;

    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public SystemTrayService(ILogger<SystemTrayService> logger)
    {
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public void Initialize(nint hwnd)
    {
        _hwnd = hwnd;
        try
        {
            _hIcon = LoadAppIcon();

            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "Music Player"
            };

            Shell_NotifyIcon(NIM_ADD, ref nid);
            _isVisible = true;
            _logger.LogInformation("System tray icon created");
        }
        catch { }
    }

    public void ShowBalloon(string title, string message)
    {
        if (!_isVisible) return;
        try
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_INFO,
                szInfoTitle = title ?? string.Empty,
                szInfo = message ?? string.Empty,
                dwInfoFlags = 0x01
            };
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }
        catch { }
    }

    public void UpdateTooltip(string text)
    {
        if (!_isVisible) return;
        try
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_TIP,
                szTip = text ?? string.Empty
            };
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }
        catch { }
    }

    public void HandleCallbackMessage(int lParam)
    {
        int msg = lParam & 0xFFFF;

        if (msg == WM_LBUTTONDBLCLK)
        {
            OpenRequested?.Invoke();
        }
        else if (msg == WM_RBUTTONUP)
        {
            ShowContextMenu();
        }
        else if (msg == WM_LBUTTONUP)
        {
            OpenRequested?.Invoke();
        }
    }

    private void ShowContextMenu()
    {
        nint hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING, CMD_PLAY_PAUSE, "Play/Pause");
        AppendMenu(hMenu, MF_STRING, CMD_NEXT, "Next");
        AppendMenu(hMenu, MF_STRING, CMD_PREVIOUS, "Previous");
        AppendMenu(hMenu, MF_SEPARATOR, 0, "");
        AppendMenu(hMenu, MF_STRING, CMD_OPEN, "Open");
        AppendMenu(hMenu, MF_SEPARATOR, 0, "");
        AppendMenu(hMenu, MF_STRING, CMD_EXIT, "Exit");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        int cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, pt.X, pt.Y, _hwnd, 0);
        DestroyMenu(hMenu);

        switch (cmd)
        {
            case CMD_PLAY_PAUSE: PlayPauseRequested?.Invoke(); break;
            case CMD_NEXT: NextRequested?.Invoke(); break;
            case CMD_PREVIOUS: PreviousRequested?.Invoke(); break;
            case CMD_OPEN: OpenRequested?.Invoke(); break;
            case CMD_EXIT: ExitRequested?.Invoke(); break;
        }
    }

    public void RemoveIcon()
    {
        if (!_isVisible) return;
        try
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
        }
        catch { }
        _isVisible = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemoveIcon();
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
    }
}
