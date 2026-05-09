using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class MainPage : Page
{
    private readonly MainViewModel _viewModel = new();

    public MainViewModel ViewModel => _viewModel;

    public MainPage()
    {
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SelectNavItem(_viewModel.ActiveNavItem);
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
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage), null, new DrillInNavigationTransitionInfo());
        }
        else if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            _viewModel.ActiveNavItem = tag;
            NavigateToTaskList();
        }
    }

    private void NavigateToTaskList()
    {
        ContentFrame.Navigate(typeof(TaskListPage), _viewModel, new DrillInNavigationTransitionInfo());
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }
}
