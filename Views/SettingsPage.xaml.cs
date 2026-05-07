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
}
