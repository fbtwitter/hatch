using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Hatch.Models;
using Hatch.ViewModels;
using Windows.Graphics;

namespace Hatch.Views;

public sealed partial class QuickAddBubbleWindow : Window
{
    private readonly IntPtr _hwnd;

    public QuickAddBubbleWindow()
    {
        InitializeComponent();

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

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

        AddButton.Click += AddButton_Click;
        OpenMainWindowButton.Click += (_, _) =>
        {
            Close();
            App.MascotWindowInstance?.ViewModel.ToggleMainWindowCommand.Execute(null);
        };
        CloseButton.Click += (_, _) => Close();
        Closed += (_, _) => OnWindowClosed();

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

    private void DatePresetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomDatePicker is null) return; // Not yet initialized during InitializeComponent

        var isCustom = DatePresetSelector.SelectedIndex == 3;
        CustomDatePicker.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        // Expand window when the calendar picker row appears
        AppWindow.Resize(new Windows.Graphics.SizeInt32(340, isCustom ? 370 : 320));
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
            1 => DateTimeOffset.Now,                  // Today
            2 => DateTimeOffset.Now.AddDays(1),       // Tomorrow
            3 => CustomDatePicker.Date,               // Pick a date
            _ => null                                 // No date
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
        // Set subtitle to the list name that received the task
        var selectedList = ListSelector.SelectedItem as TaskList;
        ConfirmationText.Text = selectedList != null ? $"Added to \"{selectedList.Name}\"" : string.Empty;

        // Reset opacity before showing
        ConfirmationOverlay.Opacity = 0;

        BubbleContent.Visibility = Visibility.Collapsed;
        ConfirmationOverlay.Visibility = Visibility.Visible;

        // Play spring pop-in
        var fadeIn = (Storyboard)BubbleRoot.Resources["ConfirmationFadeIn"];
        fadeIn.Begin();

        // Hold for the user to read it (pop-in is 0.45 s, hold remainder up to 1.2 s total)
        await Task.Delay(800);

        // Play shrink + fade out
        var fadeOut = (Storyboard)BubbleRoot.Resources["ConfirmationFadeOut"];

        var tcs = new TaskCompletionSource<bool>();
        fadeOut.Completed += (_, _) => tcs.TrySetResult(true);
        fadeOut.Begin();
        await tcs.Task;

        Close();
    }

    private void OnWindowClosed()
    {
        // Notify mascot that bubble closed
        var mascotVm = App.MainWindowInstance?.Content as FrameworkElement;
        // We'll handle this differently in MascotViewModel
    }
}
