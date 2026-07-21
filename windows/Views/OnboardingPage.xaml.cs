using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class OnboardingPage : Page
{
    private MainViewModel? _viewModel;

    public OnboardingPage()
    {
        InitializeComponent();
        // ScrollViewer.ViewportHeight is not a change-notifying binding source —
        // keep the content at least viewport-tall (for vertical centering) manually.
        RootScroller.SizeChanged += (_, _) => ContentRoot.MinHeight = RootScroller.ViewportHeight;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _viewModel = e.Parameter as MainViewModel;
    }

    private async void GetStartedButton_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.FirstRunComplete = true;
        await App.SettingsService.SaveAsync();
        Frame.Navigate(typeof(MainPage), _viewModel);
        Frame.BackStack.Clear();
    }
}
