using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Hatch.Models;
using Hatch.ViewModels;
using Hatch.Helpers;
using Hatch.Services;
using Windows.Graphics;
using Microsoft.UI.Xaml.Input;

namespace Hatch.Views;

public sealed partial class QuickAddBubbleWindow : Window
{
    private readonly IntPtr _hwnd;
    private Storyboard? _fadeIn;
    private Storyboard? _fadeOut;
    private Storyboard? _tipFadeIn;
    private Storyboard? _tipFadeOut;
    private bool _isClosed = false;
    private bool _positionKnown;
    private bool _fitting;
    private XamlRoot? _bubbleXamlRoot;
    private double _lastRasterizationScale;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    // Fired when the user dismisses the bubble (close/Esc/action) — not on app shutdown.
    public event Action? Dismissed;
    private CancellationTokenSource? _tipDismissCts;
    // A separate, per-segment token linked to _tipDismissCts — cancelling this alone
    // (on hover-pause) stops just the current countdown wait without tearing down the
    // whole tip lifecycle; cancelling _tipDismissCts cancels both.
    private CancellationTokenSource? _tipCountdownCts;
    private System.Diagnostics.Stopwatch? _tipDismissStopwatch;
    private bool _tipDismissPaused = false;
    private int _tipDismissRemainingMs = 0;
    private bool _tipWasShown = false;
    private bool _tipAutoDismissCompleted = false;
    private Tip? _currentTip;
    private int _mascotX, _mascotY, _mascotWidth;

