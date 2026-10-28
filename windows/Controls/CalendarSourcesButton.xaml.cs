using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hatch.Controls;

public sealed partial class CalendarSourcesButton : UserControl
{
    public CalendarSourcesButton() => InitializeComponent();

    private void OpenSettings_Click(object sender, RoutedEventArgs args)
        => App.MainWindowInstance?.NavigateToSettings("Calendars");
}
