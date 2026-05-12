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

namespace Hatch.Views;

public sealed partial class QuickAddBubbleWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly TipEngine _tipEngine = new();
    private Storyboard? _fadeIn;
    private Storyboard? _fadeOut;
    private Storyboard? _tipFadeIn;
    private bool _isClosed = false;

    public QuickAddBubbleWindow()
    {
        InitializeComponent();

        _fadeIn  = (Storyboard)BubbleRoot.Resources["ConfirmationFadeIn"];
        _fadeOut = (Storyboard)BubbleRoot.Resources["ConfirmationFadeOut"];
        _tipFadeIn = (Storyboard)BubbleRoot.Resources["TipFadeIn"];
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

        // Size — extra height to accommodate the CalendarDatePicker when shown
        AppWindow.Resize(new Windows.Graphics.SizeInt32(340, 320));

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

        // Evaluate and display contextual tip
        EvaluateAndShowTip();

        AddButton.Click += AddButton_Click;
        OpenMainWindowButton.Click += (_, _) =>
        {
            Close();
            App.MascotWindowInstance?.ViewModel.ToggleMainWindowCommand.Execute(null);
        };
        CloseButton.Click += (_, _) => Close();
        Closed += (_, _) => { _isClosed = true; OnWindowClosed(); };

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
        // Logical (96 DPI) sizes — same values used in AppWindow.Resize
        const int logicalWidth  = 340;
        const int logicalHeight = 320;
        const int gap           = 12;

        var pt       = new NativeMethods.POINT { X = mascotX + mascotWidth / 2, Y = mascotY + mascotWidth / 2 };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi       = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            return;

        // Scale logical sizes to physical pixels for this monitor's DPI
        NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        double scale      = dpiX / 96.0;
        int bubbleWidth   = (int)Math.Round(logicalWidth  * scale);
        int bubbleHeight  = (int)Math.Round(logicalHeight * scale);
        int scaledGap     = (int)Math.Round(gap * scale);

        var w = mi.rcWork;

        // --- Horizontal axis ---
        // Prefer left of mascot; flip to right if it doesn't fit.
        int bubbleX = mascotX - bubbleWidth - scaledGap;
        if (bubbleX < w.left)
            bubbleX = mascotX + mascotWidth + scaledGap;

        // Clamp to work area (handles ultrawide or very wide bubbles)
        bubbleX = Math.Clamp(bubbleX, w.left, w.right - bubbleWidth);

        // --- Vertical axis ---
        // Default: vertically centre on the mascot
        int mascotCenterY = mascotY + mascotWidth / 2;
        int bubbleY       = mascotCenterY - bubbleHeight / 2;

        // If near bottom edge (system tray corner), shift upward before clamping
        if (bubbleY + bubbleHeight > w.bottom - scaledGap)
            bubbleY = w.bottom - bubbleHeight - scaledGap;

        bubbleY = Math.Clamp(bubbleY, w.top + scaledGap, w.bottom - bubbleHeight);

        AppWindow.Move(new PointInt32(bubbleX, bubbleY));
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
    }

    private void EvaluateAndShowTip()
    {
        var mainVm = GetMainViewModel();
        if (mainVm == null) return;

        var tip = _tipEngine.GetTip(mainVm.Tasks);
        TipTextBlock.Text = tip;

        var today = DateTime.Today;
        var isNewDay = App.Settings.LastTipShowDate?.Date != today;

        if (isNewDay)
        {
            TipBubble.Visibility = Visibility.Visible;
            _tipFadeIn?.Begin();
            App.Settings.LastTipShowDate = today;
            _ = App.SettingsService.SaveAsync();
        }
        else
        {
            TipBubble.Visibility = Visibility.Collapsed;
        }
    }
}
