using Microsoft.UI.Xaml.Controls;

namespace Hatch.Views;

public sealed partial class CalendarPage : Page
{
    public CalendarPage()
    {
        InitializeComponent();
        Loaded += (_, _) => App.MainWindowInstance?.NavigateTo("planned");
    }
}
