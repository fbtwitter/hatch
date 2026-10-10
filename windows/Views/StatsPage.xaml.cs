using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class StatsPage : Page
{
    private readonly StatsViewModel _viewModel;
    private MainViewModel? _mainViewModel;
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
        if (_mainViewModel != null)
            _mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        _mainViewModel = e.Parameter as MainViewModel;
        if (_mainViewModel != null)
            _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        _viewModel.RefreshStats(_mainViewModel?.Tasks ?? []);
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_mainViewModel != null)
            _mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
    }

    private void OnMainViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ThemeVersion))
            _viewModel.RefreshStats(_mainViewModel?.Tasks ?? []);
    }

    private void TaskRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is UpcomingTaskInfo row)
            App.MainWindowInstance?.NavigateToTask(row.Task);
    }

    private void MyDayHero_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("myday");

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is StatTileInfo tile && tile.NavTag is { } tag)
            App.MainWindowInstance?.NavigateTo(tag);
    }
}
