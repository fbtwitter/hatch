using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Hatch.Views;

namespace Hatch.ViewModels;

public sealed class MascotViewModel : INotifyPropertyChanged, IDisposable
{
    internal const int WindowSize = 120;
    private const int EdgePadding = 20;

    private readonly DispatcherQueue _dispatcher;
    private PeriodicTimer? _pollTimer;
    private CancellationTokenSource? _cts;
    private PeriodicTimer? _hideRestoreTimer;
    private CancellationTokenSource? _hideRestoreCts;
    private bool _isVisible = true;
    private bool _isDragging;
    private NativeMethods.POINT _dragStartCursor;
    private int _dragStartWindowX;
    private int _dragStartWindowY;
    private bool _isBubbleOpen;
    private int _bubbleX;
    private int _bubbleY;
    private bool _isMascotHidden;

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public int X
    {
        get => App.Settings.MascotX;
        set
        {
            if (App.Settings.MascotX == value) return;
            App.Settings.MascotX = value;
            if (!_isDragging) _ = App.SettingsService.SaveAsync();
            OnPropertyChanged();
        }
    }

    public int Y
    {
        get => App.Settings.MascotY;
        set
        {
            if (App.Settings.MascotY == value) return;
            App.Settings.MascotY = value;
            if (!_isDragging) _ = App.SettingsService.SaveAsync();
            OnPropertyChanged();
        }
    }

    public bool MuteAnimation
    {
        get => App.Settings.MuteAnimation;
        set
        {
            if (App.Settings.MuteAnimation == value) return;
            App.Settings.MuteAnimation = value;
            _ = App.SettingsService.SaveAsync();
            OnPropertyChanged();
        }
    }

    // Called by SettingsViewModel so MascotWindow responds without re-saving.
    public void RaiseMuteChanged() => OnPropertyChanged(nameof(MuteAnimation));

    public string? LottieFilePath => App.Settings.LottieFilePath;
    public void RaiseLottieFileChanged() => OnPropertyChanged(nameof(LottieFilePath));

    public bool IsDragging => _isDragging;

    public void CloseBubble() => IsBubbleOpen = false;

    public bool IsBubbleOpen
    {
        get => _isBubbleOpen;
        private set
        {
            if (_isBubbleOpen == value) return;
            _isBubbleOpen = value;
            OnPropertyChanged();
        }
    }

    public int BubbleX
    {
        get => _bubbleX;
        private set
        {
            if (_bubbleX == value) return;
            _bubbleX = value;
            OnPropertyChanged();
        }
    }

    public int BubbleY
    {
        get => _bubbleY;
        private set
        {
            if (_bubbleY == value) return;
            _bubbleY = value;
            OnPropertyChanged();
        }
    }

    public ICommand ResetPositionCommand      { get; }
    public ICommand ShowMainWindowCommand     { get; }
    public ICommand ToggleMainWindowCommand   { get; }
    public ICommand ToggleBubbleCommand       { get; }
    public ICommand HideFor1HourCommand       { get; }
    public ICommand HideFor3HoursCommand      { get; }
    public ICommand HideUntilTomorrowCommand  { get; }
    public ICommand HideUntilRestartCommand   { get; }
    public ICommand RestoreFromHideCommand    { get; }

    public bool IsMascotHidden
    {
        get => _isMascotHidden;
        private set
        {
            if (_isMascotHidden == value) return;
            _isMascotHidden = value;
            OnPropertyChanged();
        }
    }

