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
    private CancellationTokenSource? _tipDismissCts;
    private bool _tipDismissPaused = false;
    private int _tipDismissRemainingMs = 0;
    private bool _tipWasShown = false;
    private bool _tipAutoDissmissCompleted = false;
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

        // Resize dynamically to content after each layout pass
        BubbleContent.SizeChanged += (_, _) => FitWindowToContent();

        // Initialize list selector
        var mainVm = GetMainViewModel();
        if (mainVm != null)
        {
            ListSelector.ItemsSource = mainVm.Lists;
            ListSelector.DisplayMemberPath = nameof(TaskList.Name);

            // Pre-select the last-used list if it still exists, otherwise fall back to first
            var lastUsedIndex = mainVm.Lists.IndexOf(
                mainVm.Lists.FirstOrDefault(l => l.Id == App.Settings.LastUsedListId)!);
            ListSelector.SelectedIndex = lastUsedIndex >= 0 ? lastUsedIndex
                                       : mainVm.Lists.Count > 0 ? 0 : -1;
        }

        // Show first-run intro if needed
        if (!App.Settings.FirstRunComplete)
        {
            IntroMessage.Visibility = Visibility.Visible;
            var gotItButton = IntroMessage.Children[2] as Button;
            if (gotItButton != null)
            {
                gotItButton.Click += async (_, _) =>
                {
                    IntroMessage.Visibility = Visibility.Collapsed;
                    App.Settings.FirstRunComplete = true;
                    await App.SettingsService.SaveAsync();
                    TaskTitleBox.Focus(FocusState.Programmatic);
                };
            }
        }

        // Show contextual tip when bubble opens
        ShowContextualTip();

        AddButton.Click += AddButton_Click;
        OpenMainWindowButton.Click += (_, _) =>
        {
            Close();
            App.MascotWindowInstance?.ViewModel.ToggleMainWindowCommand.Execute(null);
        };
        CloseButton.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            _isClosed = true;
            OnWindowClosed();
            App.MascotWindowInstance?.ViewModel.HideDailyTipIndicator();
        };

        // Disable Add when title is empty
        TaskTitleBox.TextChanged += (_, _) =>
            AddButton.IsEnabled = !string.IsNullOrWhiteSpace(TaskTitleBox.Text);
        AddButton.IsEnabled = false;

        // Handle keyboard interactions
        TaskTitleBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                Close();
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
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => TaskTitleBox.Focus(FocusState.Programmatic));
        }
        this.Activated += OnFirstActivated;
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

    private void DatePresetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    public void PositionRelativeToMascot(int mascotX, int mascotY, int mascotWidth)
    {
        _mascotX = mascotX;
        _mascotY = mascotY;
        _mascotWidth = mascotWidth;
        UpdatePosition();
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
            ListId = selectedListId
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
        ConfirmationText.Text = selectedList != null ? $"Added to \"{selectedList.Name}\"" : string.Empty;

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

        var tcs = new TaskCompletionSource<bool>();
        void OnCompleted(object? _, object __) => tcs.TrySetResult(true);
        _fadeOut!.Completed += OnCompleted;
        _fadeOut.Begin();
        await tcs.Task;
        _fadeOut.Completed -= OnCompleted;

        if (!_isClosed)
            Close();
    }

    private void OnWindowClosed()
    {
        _tipDismissCts?.Cancel();

        // If tip was shown but auto-dismiss didn't complete, user closed early = dismissal
        if (_tipWasShown && !_tipAutoDissmissCompleted)
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
        _tipAutoDissmissCompleted = false;

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
            _tipAutoDissmissCompleted = true;
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
        if (!_isClosed && _tipWasShown && !_tipAutoDissmissCompleted)
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

        Close();
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
                mainVm.ActiveNavItem = "planned";
                mainWindow.Activate();
                break;

            case TipActionType.ViewMyDay:
                mainVm.ActiveNavItem = "myday";
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
        var sampleTask = new TodoItem
        {
            Title = "Example: Click to edit task name",
            ListId = mainVm.Lists.Count > 0 ? mainVm.Lists[0].Id : Guid.Empty
        };

        mainVm.Tasks.Insert(0, sampleTask);
        mainVm.AttachTaskPropertyChangedHandler(sampleTask);
        mainVm.SaveAsync();
    }
}
