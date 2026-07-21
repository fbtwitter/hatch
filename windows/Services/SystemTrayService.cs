using System.Runtime.InteropServices;

namespace Hatch.Services;

public sealed class SystemTrayService : IDisposable
{
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_INFO = 0x10;
    private const uint NIIF_NOSOUND = 0x10;
    private const uint WM_TRAYICON = 0x0401;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const uint MF_STRING = 0x00;
    private const uint MF_SEPARATOR = 0x800;
    private const uint MF_POPUP = 0x10;
    private const uint TPM_RIGHTBUTTON = 0x02;
    private const uint TPM_RETURNCMD = 0x0100;
    private const int GWLP_WNDPROC = -4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type,
        int cx, int cy, uint fuLoad);

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x10;
    private const uint LR_DEFAULTSIZE = 0x40;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int newLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr SetWndLong(IntPtr hwnd, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, value)
            : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));

    private static IntPtr GetWndLong(IntPtr hwnd, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, index)
            : new IntPtr(GetWindowLong32(hwnd, index));

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private IntPtr _hwnd;
    private IntPtr _originalWndProc;
    private WndProc? _wndProcDelegate;
    private NOTIFYICONDATA _nid;
    private bool _iconVisible;
    private bool _disposed;
    private string _currentTooltip = "Hatch";
    private IntPtr _normalIcon;
    private IntPtr _hiddenIcon;

    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action? RestoreMascotRequested;
    public event Action<HideDuration>? HideMascotRequested;

    public enum HideDuration { OneHour, ThreeHours, UntilTomorrow, UntilRestart }

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;

        _wndProcDelegate = new WndProc(WndProcHandler);
        _originalWndProc = SetWndLong(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

        var baseDir = AppContext.BaseDirectory;
        var icoPath       = Path.Combine(baseDir, "Assets", "Hatch.ico");
        var hiddenIcoPath = Path.Combine(baseDir, "Assets", "HatchHidden.ico");

        _normalIcon = File.Exists(icoPath)
            ? LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE)
            : LoadIcon(IntPtr.Zero, new IntPtr(0x7F00));

        _hiddenIcon = File.Exists(hiddenIcoPath)
            ? LoadImage(IntPtr.Zero, hiddenIcoPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE)
            : _normalIcon;

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = (int)WM_TRAYICON,
            hIcon = _normalIcon,
            szTip = _currentTooltip
        };
    }

    public void SetTooltip(string tooltip)
    {
        if (_currentTooltip == tooltip) return;
        _currentTooltip = tooltip;

        if (_iconVisible)
        {
            _nid.szTip = tooltip;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }
    }

    public void SetHiddenState(bool hidden)
    {
        _nid.hIcon  = hidden ? _hiddenIcon : _normalIcon;
        _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        if (_iconVisible)
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    public void ShowBalloon(string title, string text)
    {
        if (!_iconVisible) return;
        _nid.uFlags      = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_INFO;
        _nid.szInfoTitle = title;
        _nid.szInfo      = text;
        _nid.dwInfoFlags = (int)NIIF_NOSOUND;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        // Reset so future NIM_MODIFY calls don't re-trigger the balloon
        _nid.uFlags      = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        _nid.szInfo      = string.Empty;
        _nid.szInfoTitle = string.Empty;
    }

    public void ShowIcon()
    {
        if (_iconVisible || _disposed) return;
        Shell_NotifyIcon(NIM_ADD, ref _nid);
        _iconVisible = true;
    }

    public void HideIcon()
    {
        if (!_iconVisible) return;
        Shell_NotifyIcon(NIM_DELETE, ref _nid);
        _iconVisible = false;
    }

    private IntPtr WndProcHandler(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            switch ((int)lParam & 0xFFFF)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    ShowRequested?.Invoke();
                    break;
                case WM_RBUTTONUP:
                    ShowContextMenu();
                    break;
            }
        }
        return CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        GetCursorPos(out var pt);
        var menu = CreatePopupMenu();

        bool isMascotHidden = App.MascotWindowInstance?.ViewModel.IsMascotHidden ?? false;
        string mainLabel = isMascotHidden ? "Restore Hatch" : "Open Hatch";

        AppendMenu(menu, MF_STRING, new IntPtr(1), mainLabel);
        AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);

        if (!isMascotHidden)
        {
            var hideMenu = CreatePopupMenu();
            AppendMenu(hideMenu, MF_STRING, new IntPtr(10), "1 hour");
            AppendMenu(hideMenu, MF_STRING, new IntPtr(11), "3 hours");
            AppendMenu(hideMenu, MF_STRING, new IntPtr(12), "Until tomorrow");
            AppendMenu(hideMenu, MF_STRING, new IntPtr(13), "Until restart");
            AppendMenu(menu, MF_POPUP, hideMenu, "Hide for...");
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
        }

        AppendMenu(menu, MF_STRING, new IntPtr(2), "Exit");

        SetForegroundWindow(_hwnd);
        var cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd == 1)
        {
            if (isMascotHidden)
                RestoreMascotRequested?.Invoke();
            else
                ShowRequested?.Invoke();
        }
        else if (cmd == 10) HideMascotRequested?.Invoke(HideDuration.OneHour);
        else if (cmd == 11) HideMascotRequested?.Invoke(HideDuration.ThreeHours);
        else if (cmd == 12) HideMascotRequested?.Invoke(HideDuration.UntilTomorrow);
        else if (cmd == 13) HideMascotRequested?.Invoke(HideDuration.UntilRestart);
        else if (cmd == 2)
            ExitRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_iconVisible)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _iconVisible = false;
        }
        if (_originalWndProc != IntPtr.Zero)
        {
            SetWndLong(_hwnd, GWLP_WNDPROC, _originalWndProc);
            _originalWndProc = IntPtr.Zero;
        }
        if (_hiddenIcon != IntPtr.Zero && _hiddenIcon != _normalIcon)
        {
            DestroyIcon(_hiddenIcon);
            _hiddenIcon = IntPtr.Zero;
        }
        if (_normalIcon != IntPtr.Zero)
        {
            DestroyIcon(_normalIcon);
            _normalIcon = IntPtr.Zero;
        }
    }
}
