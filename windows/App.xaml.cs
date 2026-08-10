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
    public static TipCoordinator TipCoordinator { get; } = new(SettingsService);
    public static MainWindow? MainWindowInstance { get; private set; }
    public static MascotWindow? MascotWindowInstance { get; private set; }
    public static QuickAddBubbleWindow? BubbleWindowInstance { get; set; }
    public static bool IsStartupLaunch { get; private set; } = false;

    public App()
    {
        InitializeComponent();
        RegisterProtocolWhenUnpackaged();
    }

    // Debug builds are unpackaged on purpose (see hatch.csproj) so XAML Hot Reload works,
    // which means Package.appxmanifest's hatch:// declaration does not apply and the OAuth
    // callback has nowhere to land. Registering at runtime routes hatch:// through the same
    // AppInstance activation path the packaged build uses, so OnAppActivated is unchanged.
    private static void RegisterProtocolWhenUnpackaged()
    {
        if (IsPackaged()) return;
        try
        {
            // Empty exePath means "this executable".
            ActivationRegistrationManager.RegisterForProtocolActivation(
                scheme: "hatch", logo: string.Empty, displayName: "Hatch", exePath: string.Empty);
        }
        catch
        {
            // Non-fatal: only OAuth sign-in depends on it, and the failure surfaces there.
        }
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current.Id;
            return true;
        }
        catch
        {
            return false;
        }
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

    private static void CenterOnWorkArea(AppWindow window)
    {
        var workArea = DisplayArea.Primary.WorkArea;
        var size = window.Size;
        window.Move(new Windows.Graphics.PointInt32(
            workArea.X + (workArea.Width  - size.Width)  / 2,
            workArea.Y + (workArea.Height - size.Height) / 2));
    }

    private static bool IsMascotHiddenAtLaunch()
    {
        var ticks = Settings.HideUntilTicks;
        if (ticks == null) return false;
        if (ticks.Value == long.MaxValue) return true;
        return DateTime.UtcNow < new DateTime(ticks.Value, DateTimeKind.Utc);
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

    private static async Task InitializeSyncAsync()
    {
        try
        {
            await SyncService.InitializeAsync();
            await SyncService.PullIfNewerAsync();
            SyncService.StartAutoSync();
        }
        catch
        {
            // Offline or Supabase unreachable — the app is fully functional without sync;
            // signing in again from Settings re-establishes it.
        }
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

            // Was synchronous File.ReadAllText in the App constructor — moved here and
            // awaited so cold start never blocks the UI thread on file I/O. Placed after
            // the redirect-and-exit path above (a secondary instance shouldn't pay for a
            // settings read it'll never use) but before anything below, which all touches
            // Settings/App.Settings.
            await SettingsService.LoadAsync();

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
                SettingsService.SaveDebounced();
            }

            MainWindowInstance = new MainWindow();
            // HATCH_UI_TEST=1 forces main window visible even during startup-launch suppression
            var uiTest = Environment.GetEnvironmentVariable("HATCH_UI_TEST") == "1";
            // Mascot-only launch: the main window opens on demand (mascot click, tray,
            // hotkey). It still opens when there would otherwise be nothing on screen —
            // first run (onboarding), Show Mascot off, or an active "Hide for…" window.
            var mascotVisibleAtLaunch = Settings.ShowMascot && !IsMascotHiddenAtLaunch();
            if (uiTest || (!IsStartupLaunch && (!Settings.FirstRunComplete || !mascotVisibleAtLaunch)))
            {
                // Without a visible mascot there is no anchor to position near — center instead.
                if (!mascotVisibleAtLaunch)
                    CenterOnWorkArea(MainWindowInstance.AppWindow);
                MainWindowInstance.Activate();
            }

            MascotWindowInstance = new MascotWindow();
            MascotWindowInstance.Activate(); // focus returns to MascotWindow last

            // Sync runs off the launch path — a slow network round-trip must not delay
            // the mascot past the cold-start budget. A pull that lands after LoadAsync
            // reaches MainViewModel via TasksReceived → ReloadAsync.
            _ = InitializeSyncAsync();
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