    public MascotViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        ResetPositionCommand     = new RelayCommand(_ => ResetPosition());
        ShowMainWindowCommand    = new RelayCommand(_ => ShowMainWindow());
        ToggleMainWindowCommand  = new RelayCommand(_ => ToggleMainWindow());
        ToggleBubbleCommand      = new RelayCommand(_ => ToggleBubble());
        HideFor1HourCommand      = new RelayCommand(_ => HideFor(TimeSpan.FromHours(1)));
        HideFor3HoursCommand     = new RelayCommand(_ => HideFor(TimeSpan.FromHours(3)));
        HideUntilTomorrowCommand = new RelayCommand(_ => HideFor(UntilTomorrow()));
        HideUntilRestartCommand  = new RelayCommand(_ => HideUntilRestart());
        RestoreFromHideCommand   = new RelayCommand(_ => RestoreFromHide());
        InitializePosition();
        StartFullscreenPolling();
        CheckHideExpiration();
    }

    public bool LockPosition
    {
        get => App.Settings.LockMascotPosition;
        set
        {
            if (App.Settings.LockMascotPosition == value) return;
            App.Settings.LockMascotPosition = value;
            _ = App.SettingsService.SaveAsync();
            OnPropertyChanged();
        }
    }

    public void RaiseLockPositionChanged() => OnPropertyChanged(nameof(LockPosition));

    public void BeginDrag(int windowX, int windowY)
    {
        if (App.Settings.LockMascotPosition) return;
        NativeMethods.GetCursorPos(out _dragStartCursor);
        _dragStartWindowX = windowX;
        _dragStartWindowY = windowY;
        _isDragging = true;
    }

    public void ContinueDrag()
    {
        if (!_isDragging) return;
        NativeMethods.GetCursorPos(out var cur);
        var newX = _dragStartWindowX + (cur.X - _dragStartCursor.X);
        var newY = _dragStartWindowY + (cur.Y - _dragStartCursor.Y);

        // Clamp to the monitor's work area during drag
        var pt = new NativeMethods.POINT { X = newX, Y = newY };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
        {
            var w = mi.rcWork;
            newX = Math.Clamp(newX, w.left, w.right  - WindowSize);
            newY = Math.Clamp(newY, w.top,  w.bottom - WindowSize);
        }

        X = newX;
        Y = newY;
    }

    public void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        _ = App.SettingsService.SaveAsync();
    }

    private void ResetPosition()
    {
        var workArea = DisplayArea.Primary.WorkArea;
        X = workArea.X + workArea.Width  - WindowSize - EdgePadding;
        Y = workArea.Y + workArea.Height - WindowSize - EdgePadding;
        _ = App.SettingsService.SaveAsync();
    }

    private static void ShowMainWindow() => App.MainWindowInstance?.Activate();

    private static void ToggleMainWindow()
    {
        var win = App.MainWindowInstance;
        if (win == null) return;

        var mainHwnd = Win32Interop.GetWindowFromWindowId(win.AppWindow.Id);

        if (!win.AppWindow.IsVisible)
        {
            // Close the bubble if open — only one panel at a time
            if (App.MascotWindowInstance?.ViewModel.IsBubbleOpen == true)
                App.MascotWindowInstance.ViewModel.CloseBubble();

            PositionMainWindowNearMascot(win);
            win.AppWindow.Show();

            NativeMethods.SetWindowPos(
                mainHwnd,
                NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE);

            return;
        }

        win.AppWindow.Hide();
    }

    internal static void PositionMainWindowNearMascot(MainWindow win)
    {
        const int logicalWidth  = 520;
        const int logicalHeight = 640;
        const int gap           = 12;

        int mascotX = App.Settings.MascotX;
        int mascotY = App.Settings.MascotY;

        var pt       = new NativeMethods.POINT { X = mascotX + WindowSize / 2, Y = mascotY + WindowSize / 2 };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi       = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return;

        NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        double scale       = dpiX / 96.0;
        int winWidth       = (int)Math.Round(logicalWidth  * scale);
        int winHeight      = (int)Math.Round(logicalHeight * scale);
        int scaledGap      = (int)Math.Round(gap * scale);

        var w = mi.rcWork;

        // Prefer left of mascot; flip to right if it doesn't fit
        int x = mascotX - winWidth - scaledGap;
        if (x < w.left)
            x = mascotX + WindowSize + scaledGap;

        // Vertically centre the main window on the mascot
        int mascotCenterY = mascotY + WindowSize / 2;
        int y = mascotCenterY - winHeight / 2;
        y = Math.Clamp(y, w.top + scaledGap, w.bottom - winHeight);
        x = Math.Clamp(x, w.left, w.right - winWidth);

        win.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void ToggleBubble()
    {
        if (IsBubbleOpen)
        {
            IsBubbleOpen = false;
        }
        else
        {
            IsBubbleOpen = true;

            // Hide the main window after the bubble is already opening — only one panel at a time.
            // Hiding first causes a focus-transition stall that makes the bubble feel slow.
            if (App.MainWindowInstance?.AppWindow.IsVisible == true)
                App.MainWindowInstance.AppWindow.Hide();
        }
    }
            private void InitializePosition()
    {
        if (App.Settings.MascotX < 0 || App.Settings.MascotY < 0)
        {
            var workArea = DisplayArea.Primary.WorkArea;
            App.Settings.MascotX = workArea.X + workArea.Width  - WindowSize - EdgePadding;
            App.Settings.MascotY = workArea.Y + workArea.Height - WindowSize - EdgePadding;
            _ = App.SettingsService.SaveAsync();
        }
        else
        {
            ClampToWorkArea();
        }
    }

    // Clamps stored position so the window stays fully inside its monitor's work area.
    private static void ClampToWorkArea()
    {
        var pt = new NativeMethods.POINT { X = App.Settings.MascotX, Y = App.Settings.MascotY };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return;

        var w = mi.rcWork;
        App.Settings.MascotX = Math.Clamp(App.Settings.MascotX, w.left, w.right  - WindowSize);
        App.Settings.MascotY = Math.Clamp(App.Settings.MascotY, w.top,  w.bottom - WindowSize);
    }

    private void StartFullscreenPolling()
    {
        _cts = new CancellationTokenSource();
        _pollTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _ = PollFullscreenAsync(_cts.Token);
    }

    private async Task PollFullscreenAsync(CancellationToken ct)
    {
        try
        {
            while (await _pollTimer!.WaitForNextTickAsync(ct))
            {
                var isFull = App.Settings.HideWhenFullscreen && IsForegroundWindowFullscreen();
                _dispatcher.TryEnqueue(() => IsVisible = !isFull);
            }
        }
        catch (OperationCanceledException) { }
    }

    // Returns true when the user is in a context where Hatch should stay out of the way:
    // fullscreen window covering a monitor, D3D fullscreen (games), or Windows presentation mode.
    private static bool IsForegroundWindowFullscreen()
    {
        // Fast path: presentation mode or D3D fullscreen (games) via shell API.
        if (NativeMethods.SHQueryUserNotificationState(out var quns) == 0)
        {
            if (quns == NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE ||
                quns == NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                quns == NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY)
                return true;
        }

        // Geometry check: any non-Hatch window that covers the full monitor bounds.
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if ((int)pid == Environment.ProcessId) return false;

        var hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return false;

        NativeMethods.GetWindowRect(hwnd, out var wr);
        var mr = mi.rcMonitor;
        return wr.left <= mr.left && wr.top <= mr.top &&
               wr.right >= mr.right && wr.bottom >= mr.bottom;
    }

    private static TimeSpan UntilTomorrow()
    {
        var tomorrow = DateTime.Now.Date.AddDays(1);
        return tomorrow - DateTime.Now;
    }

    private void HideFor(TimeSpan duration)
    {
        var hideUntil = DateTime.UtcNow.Add(duration);
        App.Settings.HideUntilTicks = hideUntil.Ticks;
        _ = App.SettingsService.SaveAsync();

        IsMascotHidden = true;

        if (IsBubbleOpen)
            CloseBubble();

        if (App.MainWindowInstance?.GetTrayService() is { } tray)
        {
            tray.SetTooltip("Hatch is hidden — right-click to restore");
            tray.SetHiddenState(true);
            tray.ShowBalloon("Hatch hidden", FormatHideDuration(duration));
        }

        StartHideRestoreTimer();
    }

    private void HideUntilRestart()
    {
        // No expiry tick — stays hidden until the app is restarted
        App.Settings.HideUntilTicks = long.MaxValue;
        _ = App.SettingsService.SaveAsync();

        IsMascotHidden = true;

        if (IsBubbleOpen)
            CloseBubble();

        if (App.MainWindowInstance?.GetTrayService() is { } tray)
        {
            tray.SetTooltip("Hatch is hidden — right-click to restore");
            tray.SetHiddenState(true);
            tray.ShowBalloon("Hatch hidden", "Hidden until restart. Right-click the tray icon to restore.");
        }

        // No timer needed — only restores via RestoreFromHide()
    }

    private static string FormatHideDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 23)
            return "Hidden until tomorrow. Right-click the tray icon to restore.";
        if (duration.TotalHours >= 2)
            return $"Hidden for {(int)duration.TotalHours} hours. Right-click the tray icon to restore.";
        return "Hidden for 1 hour. Right-click the tray icon to restore.";
    }

    // Applied on cold start when settings indicate the mascot was already hidden.
    // No balloon here — the user didn't just take an action.
    private static void ApplyHiddenTrayState()
    {
        if (App.MainWindowInstance?.GetTrayService() is { } tray)
        {
            tray.SetTooltip("Hatch is hidden — right-click to restore");
            tray.SetHiddenState(true);
        }
    }

    private void RestoreFromHide()
    {
        App.Settings.HideUntilTicks = null;
        _ = App.SettingsService.SaveAsync();

        IsMascotHidden = false;
        StopHideRestoreTimer();

        if (App.MainWindowInstance?.GetTrayService() is { } tray)
        {
            tray.SetTooltip("Hatch");
            tray.SetHiddenState(false);
        }
    }

    private void CheckHideExpiration()
    {
        if (App.Settings.HideUntilTicks == null)
        {
            IsMascotHidden = false;
            return;
        }

        // long.MaxValue means "until restart" — stay hidden, no timer needed
        if (App.Settings.HideUntilTicks.Value == long.MaxValue)
        {
            IsMascotHidden = true;
            ApplyHiddenTrayState();
            return;
        }

        var hideUntil = new DateTime(App.Settings.HideUntilTicks.Value, DateTimeKind.Utc);
        if (DateTime.UtcNow >= hideUntil)
        {
            RestoreFromHide();
            return;
        }

        IsMascotHidden = true;
        ApplyHiddenTrayState();
        StartHideRestoreTimer();
    }

    private void StartHideRestoreTimer()
    {
        StopHideRestoreTimer();
        _hideRestoreCts = new CancellationTokenSource();
        _hideRestoreTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        _ = PollHideExpirationAsync(_hideRestoreCts.Token);
    }

    private void StopHideRestoreTimer()
    {
        _hideRestoreCts?.Cancel();
        _hideRestoreCts?.Dispose();
        _hideRestoreCts = null;
        _hideRestoreTimer?.Dispose();
        _hideRestoreTimer = null;
    }

    private async Task PollHideExpirationAsync(CancellationToken ct)
    {
        try
        {
            while (await _hideRestoreTimer!.WaitForNextTickAsync(ct))
            {
                if (App.Settings.HideUntilTicks != null &&
                    App.Settings.HideUntilTicks.Value != long.MaxValue)
                {
                    var hideUntil = new DateTime(App.Settings.HideUntilTicks.Value, DateTimeKind.Utc);
                    if (DateTime.UtcNow >= hideUntil)
                    {
                        _dispatcher.TryEnqueue(() => RestoreFromHide());
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _pollTimer?.Dispose();
        StopHideRestoreTimer();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
