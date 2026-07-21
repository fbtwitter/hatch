using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Hatch.Models;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;
    public SettingsViewModel ViewModel => _viewModel;

    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel();
        DataContext = _viewModel;
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;

        _viewModel.ConflictDetected += OnConflictDetected;

        // Pre-select the currently saved hotkey key in the ComboBox
        // and initialise the mascot size slider/label
        Loaded += (_, _) =>
        {
            SyncHotkeyKeySelector();
            SyncMascotSizeSlider();
        };
    }

    private async void OnConflictDetected(SyncConflict conflict)
    {
        var choice = await ShowConflictDialogAsync(conflict);
        if (choice == null)
        {
            // User cancelled: sign out so state is clean.
            await App.SyncService.SignOutAsync();
            return;
        }
        await _viewModel.ResolveConflictAsync(choice.Value);
    }

    private async Task<SyncConflictResolution?> ShowConflictDialogAsync(SyncConflict conflict)
    {
        static string FormatDate(DateTime utc) => utc == DateTime.MinValue
            ? "Unknown"
            : utc.ToLocalTime().ToString("MMM d, h:mm tt");

        static string Pluralize(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

        var description = new TextBlock
        {
            Text = "You have tasks on this device and on your account. " +
                   "Merge keeps everything from both; the other two options replace one side entirely.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var comparisonGrid = new Grid { ColumnSpacing = 12 };
        comparisonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        comparisonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var localCard = MakeSummaryCard(
            "  This device",
            Pluralize(conflict.LocalTaskCount, "task"),
            Pluralize(conflict.LocalListCount, "list"),
            "Last updated",
            FormatDate(conflict.LocalLastModified),
            column: 0);

        var serverCard = MakeSummaryCard(
            "  Your account",
            Pluralize(conflict.ServerTaskCount, "task"),
            Pluralize(conflict.ServerListCount, "list"),
            "Last synced",
            FormatDate(conflict.ServerLastModified),
            column: 1);

        comparisonGrid.Children.Add(localCard);
        comparisonGrid.Children.Add(serverCard);

        var mergeOption = new RadioButton { Content = "Merge both (recommended) — keeps everything", IsChecked = true };
        var localOption = new RadioButton { Content = "Keep this device's data only" };
        var serverOption = new RadioButton { Content = "Use account data only" };
        var options = new StackPanel { Spacing = 4, Margin = new Thickness(0, 16, 0, 0) };
        options.Children.Add(mergeOption);
        options.Children.Add(localOption);
        options.Children.Add(serverOption);

        var content = new StackPanel { Spacing = 0, MinWidth = 420 };
        content.Children.Add(description);
        content.Children.Add(comparisonGrid);
        content.Children.Add(options);

        var dialog = new ContentDialog
        {
            Title = "Data conflict",
            Content = content,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;

        if (localOption.IsChecked == true)  return SyncConflictResolution.UseLocal;
        if (serverOption.IsChecked == true) return SyncConflictResolution.UseServer;
        return SyncConflictResolution.Merge;
    }

    private static UIElement MakeSummaryCard(
        string title, string tasks, string lists, string dateLabel, string dateStr, int column)
    {
        var panel = new StackPanel { Spacing = 3, Padding = new Thickness(12) };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(new TextBlock { Text = tasks });
        panel.Children.Add(new TextBlock { Text = lists });
        panel.Children.Add(new TextBlock
        {
            Text = $"{dateLabel}: {dateStr}",
            FontSize = 12,
            Opacity = 0.7,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        var border = new Border
        {
            Child = panel,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1)
        };

        // Look up theme brushes via the app's merged resource dictionaries.
        if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var bg) && bg is Brush bgBrush)
            border.Background = bgBrush;
        if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var stroke) && stroke is Brush strokeBrush)
            border.BorderBrush = strokeBrush;

        Grid.SetColumn(border, column);
        return border;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private async void BrowseLottieButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        // FileOpenPicker requires an owner window HWND in WinUI 3
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            Win32Interop.GetWindowFromWindowId(App.MainWindowInstance!.AppWindow.Id));
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file != null)
            _viewModel.SetLottieFilePath(file.Path);
    }

    private void ClearLottieButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetLottieFilePath(null);
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var format = (string)((Button)sender).Tag;
        var (extension, description) = format switch
        {
            "csv"      => (".csv", "CSV file"),
            "markdown" => (".md", "Markdown file"),
            _          => (".json", "JSON file")
        };

        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            Win32Interop.GetWindowFromWindowId(App.MainWindowInstance!.AppWindow.Id));
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.SuggestedFileName = $"hatch-tasks-{DateTime.Now:yyyy-MM-dd}";
        picker.FileTypeChoices.Add(description, new List<string> { extension });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        await _viewModel.ExportAsync(format, file.Path);
    }

    private void SyncHotkeyKeySelector()
    {
        var vk = _viewModel.HotkeyVirtualKey;
        for (int i = 0; i < HotkeyKeySelector.Items.Count; i++)
        {
            if (HotkeyKeySelector.Items[i] is ComboBoxItem item &&
                item.Tag is string tag && uint.TryParse(tag, out var tagVk) && tagVk == vk)
            {
                HotkeyKeySelector.SelectedIndex = i;
                return;
            }
        }
        HotkeyKeySelector.SelectedIndex = 0; // fallback to Space
    }

    private void HotkeyKeySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotkeyKeySelector.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag && uint.TryParse(tag, out var vk))
        {
            _viewModel.HotkeyVirtualKey = vk;
        }
    }

    private void SyncMascotSizeSlider()
    {
        MascotSizeSlider.Value = Math.Clamp(_viewModel.MascotSize, 60, 200);
        MascotSizeHeaderLabel.Text = $"{_viewModel.MascotSize}px";
    }

    private void MascotSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_viewModel is null) return;

        var size = (int)MascotSizeSlider.Value;
        MascotSizeHeaderLabel.Text = $"{size}px";
        _viewModel.MascotSize = size;
    }

    private void MascotSizePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var size))
        {
            MascotSizeSlider.Value = size;
        }
    }

    // PasswordBox.Password cannot be bound via x:Bind — pass directly to ViewModel.
    private async void SyncSignIn_Click(object sender, RoutedEventArgs e)
        => await _viewModel.SignInAsync(SyncPasswordBox.Password);

    private async void SyncSignUp_Click(object sender, RoutedEventArgs e)
        => await _viewModel.SignUpAsync(SyncPasswordBox.Password);

    private async void SyncSetPassphrase_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SetSyncPassphraseAsync(SyncPassphraseBox.Password);
        SyncPassphraseBox.Password = "";
    }

    private async void SyncGitHubSignIn_Click(object sender, RoutedEventArgs e)
    {
        var url = await App.SyncService.GetGitHubSignInUrlAsync();
        if (!string.IsNullOrEmpty(url))
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }
}
