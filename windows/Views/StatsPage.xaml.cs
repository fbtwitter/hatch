using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    public static bool HasNav(string? navTag) => navTag != null;

    private void TaskRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is UpcomingTaskInfo row)
            App.MainWindowInstance?.NavigateToTask(row.Task);
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is StatTileInfo tile && tile.NavTag is { } tag)
            App.MainWindowInstance?.NavigateTo(tag);
    }
}
