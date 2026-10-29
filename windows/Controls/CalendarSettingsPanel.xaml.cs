using Hatch.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace Hatch.Controls;

public sealed partial class CalendarSettingsPanel : UserControl
{
    public CalendarViewModel ViewModel { get; }

    public CalendarSettingsPanel()
    {
        ViewModel = new(App.CalendarService, PickImportFileAsync);
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.StartAsync();
        Unloaded += (_, _) => ViewModel.Stop();
    }

    private static async Task<string?> PickImportFileAsync()
    {
        var picker = new FileOpenPicker(App.MainWindowInstance!.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".ics");
        return (await picker.PickSingleFileAsync())?.Path;
    }
}
