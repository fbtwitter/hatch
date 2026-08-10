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

    // My Day is the only tile with a second value line ("0%") — collapse the row for
    // every other tile rather than reserve blank space for it.
    public static Visibility SecondaryValueVisibility(string? secondaryValue) =>
        string.IsNullOrEmpty(secondaryValue) ? Visibility.Collapsed : Visibility.Visible;

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
