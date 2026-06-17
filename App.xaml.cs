using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Hatch.Models;
using Hatch.Services;
using Hatch.Views;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using ProtocolActivatedEventArgs = Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs;

namespace Hatch;

public partial class App : Application
{
    public static SettingsService SettingsService { get; } = new();
    public static AppSettings Settings => SettingsService.Current;
    public static SyncService SyncService { get; } = new();
    public static NotificationSchedulerService NotificationScheduler { get; } = new();
    public static MainWindow? MainWindowInstance { get; private set; }
    public static MascotWindow? MascotWindowInstance { get; private set; }
    public static QuickAddBubbleWindow? BubbleWindowInstance { get; set; }
    public static bool IsStartupLaunch { get; private set; } = false;

    public App()
    {
        InitializeComponent();
        SettingsService.Load();
    }

    private void OnAppActivated(object? sender, AppActivationArguments args)
    {
        if (args.Kind != ExtendedActivationKind.Protocol) return;
        if (args.Data is not ProtocolActivatedEventArgs protocol) return;

        var uri = protocol.Uri;
        var queue = MainWindowInstance?.DispatcherQueue;

        if (uri.Host == "auth-callback")
        {
            if (queue != null)
                queue.TryEnqueue(async () => await SyncService.HandleOAuthCallbackAsync(uri));
            else
                _ = SyncService.HandleOAuthCallbackAsync(uri);
            return;
        }

        if (queue == null) return;

        queue.TryEnqueue(() =>
        {
            if (uri.Host == "opentask" && TryGetQueryParam(uri, "id", out var openId) &&
                Guid.TryParse(openId, out var openGuid))
            {
                MainWindowInstance?.ShowAndSelectTask(openGuid);
            }
            else if (uri.Host == "complete" && TryGetQueryParam(uri, "id", out var completeId) &&
                     Guid.TryParse(completeId, out var completeGuid))
            {
                MainWindowInstance?.ViewModel.CompleteTaskById(completeGuid);
            }
        });
    }

    private static bool TryGetQueryParam(Uri uri, string name, out string value)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var sep = part.IndexOf('=');
            if (sep > 0 && part[..sep] == name)
            {
                value = Uri.UnescapeDataString(part[(sep + 1)..]);
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Single-instance: if another Hatch is already running, redirect this activation
            // to it (e.g. hatch:// OAuth callback) and exit without showing a window.
            var mainInstance = AppInstance.FindOrRegisterForKey("hatch-main");
            if (!mainInstance.IsCurrent)
            {
                await mainInstance.RedirectActivationToAsync(
                    AppInstance.GetCurrent().GetActivatedEventArgs());
                Application.Current.Exit();
                return;
            }
            // We are the main instance — handle future activations (OAuth callbacks, etc.)
            mainInstance.Activated += OnAppActivated;

            // Restore the saved session so the user stays signed in across launches.
            await SyncService.InitializeAsync();
            // Pull before creating windows — MainViewModel.LoadAsync reads the updated file naturally.
            await SyncService.PullIfNewerAsync();
            SyncService.StartAutoSync();
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
