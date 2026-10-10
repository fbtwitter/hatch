using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.ViewModels;

internal enum MainWindowAction
{
    Show,
    Toggle
}

public sealed class MascotViewModel : INotifyPropertyChanged, IDisposable
{
    private const int EdgePadding = 20;

    private readonly DispatcherQueue _dispatcher;
    private readonly SettingsService _settings;
    private readonly TipCoordinator _tipCoordinator;
    private readonly IReadOnlyList<TodoItem> _tasks;
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
        get => _settings.Current.MascotX;
        set
        {
            if (_settings.Current.MascotX == value) return;
            _settings.Current.MascotX = value;
            if (!_isDragging) _settings.SaveDebounced();
            OnPropertyChanged();
        }
    }

    public int Y
    {
        get => _settings.Current.MascotY;
        set
        {
            if (_settings.Current.MascotY == value) return;
            _settings.Current.MascotY = value;
            if (!_isDragging) _settings.SaveDebounced();
            OnPropertyChanged();
        }
    }

    public int WindowSize => _settings.Current.MascotSize;

    public bool MuteAnimation
    {
        get => _settings.Current.MuteAnimation;
        set
        {
            if (_settings.Current.MuteAnimation == value) return;
            _settings.Current.MuteAnimation = value;
            _settings.SaveDebounced();
            OnPropertyChanged();
        }
    }

    // Called by SettingsViewModel so MascotWindow responds without re-saving.
    public void RaiseMuteChanged() => OnPropertyChanged(nameof(MuteAnimation));

    public void ApplyShowMascotChanged() => RefreshVisibility();

    public void RefreshVisibility()
    {
        IsVisible = _settings.Current.ShowMascot && !IsMascotHidden &&
            !(_settings.Current.HideWhenFullscreen &&
              IsForegroundWindowFullscreen());
        if (!IsVisible) CloseBubble();
    }

    public void RefreshPosition() => ClampToWorkArea();

    public string? LottieFilePath => _settings.Current.LottieFilePath;
    public void RaiseLottieFileChanged() => OnPropertyChanged(nameof(LottieFilePath));

    // Called by SettingsViewModel after saving MascotSize so MascotWindow responds without re-saving.
    public void RaiseWindowSizeChanged()
    {
        OnPropertyChanged(nameof(WindowSize));
        ClampToWorkArea();
    }

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
            RefreshVisibility();
        }
    }

    // Fires on the UI thread when an opted-in planning or inspiration moment is due.
    // MascotWindow owns the popup and records a daily slot only after showing it.
    public event Action<Tip>? ProactiveTipDue;
    internal event Action<MainWindowAction>? MainWindowActionRequested;

    // Called from the dispatcher-queued fullscreen-poll tick — already on the UI thread.
    private void CheckProactiveTipDue()
    {
        if (!IsVisible || IsMascotHidden || IsBubbleOpen || IsForegroundWindowFullscreen() ||
            App.MainWindowInstance?.ViewModel.IsLoaded != true) return;

        _tipCoordinator.PeekPendingTip(_tasks);

        var tip = _tipCoordinator.TryGetProactiveTip(_tasks);
        if (tip == null) return;

        ProactiveTipDue?.Invoke(tip);
    }

    // Only explicit action and X interactions change a topic's response history.
    public void RecordProactiveTipEngagement(Tip? tip) => _tipCoordinator.RecordEngagement(tip);

    public void RecordProactiveTipDismissal(Tip tip) => _tipCoordinator.RecordDismissal(tip);

    public MascotViewModel(
        SettingsService settings,
        TipCoordinator tipCoordinator,
        DispatcherQueue dispatcher,
        IReadOnlyList<TodoItem> tasks)
    {
        _settings = settings;
        _tipCoordinator = tipCoordinator;
        _dispatcher = dispatcher;
        _tasks = tasks;
        _isVisible = _settings.Current.ShowMascot;
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
        CheckHideExpiration();
        RefreshVisibility();
        StartFullscreenPolling();
    }

    public bool LockPosition
    {
        get => _settings.Current.LockMascotPosition;
        set
        {
            if (_settings.Current.LockMascotPosition == value) return;
            _settings.Current.LockMascotPosition = value;
            _settings.SaveDebounced();
            OnPropertyChanged();
        }
    }

    public void RaiseLockPositionChanged() => OnPropertyChanged(nameof(LockPosition));

    public void BeginDrag(int windowX, int windowY)
    {
        if (_settings.Current.LockMascotPosition) return;
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
            var size = WindowSize;
            newX = Math.Clamp(newX, w.left, w.right  - size);
            newY = Math.Clamp(newY, w.top,  w.bottom - size);
        }

        X = newX;
        Y = newY;
    }

    public void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        _settings.SaveDebounced();
    }

    private void ResetPosition()
    {
        var workArea = DisplayArea.Primary.WorkArea;
        var size = WindowSize;
        X = workArea.X + workArea.Width  - size - EdgePadding;
        Y = workArea.Y + workArea.Height - size - EdgePadding;
        _settings.SaveDebounced();
    }

    private void ShowMainWindow() => MainWindowActionRequested?.Invoke(MainWindowAction.Show);

    private void ToggleMainWindow() => MainWindowActionRequested?.Invoke(MainWindowAction.Toggle);

    private void ToggleBubble()
    {
        if (App.MainWindowInstance?.HideIfVisibleFromMascot() == true) return;

        if (IsBubbleOpen)
        {
            // Bubble is open — pressing mascot again opens main window instead.
            ShowMainWindow();
        }
        else
        {
            IsBubbleOpen = true;
        }
    }
    private void InitializePosition()
    {
        if (_settings.Current.MascotX == -1 && _settings.Current.MascotY == -1)
        {
            var workArea = DisplayArea.Primary.WorkArea;
            var size = WindowSize;
            _settings.Current.MascotX = workArea.X + workArea.Width  - size - EdgePadding;
            _settings.Current.MascotY = workArea.Y + workArea.Height - size - EdgePadding;
            _settings.SaveDebounced();
        }
        else
        {
            ClampToWorkArea();
        }
    }

    private void ClampToWorkArea()
    {
        var pt = new NativeMethods.POINT { X = _settings.Current.MascotX, Y = _settings.Current.MascotY };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return;

        var w = mi.rcWork;
        var size = WindowSize;
        // Route through the property setters so PropertyChanged fires and
        // MascotWindow.AppWindow.Move() is invoked for the live window.
        X = Math.Clamp(_settings.Current.MascotX, w.left, w.right  - size);
        Y = Math.Clamp(_settings.Current.MascotY, w.top,  w.bottom - size);
    }

    private void StartFullscreenPolling()
    {
        _cts = new CancellationTokenSource();
        _pollTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        _ = PollFullscreenAsync(_cts.Token);
    }

    private async Task PollFullscreenAsync(CancellationToken ct)
    {
        try
        {
            while (await _pollTimer!.WaitForNextTickAsync(ct))
            {
                _dispatcher.TryEnqueue(() =>
                {
                    RefreshVisibility();
                    CheckProactiveTipDue();
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private static bool IsForegroundWindowFullscreen()
    {
        // Use foreground geometry for ordinary fullscreen instead of treating the
        // shell's general QUNS_BUSY state as proof that the foreground app is fullscreen.
        if (NativeMethods.SHQueryUserNotificationState(out var quns) == 0)
        {
            if (quns == NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE ||
                quns == NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN)
                return true;
        }

        // Any non-Hatch window that covers the full monitor bounds.
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == NativeMethods.GetShellWindow() ||
            hwnd == NativeMethods.GetDesktopWindow()) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if ((int)pid == Environment.ProcessId) return false;

        var hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var wr)) return false;
        var mr = mi.rcMonitor;

        // Maximized windows are positioned at -8,-8 (invisible resize border) so their
        // RECT exceeds rcMonitor on all sides — exclude them; they are not true fullscreen.
        if (NativeMethods.IsZoomed(hwnd)) return false;

        return wr.left <= mr.left && wr.top <= mr.top &&
               wr.right >= mr.right && wr.bottom >= mr.bottom;
    }

    private static TimeSpan UntilTomorrow()
    {
        var tomorrow = DateTimeOffset.Now.Date.AddDays(1);
        return tomorrow - DateTimeOffset.Now;
    }

    private void HideFor(TimeSpan duration)
    {
        var hideUntil = DateTime.UtcNow.Add(duration);
        _settings.Current.HideUntilTicks = hideUntil.Ticks;
        _settings.SaveDebounced();

        IsMascotHidden = true;

        if (IsBubbleOpen)
            CloseBubble();

        App.MainWindowInstance?.ShowMascotHiddenInTray(FormatHideDuration(duration));

        StartHideRestoreTimer();
    }

    private void HideUntilRestart()
    {
        // No expiry tick — stays hidden until the app is restarted
        _settings.Current.HideUntilTicks = long.MaxValue;
        _settings.SaveDebounced();

        IsMascotHidden = true;

        if (IsBubbleOpen)
            CloseBubble();

        App.MainWindowInstance?.ShowMascotHiddenInTray(
            "Hidden until restart. Right-click the tray icon to restore.");

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
        App.MainWindowInstance?.ShowMascotHiddenInTray();
    }

    private void RestoreFromHide()
    {
        _settings.Current.HideUntilTicks = null;
        _settings.SaveDebounced();

        IsMascotHidden = false;
        StopHideRestoreTimer();

        App.MainWindowInstance?.ShowMascotRestoredInTray();
    }

    private void CheckHideExpiration()
    {
        if (_settings.Current.HideUntilTicks == null)
        {
            IsMascotHidden = false;
            return;
        }

        // long.MaxValue means "until restart" — stay hidden, no timer needed
        if (_settings.Current.HideUntilTicks.Value == long.MaxValue)
        {
            IsMascotHidden = true;
            ApplyHiddenTrayState();
            return;
        }

        var hideUntil = new DateTime(_settings.Current.HideUntilTicks.Value, DateTimeKind.Utc);
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
        _hideRestoreTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
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
                if (_settings.Current.HideUntilTicks != null &&
                    _settings.Current.HideUntilTicks.Value != long.MaxValue)
                {
                    var hideUntil = new DateTime(_settings.Current.HideUntilTicks.Value, DateTimeKind.Utc);
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
