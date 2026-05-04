using System.Runtime.InteropServices;

namespace TodoWinUI3.Services;

public sealed class SystemTrayService : IDisposable
{
    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const uint WM_TRAYICON = 0x0401;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const uint MF_STRING = 0x00;
    private const uint MF_SEPARATOR = 0x800;
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

    public event Action? ShowRequested;
    public event Action? ExitRequested;

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;

        _wndProcDelegate = new WndProc(WndProcHandler);
        _originalWndProc = SetWndLong(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = (int)WM_TRAYICON,
            hIcon = LoadIcon(IntPtr.Zero, new IntPtr(0x7F00)), // IDI_APPLICATION
            szTip = "To-Do"
        };
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
        AppendMenu(menu, MF_STRING, new IntPtr(1), "Open To-Do");
        AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
        AppendMenu(menu, MF_STRING, new IntPtr(2), "Exit");

        SetForegroundWindow(_hwnd);
        var cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd == 1)
            ShowRequested?.Invoke();
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
    }
}
