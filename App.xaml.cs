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
    public static QuickAddBubbleWindow? BubbleWindowInstance { get; set; }

    public App()
    {
        InitializeComponent();

        SettingsService.Load();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            MainWindowInstance = new MainWindow();
            MainWindowInstance.Activate();

            MascotWindowInstance = new MascotWindow();
            MascotWindowInstance.Activate(); // focus returns to MascotWindow last
        }
        catch (Exception ex)
        {
            var msg = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            System.Diagnostics.Debug.WriteLine(msg);
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "hatch-crash.log");
                File.WriteAllText(logPath, msg);
            }
            catch { }
            throw;
        }
    }
}
