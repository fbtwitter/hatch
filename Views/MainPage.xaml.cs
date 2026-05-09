using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class MainPage : Page
{
    private MainViewModel _viewModel = null!;
    private bool _suppressNavigation = false;

    public MainViewModel ViewModel => _viewModel;

    public MainPage()
    {
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MainViewModel vm && _viewModel != vm)
            _viewModel = vm;
        _viewModel ??= new MainViewModel();

        _suppressNavigation = true;
        SelectNavItem(_viewModel.ActiveNavItem);
        _suppressNavigation = false;

        NavigateToTaskList();
    }

    private void SelectNavItem(string tag)
    {
        var items = new[] { MyDayItem, ImportantItem, PlannedItem, AllTasksItem };
        foreach (var item in items)
        {
            if (item.Tag?.ToString() == tag)
            {
                NavView.SelectedItem = item;
                break;
            }
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavigation) return;

        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage), null, new DrillInNavigationTransitionInfo());
            MainTitleBar.IsBackButtonVisible = true;
        }
        else if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            _viewModel.ActiveNavItem = tag;
            NavigateToTaskList();
        }
    }

    private void NavigateToTaskList()
    {
        ContentFrame.Navigate(typeof(TaskListPage), _viewModel, new SuppressNavigationTransitionInfo());
        ContentFrame.BackStack.Clear();
        MainTitleBar.IsBackButtonVisible = false;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
        MainTitleBar.IsBackButtonVisible = ContentFrame.CanGoBack;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }
}
