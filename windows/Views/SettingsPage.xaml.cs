using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
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

    private void AddCustomTipButton_Click(object sender, RoutedEventArgs e) => CommitCustomTip();

    private void CustomTipInput_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        CommitCustomTip();
        e.Handled = true;
    }

    private void CommitCustomTip()
    {
        _viewModel.AddCustomTip(CustomTipInput.Text);
        CustomTipInput.Text = string.Empty;
    }

    private void RemoveCustomTipButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.RemoveCustomTip((string)((FrameworkElement)sender).Tag);

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            Win32Interop.GetWindowFromWindowId(App.MainWindowInstance!.AppWindow.Id));
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.SuggestedFileName = $"hatch-tasks-{DateTime.Now:yyyy-MM-dd}";
        picker.FileTypeChoices.Add("JSON file", new List<string> { ".json" });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        await _viewModel.ExportAsync(file.Path);
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

    private void ChangePassphraseStart_Click(object sender, RoutedEventArgs e)
        => ViewModel.StartChangePassphrase();

    private void ChangePassphraseCancel_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelChangePassphrase();
        ChangePassphraseOldBox.Password = "";
        ChangePassphraseNewBox.Password = "";
        ChangePassphraseConfirmBox.Password = "";
    }

    private async void ChangePassphraseSubmit_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ChangePassphraseAsync(
            ChangePassphraseOldBox.Password,
            ChangePassphraseNewBox.Password,
            ChangePassphraseConfirmBox.Password);
        ChangePassphraseOldBox.Password = "";
        ChangePassphraseNewBox.Password = "";
        ChangePassphraseConfirmBox.Password = "";
    }

    private async void MfaEnable_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartMfaEnrollmentAsync();
        await ShowMfaQrAsync(ViewModel.MfaQrSvg);
    }

    // Imaging is a View concern, so the SVG-to-image step lives here rather than in the
    // ViewModel. The setup key stays visible either way: on a desktop you are usually
    // enrolling from the machine you would otherwise be scanning with.
    private async Task ShowMfaQrAsync(string? svg)
    {
        MfaQrPanel.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(svg)) return;

        // Supabase returns raw markup, but tolerate a data: URI wrapper.
        var markup = svg.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? DecodeDataUri(svg)
            : svg;
        if (markup == null || !markup.Contains("<svg", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            var bytes = Encoding.UTF8.GetBytes(markup);
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var source = new SvgImageSource();
            var result = await source.SetSourceAsync(stream);
            if (result != SvgImageSourceLoadStatus.Success) return;

            MfaQrImage.Source = source;
            MfaQrPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            // Falling back to the setup key alone is a complete path, not a broken one.
        }
    }

    private static string? DecodeDataUri(string uri)
    {
        var comma = uri.IndexOf(',');
        if (comma < 0) return null;
        var payload = uri[(comma + 1)..];
        return uri[..comma].Contains("base64", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(payload))
            : Uri.UnescapeDataString(payload);
    }

    private async void MfaConfirm_Click(object sender, RoutedEventArgs e)
    {
        var code = MfaCodeBox.Text;
        await ViewModel.ConfirmMfaEnrollmentAsync(code);
        MfaCodeBox.Text = string.Empty;
    }

    private async void MfaCancel_Click(object sender, RoutedEventArgs e)
        => await ViewModel.CancelMfaEnrollmentAsync();

    private async void MfaDisable_Click(object sender, RoutedEventArgs e)
        => await ViewModel.DisableMfaAsync();

    private async void MfaChallengeSubmit_Click(object sender, RoutedEventArgs e)
    {
        var code = MfaChallengeCodeBox.Text;
        MfaChallengeCodeBox.Text = string.Empty;
        await ViewModel.SubmitMfaChallengeAsync(code);
    }

    private void MfaUseRecovery_Click(object sender, RoutedEventArgs e)
        => ViewModel.StartRecoveryCodeEntry();

    private void RecoveryCancel_Click(object sender, RoutedEventArgs e)
        => ViewModel.CancelRecoveryCodeEntry();

    private async void RecoveryRedeem_Click(object sender, RoutedEventArgs e)
    {
        var code = RecoveryCodeBox.Text;
        RecoveryCodeBox.Text = string.Empty;
        await ViewModel.RedeemRecoveryCodeAsync(code);
    }

    private void RecoveryCopy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.RecoveryCodesText);
        Clipboard.SetContent(package);
    }

    private void RecoveryDone_Click(object sender, RoutedEventArgs e)
        => ViewModel.DismissRecoveryCodes();

    private void SyncNotice_Close(InfoBar sender, object args)
        => ViewModel.DismissSyncNotice();

    private void RecoveryKitShow_Click(object sender, RoutedEventArgs e)
        => ViewModel.ShowRecoveryKit();

    private void RecoveryKitDone_Click(object sender, RoutedEventArgs e)
        => ViewModel.DismissRecoveryKit();

    private void RecoveryKitCopy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.RecoveryKitText ?? "");
        Clipboard.SetContent(package);
    }

    private async void RecoveryKitSave_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            Win32Interop.GetWindowFromWindowId(App.MainWindowInstance!.AppWindow.Id));
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.SuggestedFileName = ViewModel.RecoveryKitFileName;
        picker.FileTypeChoices.Add("Text file", new List<string> { ".txt" });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        await File.WriteAllTextAsync(file.Path, ViewModel.RecoveryKitText ?? "");
    }

    private async void SyncGitHubSignIn_Click(object sender, RoutedEventArgs e)
        => await _viewModel.SignInWithGitHubAsync();
}
