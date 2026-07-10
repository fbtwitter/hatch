using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Hatch.Models;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class SearchPage : Page
{
    private MainViewModel _viewModel = null!;
    public MainViewModel ViewModel => _viewModel;

    public SearchPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MainViewModel vm)
            _viewModel = vm;
    }

    // Full editing (star, tags, due date, delete, snooze, focus mode) only lives in
    // TaskListPage's details pane — this page is a lighter results browser, so tapping
    // a result jumps over to open it there rather than duplicating every row control.
    private void ResultRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var task = (TodoItem)((FrameworkElement)sender).Tag;
        (App.MainWindowInstance as MainWindow)?.NavigateToTask(task);
    }
}
