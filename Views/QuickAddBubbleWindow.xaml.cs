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
    private readonly TipEngine _tipEngine = new();
    private Storyboard? _fadeIn;
    private Storyboard? _fadeOut;
    private Storyboard? _tipFadeIn;
    private Storyboard? _tipFadeOut;
    private bool _isClosed = false;
    private bool _initialLayoutDone = false;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    // Fired when the user dismisses the bubble (close/Esc/action) — not on app shutdown.
    public event Action? Dismissed;
    private CancellationTokenSource? _tipDismissCts;
    private bool _tipDismissPaused = false;
    private int _tipDismissRemainingMs = 0;
    private bool _tipWasShown = false;
    private bool _tipAutoDismissCompleted = false;
    private Tip? _currentTip;
    private int _mascotX, _mascotY, _mascotWidth;
    private bool _tipOnlyMode = false;
    private const int ProactiveTipFallbackDismissMs = 8000;

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
        // Don't move yet — FitWindowToContent will call UpdatePosition after the first
        // layout pass when the window size is known. Moving here would use the WinUI 3
        // default window width and place the bubble at the wrong X position.
        if (_initialLayoutDone)
            UpdatePosition();
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
        _tipOnlyMode = false;
        _tipWasShown = false;
        _tipAutoDismissCompleted = false;
        _tipDismissCts?.Cancel();
        _tipDismissCts = null;

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
        UpdatePosition();

        // Show without stealing focus first, then bring to front
        NativeMethods.ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        NativeMethods.SetForegroundWindow(_hwnd);

        // Re-fit after layout pass so the window is never stuck at confirmation height.
        // SizeChanged alone is unreliable here because SW_HIDE can suspend layout updates.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
            () => { FitWindowToContent(); UpdatePosition(); });

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => TaskTitleBox.Focus(FocusState.Programmatic));

        ShowContextualTip();
    }

    // Shows only the contextual tip, above the mascot, without stealing focus and
    // without the quick-add form — used by the "Show tips automatically" setting.
    // Reuses ShowContextualTip() so cadence/suppression rules stay identical to the
    // click-triggered path; if no tip is available right now, nothing is shown at all.
    public void ShowProactiveTip(int mascotX, int mascotY, int mascotWidth)
    {
        _isClosed = false;
        _tipOnlyMode = true;
        _tipWasShown = false;
        _tipAutoDismissCompleted = false;
        _tipDismissCts?.Cancel();
        _tipDismissCts = null;

        QuickAddFormPanel.Visibility = Visibility.Collapsed;
        IntroMessage.Visibility = Visibility.Collapsed;
        ConfirmationOverlay.Visibility = Visibility.Collapsed;
        BubbleContent.Visibility = Visibility.Visible;
        TipBubble.Visibility = Visibility.Collapsed;
        TipBubble.Opacity = 0;

        _mascotX = mascotX;
        _mascotY = mascotY;
        _mascotWidth = mascotWidth;

        ShowContextualTip();

        if (TipBubble.Visibility != Visibility.Visible)
        {
            // No tip available right now (cooldown/suppressed) — don't show an empty window.
            _isClosed = true;
            _tipOnlyMode = false;
            return;
        }

        // Tip-only mode has no close button — unlike the click-triggered bubble, it must
        // always self-dismiss even for Critical tips that normally rely on the user
        // closing the form manually.
        if (_currentTip != null && _currentTip.DismissAfterMs <= 0)
        {
            _tipDismissRemainingMs = ProactiveTipFallbackDismissMs;
            _ = ScheduleTipDismissAsync(_tipDismissCts!.Token);
        }

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
            () => { FitWindowToContent(); UpdatePosition(); });

        NativeMethods.ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
    }

    private void UpdatePosition()
    {
        const int gap = 12;

        var pt       = new NativeMethods.POINT { X = _mascotX + _mascotWidth / 2, Y = _mascotY + _mascotWidth / 2 };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi       = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            return;

        NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        double scale  = dpiX / 96.0;
        int scaledGap = (int)Math.Round(gap * scale);

        // Use the window's current physical size (already correct after FitWindowToContent)
        int bubbleWidth  = AppWindow.Size.Width;
        int bubbleHeight = AppWindow.Size.Height;

        var w = mi.rcWork;

        // Prefer left of mascot; flip to right if it doesn't fit.
        int bubbleX = _mascotX - bubbleWidth - scaledGap;
        if (bubbleX < w.left)
            bubbleX = _mascotX + _mascotWidth + scaledGap;
        bubbleX = Math.Clamp(bubbleX, w.left, w.right - bubbleWidth);

        // Vertically centre on the mascot, clamp to work area.
        int mascotCenterY = _mascotY + _mascotWidth / 2;
        int bubbleY       = mascotCenterY - bubbleHeight / 2;
        if (bubbleY + bubbleHeight > w.bottom - scaledGap)
            bubbleY = w.bottom - bubbleHeight - scaledGap;
        bubbleY = Math.Clamp(bubbleY, w.top + scaledGap, w.bottom - bubbleHeight);

        AppWindow.Move(new PointInt32(bubbleX, bubbleY));
    }

    private void FitWindowToContent()
    {
        if (BubbleContent.Visibility != Visibility.Visible) return;
        var scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
        int physW = (int)Math.Round(340 * scale);
        int contentH = (int)Math.Round(BubbleContent.ActualHeight * scale);
        if (contentH <= 0) return;
        // Add non-client border overhead (border frame not included in client/content area)
        int ncOverhead = Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height);
        AppWindow.Resize(new SizeInt32(physW, contentH + ncOverhead));
        UpdatePosition();
        _initialLayoutDone = true;
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

        task.DueDate = (DatePresetSelector.SelectedIndex) switch
        {
            1 => new DateTimeOffset(DueDatePresets.GetToday(DateTime.Today)),
            2 => new DateTimeOffset(DueDatePresets.GetTomorrow(DateTime.Today)),
            3 => new DateTimeOffset(DueDatePresets.GetThisWeekend(DateTime.Today)),
            4 => new DateTimeOffset(DueDatePresets.GetNextWeek(DateTime.Today)),
            _ => (DateTimeOffset?)null
        };

        mainVm.Tasks.Insert(0, task);
        mainVm.AttachTaskPropertyChangedHandler(task);
        mainVm.SaveAsync();

        App.Settings.LastUsedListId = selectedListId;
        _ = App.SettingsService.SaveAsync();

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

        // Shrink to a compact confirmation size
        var scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
        int ncOverhead = Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height);
        AppWindow.Resize(new SizeInt32((int)Math.Round(340 * scale), (int)Math.Round(180 * scale) + ncOverhead));
        UpdatePosition();

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

        // Check if in cooldown period (adaptive silence)
        var today = DateTime.Today;
        if (App.Settings.TipAutoOpenCooldownUntil.HasValue &&
            today < App.Settings.TipAutoOpenCooldownUntil.Value)
        {
            // In cooldown — don't show tip
            return;
        }

        // Track user activity
        App.Settings.LastUserActivityTime = DateTime.Now;
        _ = App.SettingsService.SaveAsync();

        _currentTip = _tipEngine.GetTip(mainVm.Tasks,
                                       App.Settings.LastMeaningfulTipTime,
                                       App.Settings.LastUserActivityTime);

        // If tip is null (suppressed fallback), don't show anything
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

        // Track meaningful tips for smart fallback suppression
        if (_currentTip.IsMeaningful)
        {
            App.Settings.LastMeaningfulTipTime = DateTime.Now;
        }

        // Check if this is a new daily tip
        bool isNewDay = App.Settings.LastTipShowDate?.Date != today;
        if (isNewDay)
        {
            App.Settings.LastTipShowDate = today;
            SignalMascotDailyTip();
        }

        _ = App.SettingsService.SaveAsync();

        // Use Severity and DismissAfterMs to determine timeout
        if (_currentTip.DismissAfterMs > 0)
        {
            _tipDismissRemainingMs = _currentTip.DismissAfterMs;
            _ = ScheduleTipDismissAsync(_tipDismissCts.Token);
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

    private async Task ScheduleTipDismissAsync(CancellationToken ct)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (_tipDismissRemainingMs > 0 && !ct.IsCancellationRequested)
            {
                if (_tipDismissPaused)
                {
                    await Task.Delay(50, ct);
                    continue;
                }

                int elapsed = (int)stopwatch.ElapsedMilliseconds;
                _tipDismissRemainingMs -= elapsed;
                stopwatch.Restart();

                if (_tipDismissRemainingMs <= 0)
                    break;

                await Task.Delay(Math.Min(50, _tipDismissRemainingMs), ct);
            }

            if (ct.IsCancellationRequested) return;

            // Auto-dismiss completed — user didn't manually close = engagement
            _tipAutoDismissCompleted = true;
            ResetTipDismissalCounter();

            _tipFadeOut?.Begin();
            await Task.Delay(150, ct);
            if (ct.IsCancellationRequested) return;

            TipBubble.Visibility = Visibility.Collapsed;

            // Tip-only mode has nothing else to show once the tip is gone.
            if (_tipOnlyMode && !_isClosed)
                HideWindow();
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

    private void ResetTipDismissalCounter()
    {
        if (App.Settings.ConsecutiveTipDismissals > 0)
        {
            App.Settings.ConsecutiveTipDismissals = 0;
            _ = App.SettingsService.SaveAsync();
        }
    }

    private void RecordTipDismissal()
    {
        App.Settings.ConsecutiveTipDismissals++;
        if (App.Settings.ConsecutiveTipDismissals >= 3)
        {
            // Adaptive silence: cooldown for 3 days
            App.Settings.TipAutoOpenCooldownUntil = DateTime.Today.AddDays(3);
            App.Settings.ConsecutiveTipDismissals = 0;
        }
        _ = App.SettingsService.SaveAsync();
    }

    private void TipBubble_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _tipDismissPaused = true;
    }

    private void TipBubble_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _tipDismissPaused = false;
    }

    private void TipActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTip?.Action == null) return;

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

            case TipActionType.AddSampleTask:
                AddSampleTask(mainVm);
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

    private void AddSampleTask(MainViewModel mainVm)
    {
        var firstList = mainVm.Lists.Count > 0 ? mainVm.Lists[0] : null;
        var sampleTask = new TodoItem
        {
            Title = "Example: Click to edit task name",
            ListId = firstList?.Id ?? Guid.Empty,
            ListName = firstList?.Name
        };

        mainVm.Tasks.Insert(0, sampleTask);
        mainVm.AttachTaskPropertyChangedHandler(sampleTask);
        mainVm.SaveAsync();
    }
}
