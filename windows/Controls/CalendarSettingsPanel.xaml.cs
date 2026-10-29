using Hatch.ViewModels;
using Hatch.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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

    private async void ReminderDetailsButton_Click(object sender, RoutedEventArgs e)
        => await ShowReminderDetailsDialogAsync();

    private async Task ShowReminderDetailsDialogAsync()
    {
        const double ContentDialogHorizontalContentInset = 48;
        const double ContentDialogVerticalChrome = 160;
        var maxDialogWidth = Math.Min(
            MainWindow.MinimumWindowWidth * 0.8 / XamlRoot.RasterizationScale,
            XamlRoot.Size.Width * 0.8);
        var body = new TextBlock
        {
            Text = Strings.Get("Calendar_ReminderDialogBody"),
            TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetAutomationId(body, "Calendar_ReminderDialogBody");
        var range = new TextBlock
        {
            Text = ViewModel.RangeLabel,
            TextWrapping = TextWrapping.Wrap,
            Style = Hatch.Converters.ThemeResourceHelper.GetStyle("DialogSecondaryCaptionStyle")
        };
        AutomationProperties.SetAutomationId(range, "Calendar_ReminderDialogRange");
        var content = new ScrollViewer
        {
            Content = new StackPanel { Spacing = 12, Children = { body, range } },
            MaxWidth = Math.Max(0, maxDialogWidth - ContentDialogHorizontalContentInset),
            MaxHeight = Math.Max(0, XamlRoot.Size.Height * 0.8 - ContentDialogVerticalChrome),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var dialog = new ContentDialog
        {
            Title = Strings.Get("Calendar_ReminderDialogTitle"),
            Content = content,
            CloseButtonText = Strings.Get("Settings_AboutDialog_Close"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = App.GetElementTheme(App.Settings.Theme),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            XamlRoot = XamlRoot
        };
        AutomationProperties.SetAutomationId(dialog, "Calendar_ReminderDialog");
        await dialog.ShowAsync();
    }
}
