using Microsoft.UI.Xaml;
using Hatch.Models;
using Hatch.Services;
using Hatch.Views;

namespace Hatch;

public partial class App : Application
{
    public static SettingsService SettingsService { get; } = new();
    public static AppSettings Settings => SettingsService.Current;
    public static MainWindow? MainWindowInstance { get; private set; }
    public static MascotWindow? MascotWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
        SettingsService.Load();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MascotWindowInstance = new MascotWindow();
        MascotWindowInstance.Activate();

        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate(); // takes focus back from mascot
    }
}
