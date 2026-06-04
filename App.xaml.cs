using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Hatch.Models;
using Hatch.Services;
using Hatch.Views;
using Microsoft.UI.Dispatching;

namespace Hatch;

public partial class App : Application
{
    public static SettingsService SettingsService { get; } = new();
    public static AppSettings Settings => SettingsService.Current;
    public static SyncService SyncService { get; } = new();
    public static MainWindow? MainWindowInstance { get; private set; }
    public static MascotWindow? MascotWindowInstance { get; private set; }
    public static QuickAddBubbleWindow? BubbleWindowInstance { get; set; }
    public static bool IsStartupLaunch { get; private set; } = false;

    public App()
    {
        InitializeComponent();

        SettingsService.Load();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Initialize sync and pull latest data before the main window loads.
            // If the server has newer data than the last local sync, overwrite tasks.json
            // so MainViewModel reads the up-to-date content.
            await SyncService.InitializeAsync();
            if (SyncService.IsSignedIn)
            {
                var remote = await SyncService.PullIfNewerAsync();
                if (remote != null)
                    await new TaskStorageService().SaveAsync(remote);
            }
            // Unpackaged (Debug): StartupRegistryService writes --startup into the Run key.
            // Packaged (MSIX): activation kind is StartupTask when launched by the OS.
            // Never infer startup from empty args — that would suppress the window on every manual launch.
            IsStartupLaunch =
                args.Arguments.Contains(Services.StartupRegistryService.StartupArg, StringComparison.Ordinal) ||
                Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent()
                    .GetActivatedEventArgs().Kind ==
                    Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;

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
            // HATCH_UI_TEST=1 forces main window visible even during startup-launch suppression
            var uiTest = Environment.GetEnvironmentVariable("HATCH_UI_TEST") == "1";
            if (!IsStartupLaunch || uiTest)
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
