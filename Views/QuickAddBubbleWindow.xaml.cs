using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

        // Size
        AppWindow.Resize(new Windows.Graphics.SizeInt32(340, 280));

        // Initialize list selector
        var mainVm = GetMainViewModel();
        if (mainVm != null)
        {
            ListSelector.ItemsSource = mainVm.Lists;
            ListSelector.DisplayMemberPath = nameof(TaskList.Name);
            if (mainVm.Lists.Count > 0)
                ListSelector.SelectedIndex = 0;
        }

        AddButton.Click += AddButton_Click;
        CloseButton.Click += (_, _) => Close();
        Closed += (_, _) => OnWindowClosed();

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
                AddButton_Click(AddButton, null!);
                e.Handled = true;
            }
        };

        // Focus title input after window is shown
        this.Activated += (_, _) => TaskTitleBox.Focus(FocusState.Programmatic);
    }

    public void PositionRelativeToMascot(int mascotX, int mascotY, int mascotWidth)
    {
        const int bubbleWidth = 340;
        const int bubbleHeight = 280;
        const int gap = 15;

        var pt = new NativeMethods.POINT { X = mascotX, Y = mascotY };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            return;

        var workArea = mi.rcWork;
        int bubbleX = mascotX - bubbleWidth - gap;
        int bubbleY = mascotY + (mascotWidth - bubbleHeight) / 2;

        // If bubble would be off-screen left, position it to the right instead
        if (bubbleX < workArea.left)
        {
            bubbleX = mascotX + mascotWidth + gap;
        }

        // Clamp to work area
        bubbleX = Math.Clamp(bubbleX, workArea.left, workArea.right - bubbleWidth);
        bubbleY = Math.Clamp(bubbleY, workArea.top, workArea.bottom - bubbleHeight);

        AppWindow.Move(new PointInt32(bubbleX, bubbleY));
    }

    private MainViewModel? GetMainViewModel()
    {
        var mainWindow = App.MainWindowInstance as MainWindow;
        if (mainWindow?.Content is Frame frame && frame.Content is MainPage mainPage)
        {
            return mainPage.ViewModel;
        }
        return null;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TaskTitleBox.Text?.Trim();
        if (string.IsNullOrEmpty(title)) return;

        var mainVm = GetMainViewModel();
        if (mainVm == null) return;

        var selectedListId = (ListSelector.SelectedItem as TaskList)?.Id ?? App.Settings.LastUsedListId;
        if (selectedListId == Guid.Empty)
        {
            selectedListId = mainVm.Lists.FirstOrDefault()?.Id ?? Guid.Empty;
        }

        var task = new TodoItem
        {
            Title = title,
            ListId = selectedListId
        };

        if (TodayButton.IsChecked == true)
            task.DueDate = DateTimeOffset.Now;
        else if (TomorrowButton.IsChecked == true)
            task.DueDate = DateTimeOffset.Now.AddDays(1);

        mainVm.Tasks.Insert(0, task);
        mainVm.AttachTaskPropertyChangedHandler(task);
        mainVm.SaveAsync();

        App.Settings.LastUsedListId = selectedListId;
        _ = App.SettingsService.SaveAsync();

        // Reset form
        TaskTitleBox.Text = string.Empty;
        TodayButton.IsChecked = false;
        TomorrowButton.IsChecked = false;

        // Close bubble
        Close();
    }

    private void OnWindowClosed()
    {
        // Notify mascot that bubble closed
        var mascotVm = App.MainWindowInstance?.Content as FrameworkElement;
        // We'll handle this differently in MascotViewModel
    }
}
