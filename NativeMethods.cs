using System.Runtime.InteropServices;

namespace Hatch;

internal static class NativeMethods
{
    internal static readonly IntPtr HWND_TOPMOST    = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);
    internal const uint SWP_NOSIZE     = 0x0001;
    internal const uint SWP_NOMOVE     = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;

    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    
    internal const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateEllipticRgn(
        int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // Returns true when the window is maximized (WS_MAXIMIZE state).
    // Maximized windows overlap rcMonitor due to their hidden -8px resize border,
    // so they must be excluded from the true-fullscreen geometry check.
    [DllImport("user32.dll")]
    internal static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(
        IntPtr hMonitor, uint dpiType, out uint dpiX, out uint dpiY);

    internal const uint MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const int  HOTKEY_ID    = 0x4001;
    internal const uint WM_HOTKEY    = 0x0312;
    internal const uint MOD_ALT      = 0x0001;
    internal const uint MOD_CONTROL  = 0x0002;
    internal const uint MOD_SHIFT    = 0x0004;
    internal const uint MOD_WIN      = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    // comctl32 — native window subclassing (zero per-message overhead vs managed monitors)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate IntPtr SUBCLASSPROC(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    internal static extern bool SetWindowSubclass(
        IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    internal static extern bool RemoveWindowSubclass(
        IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    internal static extern IntPtr DefSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    internal const int  GWL_EXSTYLE               = -20;
    internal const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    internal const uint SWP_FRAMECHANGED          = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    internal static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    internal const uint DWMWA_NCRENDERING_POLICY      = 2;
    internal const uint DWMNCRP_DISABLED              = 1;
    internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const uint DWMWCP_DONOTROUND             = 1;   // opt out of Win11 rounded corners
    internal const uint DWMWA_BORDER_COLOR            = 34;
    internal const uint DWMWA_COLOR_NONE              = 0xFFFFFFFE;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // SHQueryUserNotificationState — detects presentation mode, D3D fullscreen, busy state.
    internal enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT             = 1, // screen saver / locked / fast-user-switched
        QUNS_BUSY                    = 2, // fullscreen app (non-D3D)
        QUNS_RUNNING_D3D_FULL_SCREEN = 3, // D3D fullscreen (games)
        QUNS_PRESENTATION_MODE       = 4, // Windows presentation mode active
        QUNS_ACCEPTS_NOTIFICATIONS   = 5, // normal desktop — show everything
        QUNS_QUIET_TIME              = 6, // first hour after new user login
        QUNS_APP                     = 7, // Windows Store app is foreground
    }

    [DllImport("shell32.dll")]
    internal static extern int SHQueryUserNotificationState(
        out QUERY_USER_NOTIFICATION_STATE pquns);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool EmptyWorkingSet(IntPtr proc);
}
