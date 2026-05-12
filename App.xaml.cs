using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
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
    public static bool IsStartupLaunch { get; private set; } = false;

    public App()
    {
        InitializeComponent();

        SettingsService.Load();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            IsStartupLaunch = Settings.RunAtStartup && string.IsNullOrEmpty(args.Arguments);

            // Initialize mascot position if unset, so main window can position relative to it
            if (Settings.MascotX < 0 || Settings.MascotY < 0)
            {
                var workArea = DisplayArea.Primary.WorkArea;
                int size = Settings.MascotSize;
                Settings.MascotX = workArea.X + workArea.Width - size - 12;
                Settings.MascotY = workArea.Y + workArea.Height - size - 12;
                _ = SettingsService.SaveAsync();
            }

            MainWindowInstance = new MainWindow();
            if (!IsStartupLaunch)
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
