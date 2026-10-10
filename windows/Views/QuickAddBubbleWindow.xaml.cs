using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    public QuickAddViewModel ViewModel { get; } = new();
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
    private bool _tipPointerOver;
    private bool _tipHasFocus;
    private bool _tipOptionsOpen;
    private int _tipDismissRemainingMs = 0;
    private bool _tipAutoDismissCompleted = false;
    private Tip? _currentTip;
    private readonly TaskList _defaultList = new() { Id = Guid.Empty };
    private int _mascotX, _mascotY, _mascotWidth;

    public QuickAddBubbleWindow()
    {
        InitializeComponent();
        App.TipCoordinator.QuickTipsAvailabilityChanged += OnQuickTipsAvailabilityChanged;

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
            App.TipCoordinator.QuickTipsAvailabilityChanged -= OnQuickTipsAvailabilityChanged;
        };

        // Initialize list selector
        var mainVm = GetMainViewModel();
        if (mainVm != null)
            RefreshListSelector(mainVm);

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
        NativeMethods.ShowWindow(_hwnd, SW_HIDE);
        Dismissed?.Invoke();
    }

    public void ShowAndReset(int mascotX, int mascotY, int mascotWidth)
    {
        // Reset session flags
        _isClosed = false;
        _tipAutoDismissCompleted = false;
        _tipDismissCts?.Cancel();
        _tipDismissCts = null;
        _tipCountdownCts?.Cancel();
        _tipCountdownCts?.Dispose();
        _tipCountdownCts = null;
        _tipDismissStopwatch = null;
        _tipDismissPaused = false;
        _tipPointerOver = false;
        _tipHasFocus = false;
        _tipOptionsOpen = false;

        // Reset form to initial state
        TaskTitleBox.Text = string.Empty;
        ViewModel.IsImportant = false;
        AddButton.IsEnabled = false;
        DatePresetSelector.SelectedIndex = 0;
        BubbleContent.Visibility = Visibility.Visible;
        QuickAddFormPanel.Visibility = Visibility.Visible;
        ConfirmationOverlay.Visibility = Visibility.Collapsed;
        ConfirmationOverlay.Opacity = 0;
        TipBubble.Visibility = Visibility.Collapsed;
        TipBubble.Opacity = 0;

        // Start every capture in the default Tasks list.
        var mainVm = GetMainViewModel();
        if (mainVm != null)
            RefreshListSelector(mainVm);

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

    private void RefreshListSelector(MainViewModel mainVm)
    {
        _defaultList.Name = Strings.List_Default_Name;
        ListSelector.DisplayMemberPath = nameof(TaskList.Name);
        ListSelector.Items.Clear();
        ListSelector.Items.Add(_defaultList);
        foreach (var list in mainVm.CustomLists)
            ListSelector.Items.Add(list);

        ListSelector.SelectedItem = _defaultList;
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TaskTitleBox.Text?.Trim();
        if (string.IsNullOrEmpty(title)) return;

        var mainVm = GetMainViewModel();
        if (mainVm == null || !mainVm.IsLoaded) return;

        // Guard against double-submit (e.g. Enter key + button click race)
        AddButton.IsEnabled = false;

        var selectedListId = (ListSelector.SelectedItem as TaskList)?.Id ?? Guid.Empty;
        var selectedList = selectedListId == Guid.Empty
            ? _defaultList
            : mainVm.CustomLists.FirstOrDefault(list => list.Id == selectedListId) ?? _defaultList;
        ListSelector.SelectedItem = selectedList;

        ViewModel.AddTask(mainVm, title, selectedList, DatePresetSelector.SelectedIndex);

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
        var selectedList = ListSelector.SelectedItem as TaskList ?? _defaultList;
        ConfirmationText.Text = string.Format(Strings.Get("QuickAdd_ConfirmAddedTo"), selectedList.Name);

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
    }

    private void ShowContextualTip()
    {
        var mainVm = GetMainViewModel();
        if (mainVm == null || !mainVm.IsLoaded) return;

        _currentTip = App.TipCoordinator.TryGetContextualTip(mainVm.Tasks);

        // Null = cooldown or suppressed fallback — show nothing
        if (_currentTip == null)
        {
            return;
        }

        var categoryLabel = Strings.Get(_currentTip.CategoryLabelKey);
        TipCategoryIcon.Glyph = _currentTip.CategoryGlyph;
        AutomationProperties.SetName(TipCategoryIcon, categoryLabel);
        ToolTipService.SetToolTip(TipCategoryIcon, categoryLabel);
        TipCategoryHeading.Text = categoryLabel;
        var categoryVisibility = !string.IsNullOrWhiteSpace(categoryLabel)
            ? Visibility.Visible
            : Visibility.Collapsed;
        TipCategoryIcon.Visibility = categoryVisibility;
        TipCategoryHeading.Visibility = categoryVisibility;
        TipTextBlock.Text = _currentTip.Message;
        TipTextBlock.TextAlignment = TextAlignment.Left;

        // Show action button if available
        if (_currentTip.Action != null)
        {
            TipActionButton.Content = _currentTip.Action.Label;
            TipActionButton.Visibility = Visibility.Visible;
            TipActionContainer.Visibility = Visibility.Visible;
        }
        else
        {
            TipActionButton.Visibility = Visibility.Collapsed;
            TipActionContainer.Visibility = Visibility.Collapsed;
        }

        TipBubble.Visibility = Visibility.Visible;
        App.TipCoordinator.RecordShown(_currentTip, automatic: false);
        if (_currentTip.Topic == "my-day-complete") MascotSoundPlayer.Play();
        TipBubble.Opacity = 0;
        _tipDismissPaused = false;
        _tipPointerOver = false;
        _tipHasFocus = false;
        _tipOptionsOpen = false;
        _tipAutoDismissCompleted = false;
        UpdateTipChrome();

        _tipDismissCts?.Cancel();
        _tipDismissCts = new CancellationTokenSource();

        _tipFadeIn?.Begin();

        // Use Severity and DismissAfterMs to determine timeout
        if (_currentTip.DismissAfterMs > 0)
        {
            _tipDismissRemainingMs = _currentTip.DismissAfterMs;
            StartTipCountdown();
        }
    }

    private void TipCloseButton_Click(object sender, RoutedEventArgs e)
    {
        var tip = _currentTip;
        if (tip == null) return;

        HideCurrentTip();

        App.TipCoordinator.RecordDismissal(tip);
    }

    private void TipMenuHideToday_Click(object sender, RoutedEventArgs e)
    {
        App.TipCoordinator.PauseForToday();
    }

    private void TipMenuTurnOff_Click(object sender, RoutedEventArgs e)
    {
        App.TipCoordinator.SetQuickTipsEnabled(false);
    }

    private void OnQuickTipsAvailabilityChanged(bool available)
    {
        if (!available && _currentTip != null) HideCurrentTip();
    }

    private void HideCurrentTip()
    {
        _tipDismissCts?.Cancel();
        _tipCountdownCts?.Cancel();
        _tipDismissPaused = false;
        _tipPointerOver = false;
        _tipHasFocus = false;
        _tipOptionsOpen = false;
        _tipDismissStopwatch = null;
        _tipAutoDismissCompleted = true;
        UpdateTipChrome();
        TipBubble.Visibility = Visibility.Collapsed;
        _currentTip = null;
    }

    // Keeps the tip visible while the pointer or its options menu is active.
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

            _tipAutoDismissCompleted = true;

            _tipFadeOut?.Begin();
            await Task.Delay(150, ct);
            if (ct.IsCancellationRequested) return;

            TipBubble.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
    }

    private void RecordTipEngagement() => App.TipCoordinator.RecordEngagement(_currentTip);

    private void TipBubble_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _tipPointerOver = true;
        UpdateTipChrome();
        PauseTipCountdown();
    }

    private void TipBubble_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _tipPointerOver = false;
        UpdateTipChrome();
        ResumeTipCountdown();
    }

    private void TipBubble_GotFocus(object sender, RoutedEventArgs e)
    {
        _tipHasFocus = true;
        UpdateTipChrome();
    }

    private void TipBubble_LostFocus(object sender, RoutedEventArgs e)
    {
        _tipHasFocus = false;
        UpdateTipChrome();
    }

    private void TipOptionsFlyout_Opened(object sender, object args)
    {
        _tipOptionsOpen = true;
        UpdateTipChrome();
        PauseTipCountdown();
    }

    private void TipOptionsFlyout_Closed(object sender, object args)
    {
        _tipOptionsOpen = false;
        UpdateTipChrome();
        ResumeTipCountdown();
    }

    private void UpdateTipChrome()
    {
        var opacity = _tipPointerOver || _tipHasFocus || _tipOptionsOpen ? 1 : 0;
        TipOptionsButton.Opacity = opacity;
        TipCloseButton.Opacity = opacity;
    }

    private void PauseTipCountdown()
    {
        if (_tipDismissPaused || _tipDismissStopwatch == null || _tipAutoDismissCompleted) return;
        _tipDismissPaused = true;
        _tipDismissRemainingMs = Math.Max(0, _tipDismissRemainingMs - (int)_tipDismissStopwatch.ElapsedMilliseconds);
        _tipDismissStopwatch = null;
        _tipCountdownCts?.Cancel();
    }

    private void ResumeTipCountdown()
    {
        if (!_tipDismissPaused || _tipPointerOver || _tipOptionsOpen ||
            _tipAutoDismissCompleted || _currentTip is null || _currentTip.DismissAfterMs <= 0) return;

        _tipDismissPaused = false;
        _tipDismissRemainingMs = Math.Max(1, _tipDismissRemainingMs);
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
            RecordTipEngagement();
            _tipAutoDismissCompleted = true;
            TaskTitleBox.Focus(FocusState.Programmatic);
            return;
        }

        var action = _currentTip.Action;
        RecordTipEngagement();
        HideWindow();
        ExecuteTipAction(action);
    }

    private void ExecuteTipAction(TipAction action)
    {
        var mainVm = GetMainViewModel();
        var mainWindow = App.MainWindowInstance;
        if (mainVm == null || mainWindow == null) return;

        switch (action.Type)
        {
            case TipActionType.ViewOverdue:
                mainWindow.ShowFromMascot();
                mainWindow.NavigateTo("planned");
                break;

            case TipActionType.ViewMyDay:
                mainWindow.ShowFromMascot();
                mainWindow.NavigateTo("myday");
                break;

            case TipActionType.ViewPlanned:
                mainWindow.ShowFromMascot();
                mainWindow.NavigateTo("planned");
                break;

            case TipActionType.AddSampleTask:
                mainVm.AddSampleTask();
                mainWindow.ShowFromMascot();
                break;

            case TipActionType.OpenMainWindow:
                mainWindow.ShowFromMascot();
                break;

            case TipActionType.OpenTaskDetails:
                if (action.TaskId is Guid taskId)
                    mainWindow.ShowAndSelectTask(taskId);
                else
                    mainWindow.ShowFromMascot();
                break;

            case TipActionType.None:
            default:
                break;
        }
    }
}