    public QuickAddBubbleWindow()
    {
        InitializeComponent();

        _fadeIn  = (Storyboard)BubbleRoot.Resources["ConfirmationFadeIn"];
        _fadeOut = (Storyboard)BubbleRoot.Resources["ConfirmationFadeOut"];
        _tipFadeIn = (Storyboard)BubbleRoot.Resources["TipFadeIn"];
        _tipFadeOut = (Storyboard)BubbleRoot.Resources["TipFadeOut"];
        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

        // Mirror the theme from the main window so this separate window
        // always respects the user's Light/Dark/System setting.
        ApplyCurrentTheme();

        // Borderless, compact bubble window
        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        // Keep off-screen until the first layout pass so the window never appears
        // at the WinUI 3 default size before FitWindowToContent corrects it.
        AppWindow.Move(new PointInt32(-32000, -32000));

        // Resize dynamically to content after each layout pass
        BubbleContent.SizeChanged += (_, _) => FitWindowToContent();
        BubbleRoot.Loaded += (_, _) =>
        {
            if (_bubbleXamlRoot == null)
            {
                _bubbleXamlRoot = BubbleRoot.XamlRoot;
                _lastRasterizationScale = _bubbleXamlRoot.RasterizationScale;
                _bubbleXamlRoot.Changed += OnBubbleXamlRootChanged;
            }
            FitWindowToContent();
        };
        Closed += (_, _) =>
        {
            if (_bubbleXamlRoot != null) _bubbleXamlRoot.Changed -= OnBubbleXamlRootChanged;
        };

        // Initialize list selector
        var mainVm = GetMainViewModel();
        if (mainVm != null)
        {
            ListSelector.ItemsSource = mainVm.CustomLists;
            ListSelector.DisplayMemberPath = nameof(TaskList.Name);

            // Pre-select the last-used list if it still exists, otherwise fall back to first
            var lastUsedIndex = mainVm.CustomLists.IndexOf(
                mainVm.CustomLists.FirstOrDefault(l => l.Id == App.Settings.LastUsedListId)!);
            ListSelector.SelectedIndex = lastUsedIndex >= 0 ? lastUsedIndex
                                       : mainVm.CustomLists.Count > 0 ? 0 : -1;
        }

        // Show first-run intro if needed
        if (!App.Settings.FirstRunComplete)
        {
            IntroMessage.Visibility = Visibility.Visible;
            GotItButton.Click += async (_, _) =>
            {
                IntroMessage.Visibility = Visibility.Collapsed;
                App.Settings.FirstRunComplete = true;
                await App.SettingsService.SaveAsync();
                TaskTitleBox.Focus(FocusState.Programmatic);
            };
        }

        // Show contextual tip when bubble opens
        ShowContextualTip();

        AddButton.Click += AddButton_Click;
        OpenMainWindowButton.Click += (_, _) =>
        {
            HideWindow();
            App.MascotWindowInstance?.ViewModel.ToggleMainWindowCommand.Execute(null);
        };
        CloseButton.Click += (_, _) => HideWindow();

        // Disable Add when title is empty
        TaskTitleBox.TextChanged += (_, _) =>
            AddButton.IsEnabled = !string.IsNullOrWhiteSpace(TaskTitleBox.Text);
        AddButton.IsEnabled = false;

        // Handle keyboard interactions
        TaskTitleBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                HideWindow();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (AddButton.IsEnabled)
                    AddButton_Click(AddButton, null!);
                e.Handled = true;
            }
        };

        // Focus title input after the window is fully shown.
        // We use a one-shot handler + DispatcherQueue defer so XAML layout is
        // guaranteed to be complete before Focus() is called. Without the defer,
        // Focus() can silently fail and the user's first keystroke disappears.
        void OnFirstActivated(object _, WindowActivatedEventArgs __)
        {
            this.Activated -= OnFirstActivated;
            SystemBackdrop = OsVersionHelper.CreateMicaOrFallbackBackdrop();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => TaskTitleBox.Focus(FocusState.Programmatic));
        }
        this.Activated += OnFirstActivated;

        this.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated
                && !_isClosed
                && !IsCursorOverMascot())
            {
                HideWindow();
            }
        };
    }

    /// <summary>
    /// Mirrors the theme from the main window's RootFrame into this window's
    /// root element so Light/Dark/System settings are respected end-to-end.
    /// Call this whenever the app theme changes.
    /// </summary>
    public void ApplyCurrentTheme()
    {
        if (App.MainWindowInstance?.Content is not FrameworkElement mainRoot) return;
        BubbleRoot.RequestedTheme = mainRoot.ActualTheme switch
        {
            ElementTheme.Light => ElementTheme.Light,
            ElementTheme.Dark  => ElementTheme.Dark,
            _                  => ElementTheme.Default
        };
    }

    public void PositionRelativeToMascot(int mascotX, int mascotY, int mascotWidth)
    {
        _mascotX = mascotX;
        _mascotY = mascotY;
        _mascotWidth = mascotWidth;
        _positionKnown = true;
        FitWindowToContent();
    }

    public void HideWindow()
    {
        _isClosed = true;
        _tipDismissCts?.Cancel();
        OnWindowClosed();
        App.MascotWindowInstance?.ViewModel.HideDailyTipIndicator();
        NativeMethods.ShowWindow(_hwnd, SW_HIDE);
        Dismissed?.Invoke();
    }

    public void ShowAndReset(int mascotX, int mascotY, int mascotWidth)
    {
        // Reset session flags
        _isClosed = false;
        _tipWasShown = false;
        _tipAutoDismissCompleted = false;
        _tipDismissCts?.Cancel();
        _tipDismissCts = null;
        _tipCountdownCts?.Cancel();
        _tipCountdownCts?.Dispose();
        _tipCountdownCts = null;
        _tipDismissStopwatch = null;
        _tipDismissPaused = false;

        // Reset form to initial state
        TaskTitleBox.Text = string.Empty;
        AddButton.IsEnabled = false;
        DatePresetSelector.SelectedIndex = 0;
        BubbleContent.Visibility = Visibility.Visible;
        QuickAddFormPanel.Visibility = Visibility.Visible;
        ConfirmationOverlay.Visibility = Visibility.Collapsed;
        ConfirmationOverlay.Opacity = 0;
        TipBubble.Visibility = Visibility.Collapsed;
        TipBubble.Opacity = 0;

        // Re-sync list selector to last-used list
        var mainVm = GetMainViewModel();
        if (mainVm != null)
        {
            var lastUsedIndex = mainVm.CustomLists.IndexOf(
                mainVm.CustomLists.FirstOrDefault(l => l.Id == App.Settings.LastUsedListId)!);
            ListSelector.SelectedIndex = lastUsedIndex >= 0 ? lastUsedIndex
                                       : mainVm.CustomLists.Count > 0 ? 0 : -1;
        }

        // Reposition relative to (possibly moved) mascot
        _mascotX = mascotX;
        _mascotY = mascotY;
        _mascotWidth = mascotWidth;
        _positionKnown = true;
        FitWindowToContent();

        // Show without stealing focus first, then bring to front
        NativeMethods.ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        NativeMethods.SetForegroundWindow(_hwnd);

        // Re-fit after layout pass so the window is never stuck at confirmation height.
        // SizeChanged alone is unreliable here because SW_HIDE can suspend layout updates.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
            FitWindowToContent);

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => TaskTitleBox.Focus(FocusState.Programmatic));

        ShowContextualTip();
    }

    private void OnBubbleXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (sender.RasterizationScale == _lastRasterizationScale) return;
        _lastRasterizationScale = sender.RasterizationScale;
        FitWindowToContent();
    }

    private void FitWindowToContent()
    {
        if (_fitting || !_positionKnown || BubbleRoot.XamlRoot == null) return;
        var pt       = new NativeMethods.POINT { X = _mascotX + _mascotWidth / 2, Y = _mascotY + _mascotWidth / 2 };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi       = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            return;

        NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        double scale = dpiX > 0 ? dpiX / 96.0 : BubbleRoot.XamlRoot.RasterizationScale;
        int gap = (int)Math.Ceiling(12 * scale);
        var work = System.Drawing.Rectangle.FromLTRB(mi.rcWork.left, mi.rcWork.top, mi.rcWork.right, mi.rcWork.bottom);
        var mascot = new System.Drawing.Rectangle(_mascotX, _mascotY, _mascotWidth, _mascotWidth);
        _fitting = true;
        try
        {
            int borderW = Math.Max(0, AppWindow.Size.Width - AppWindow.ClientSize.Width);
            int borderH = Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height);
            int width = Math.Max(1, Math.Min((int)Math.Ceiling(340 * scale) + borderW, work.Width - 2 * gap));
            var content = BubbleContent.Visibility == Visibility.Visible ? BubbleContent : ConfirmationOverlay;
            content.Measure(new Windows.Foundation.Size(Math.Max(1, (width - borderW) / scale), double.PositiveInfinity));
            int height = (int)Math.Ceiling(content.DesiredSize.Height * scale) + borderH;
            var placement = MascotPopupPlacement.Place(work, mascot, new System.Drawing.Size(width, height), gap);
            BubbleViewport.Height = Math.Max(1, (placement.Height - borderH) / scale);
            if (AppWindow.Size.Width != placement.Width || AppWindow.Size.Height != placement.Height ||
                AppWindow.Position.X != placement.X || AppWindow.Position.Y != placement.Y)
                AppWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, placement.Width, placement.Height));
        }
        finally { _fitting = false; }
    }

    private MainViewModel? GetMainViewModel()
    {
        return (App.MainWindowInstance as MainWindow)?.ViewModel;
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TaskTitleBox.Text?.Trim();
        if (string.IsNullOrEmpty(title)) return;

        var mainVm = GetMainViewModel();
        if (mainVm == null) return;

        // Guard against double-submit (e.g. Enter key + button click race)
        AddButton.IsEnabled = false;

        // Prefer the selected list; fall back to last-used only if it still exists,
        // then to the first available list.
        var selectedList = ListSelector.SelectedItem as TaskList;
        var selectedListId = selectedList?.Id ?? Guid.Empty;
        if (selectedListId == Guid.Empty)
        {
            var lastUsed = mainVm.Lists.FirstOrDefault(l => l.Id == App.Settings.LastUsedListId);
            selectedListId = lastUsed?.Id ?? mainVm.Lists.FirstOrDefault()?.Id ?? Guid.Empty;
        }

        var task = new TodoItem
        {
            Title = title,
            ListId = selectedListId,
            ListName = selectedList?.Name ?? mainVm.Lists.FirstOrDefault(l => l.Id == selectedListId)?.Name
        };

        // TimeSpan.Zero: due dates are calendar days stored at midnight +00:00 — the
        // offsetless ctor stamped the machine's own offset, a second spelling every
        // reader then had to survive.
        task.DueDate = (DatePresetSelector.SelectedIndex) switch
        {
            1 => new DateTimeOffset(DueDatePresets.GetToday(DateTime.Today), TimeSpan.Zero),
            2 => new DateTimeOffset(DueDatePresets.GetTomorrow(DateTime.Today), TimeSpan.Zero),
            3 => new DateTimeOffset(DueDatePresets.GetThisWeekend(DateTime.Today), TimeSpan.Zero),
            4 => new DateTimeOffset(DueDatePresets.GetNextWeek(DateTime.Today), TimeSpan.Zero),
            _ => (DateTimeOffset?)null
        };

        mainVm.Tasks.Insert(0, task);
        mainVm.AttachTaskPropertyChangedHandler(task);
        mainVm.SaveAsync();

        App.Settings.LastUsedListId = selectedListId;
        App.SettingsService.SaveDebounced();

        // Trigger mascot wiggle on first add in this session
        TriggerMascotWiggle();

        // Show confirmation state
        await ShowConfirmationAsync();
    }

    private void TriggerMascotWiggle()
    {
        App.MascotWindowInstance?.PlayWiggleAnimation();
    }

    private async Task ShowConfirmationAsync()
    {
        var selectedList = ListSelector.SelectedItem as TaskList;
        ConfirmationText.Text = selectedList != null
            ? string.Format(Strings.Get("QuickAdd_ConfirmAddedTo"), selectedList.Name)
            : string.Empty;

        ConfirmationOverlay.Opacity = 0;
        BubbleContent.Visibility = Visibility.Collapsed;
        ConfirmationOverlay.Visibility = Visibility.Visible;

        FitWindowToContent();

        _fadeIn?.Begin();

        await Task.Delay(800);

        if (_isClosed) return;

        if (_fadeOut == null) { HideWindow(); return; }
        var tcs = new TaskCompletionSource<bool>();
        void OnCompleted(object? _, object __) => tcs.TrySetResult(true);
        _fadeOut.Completed += OnCompleted;
        _fadeOut.Begin();
        await tcs.Task;
        _fadeOut.Completed -= OnCompleted;

        if (!_isClosed)
            HideWindow();
    }

    private bool IsCursorOverMascot()
    {
        if (App.MascotWindowInstance == null) return false;
        NativeMethods.GetCursorPos(out var pt);
        var mascotHwnd = Win32Interop.GetWindowFromWindowId(App.MascotWindowInstance.AppWindow.Id);
        NativeMethods.GetWindowRect(mascotHwnd, out var rect);
        return pt.X >= rect.left && pt.X <= rect.right &&
               pt.Y >= rect.top  && pt.Y <= rect.bottom;
    }

    private void OnWindowClosed()
    {
        _tipDismissCts?.Cancel();

        // If tip was shown but auto-dismiss didn't complete, user closed early = dismissal
        if (_tipWasShown && !_tipAutoDismissCompleted)
        {
            RecordTipDismissal();
        }
    }

    private void ShowContextualTip()
    {
        var mainVm = GetMainViewModel();
        if (mainVm == null) return;

        _currentTip = App.TipCoordinator.TryGetContextualTip(mainVm.Tasks, out var isNewDailyTip);

        // Null = cooldown or suppressed fallback — show nothing
        if (_currentTip == null)
        {
            return;
        }

        TipTextBlock.Text = _currentTip.Message;

        // Show action button if available
        if (_currentTip.Action != null)
        {
            TipActionButton.Content = _currentTip.Action.Label;
            TipActionButton.Visibility = Visibility.Visible;
        }
        else
        {
            TipActionButton.Visibility = Visibility.Collapsed;
        }

        TipBubble.Visibility = Visibility.Visible;
        TipBubble.Opacity = 0;
        _tipDismissPaused = false;
        _tipWasShown = true;
        _tipAutoDismissCompleted = false;

        _tipDismissCts?.Cancel();
        _tipDismissCts = new CancellationTokenSource();

        _tipFadeIn?.Begin();

        if (isNewDailyTip)
            SignalMascotDailyTip();

        // Use Severity and DismissAfterMs to determine timeout
        if (_currentTip.DismissAfterMs > 0)
        {
            _tipDismissRemainingMs = _currentTip.DismissAfterMs;
            StartTipCountdown();
        }
        else if (_currentTip.Severity == TipSeverity.Critical)
        {
            // Critical tips: if shown without manual dismissal, engagement
            _ = TrackEngagementOnCloseAsync();
        }
    }

    private void SignalMascotDailyTip()
    {
        App.MascotWindowInstance?.ViewModel.SetDailyTipIndicatorVisible();
    }

    // Starts (or resumes, after a hover-pause) a single wait for _tipDismissRemainingMs —
    // no polling. TipBubble_PointerEntered cancels _tipCountdownCts to pause; the resume
    // in TipBubble_PointerExited calls this again with whatever time was left.
    private void StartTipCountdown()
    {
        _tipCountdownCts?.Cancel();
        _tipCountdownCts?.Dispose();
        _tipCountdownCts = _tipDismissCts == null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(_tipDismissCts.Token);
        _tipDismissStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _ = RunTipCountdownAsync(_tipDismissRemainingMs, _tipCountdownCts.Token);
    }

    private async Task RunTipCountdownAsync(int delayMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delayMs, ct);

            // Auto-dismiss completed — user didn't manually close = engagement
            _tipAutoDismissCompleted = true;
            ResetTipDismissalCounter();

            _tipFadeOut?.Begin();
            await Task.Delay(150, ct);
            if (ct.IsCancellationRequested) return;

            TipBubble.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
    }

    private async Task TrackEngagementOnCloseAsync()
    {
        // High-priority tips: wait for bubble close to track engagement
        await Task.Delay(10); // Minimal delay to avoid race with close event
        if (!_isClosed && _tipWasShown && !_tipAutoDismissCompleted)
        {
            // Bubble is still open and tip wasn't auto-dismissed = engagement
            ResetTipDismissalCounter();
        }
    }

    private void ResetTipDismissalCounter() => App.TipCoordinator.RecordEngagement();

    private void RecordTipDismissal() => App.TipCoordinator.RecordDismissal();

    private void TipBubble_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_tipDismissPaused || _tipDismissStopwatch == null) return;
        _tipDismissPaused = true;

        _tipDismissRemainingMs -= (int)_tipDismissStopwatch.ElapsedMilliseconds;
        if (_tipDismissRemainingMs < 0) _tipDismissRemainingMs = 0;
        _tipCountdownCts?.Cancel();
    }

    private void TipBubble_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_tipDismissPaused) return;
        _tipDismissPaused = false;

        if (_tipDismissRemainingMs > 0)
            StartTipCountdown();
    }

    private void TipActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTip?.Action == null) return;

        // "Write it down" is the one action whose destination is this window — closing it
        // would throw away the very box the user just asked for. Dismiss the tip, keep
        // the bubble, and put the caret in the input.
        if (_currentTip.Action.Type == TipActionType.CaptureTask)
        {
            _tipDismissCts?.Cancel();
            TipBubble.Visibility = Visibility.Collapsed;
            ResetTipDismissalCounter();
            _tipAutoDismissCompleted = true;
            TaskTitleBox.Focus(FocusState.Programmatic);
            return;
        }

        HideWindow();
        ExecuteTipAction(_currentTip.Action.Type);
    }

    private void ExecuteTipAction(TipActionType actionType)
    {
        var mainVm = GetMainViewModel();
        var mainWindow = App.MainWindowInstance;
        if (mainVm == null || mainWindow == null) return;

        switch (actionType)
        {
            case TipActionType.ViewOverdue:
                mainWindow.NavigateTo("planned");
                mainWindow.Activate();
                break;

            case TipActionType.ViewMyDay:
                mainWindow.NavigateTo("myday");
                mainWindow.Activate();
                break;

            case TipActionType.ViewPlanned:
                mainWindow.NavigateTo("planned");
                mainWindow.Activate();
                break;

            case TipActionType.AddSampleTask:
                mainVm.AddSampleTask();
                mainWindow.Activate();
                break;

            case TipActionType.OpenMainWindow:
                mainWindow.Activate();
                break;

            case TipActionType.None:
            default:
                break;
        }
    }
}
