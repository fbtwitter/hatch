using Microsoft.UI.Xaml;
using TodoWinUI3.Models;
using TodoWinUI3.Services;

namespace TodoWinUI3;

public partial class App : Application
{
    public static SettingsService SettingsService { get; } = new();
    public static AppSettings Settings => SettingsService.Current;
    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
        SettingsService.Load();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }
}
