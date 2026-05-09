using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel();
        DataContext = _viewModel;
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;

        // Pre-select the currently saved hotkey key in the ComboBox
        Loaded += (_, _) => SyncHotkeyKeySelector();
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
}
