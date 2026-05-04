using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoWinUI3.ViewModels;

namespace TodoWinUI3.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }
}
