using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class StatsPage : Page
{
    private readonly StatsViewModel _viewModel;
    public StatsViewModel ViewModel => _viewModel;

    public StatsPage()
    {
        InitializeComponent();
        _viewModel = new StatsViewModel();
        DataContext = _viewModel;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _viewModel.RefreshStats();
    }

    private void UpcomingRow_Tapped(object sender, TappedRoutedEventArgs e)
        => (App.MainWindowInstance as MainWindow)?.NavigateTo("planned");
}
