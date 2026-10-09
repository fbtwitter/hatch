using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Hatch.Tests.Infrastructure;

/// <summary>
/// Assembly-level fixture: launches Hatch once via FlaUI (UIA3) and keeps the
/// app + automation alive for the entire test run. No driver service required.
/// </summary>
[TestClass]
public static class TestSetup
{
    public static Application? App { get; private set; }
    public static UIA3Automation? Auto { get; private set; }
    public static Window? MainWindow { get; private set; }
    public static string DataDirectory { get; private set; } = string.Empty;
    private static string TestInstanceKey { get; set; } = string.Empty;

    public static void ResetUi()
    {
        if (App is null || Auto is null || MainWindow is null) return;

        ShowWindow(new IntPtr(MainWindow.Properties.NativeWindowHandle.Value), 9);
        foreach (var id in new[] { "FocusMode_Exit", "Bubble_Close", "ProactiveTip_Close", "PaneCloseButton" })
        {
            try
            {
                foreach (var window in App.GetAllTopLevelWindows(Auto))
                {
                    var control = window.FindFirstDescendant(cf => cf.ByAutomationId(id));
                    if (control is null || control.IsOffscreen) continue;
                    control.AsButton().Invoke();
                    break;
                }
            }
            catch { }
        }

        try
        {
            MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Nav_AllTasks"))?.Click();
        }
        catch { }

        Thread.Sleep(200);
    }

    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        Auto = new UIA3Automation();

        DataDirectory = Environment.GetEnvironmentVariable("HATCH_UI_TEST_DATA_DIR")
            ?? Path.Combine(Path.GetTempPath(), "Hatch.UiTests", Guid.NewGuid().ToString("N"));
        TestInstanceKey = "hatch-ui-test-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(DataDirectory);
        bool longTip = Environment.GetEnvironmentVariable("HATCH_TEST_LONG_TIP") == "1";
        bool proactiveTip = Environment.GetEnvironmentVariable("HATCH_TEST_PROACTIVE") == "1";
        bool noActionTip = Environment.GetEnvironmentVariable("HATCH_TEST_NO_ACTION_TIP") == "1";
        int customCount = 1;
        long dayNumber = DateTime.Today.Ticks / TimeSpan.TicksPerDay;
        while (dayNumber % (12 + customCount) < 12) customCount++;
        var customTips = longTip ? Enumerable.Repeat(string.Join("\n", Enumerable.Range(1, 45)
            .Select(i => $"Runtime layout test line {i}: this deliberately long tip must scroll without hiding controls.")), customCount).ToArray() : [];
        File.WriteAllText(Path.Combine(DataDirectory, "settings.json"), JsonSerializer.Serialize(new
        {
            FirstRunComplete = Environment.GetEnvironmentVariable("HATCH_TEST_ONBOARDING") != "1",
            Theme = int.TryParse(Environment.GetEnvironmentVariable("HATCH_TEST_THEME"), out var theme) ? theme : 0,
            MascotX = int.TryParse(Environment.GetEnvironmentVariable("HATCH_TEST_MASCOT_X"), out var x) ? x : 700,
            MascotY = int.TryParse(Environment.GetEnvironmentVariable("HATCH_TEST_MASCOT_Y"), out var y) ? y : 500,
            MascotSize = 120, HideWhenFullscreen = false, MinimizeToTray = true,
            ShowTipsAutomatically = Environment.GetEnvironmentVariable("HATCH_TEST_PROACTIVE") == "1",
            ActiveNavItem = "alltasks", MuteAnimation = Environment.GetEnvironmentVariable("HATCH_TEST_MUTE") == "1",
            CustomTips = customTips
        }));
        object[] tasks;
        if (!proactiveTip)
        {
            tasks = [new { Id = Guid.NewGuid(), Title = "_RuntimeProbe_", CreatedAt = DateTimeOffset.UtcNow }];
        }
        else if (noActionTip)
        {
            var now = DateTimeOffset.Now;
            tasks = [new
            {
                Id = Guid.NewGuid(), Title = "_RuntimeProbe_", CreatedAt = now.DateTime.AddDays(-3),
                IsCompleted = true, CompletedAt = now.AddDays(-2)
            }];
        }
        else
        {
            var now = DateTimeOffset.Now;
            tasks =
            [
                new
                {
                    Id = Guid.NewGuid(), Title = "_RuntimeProbe_", CreatedAt = now.DateTime,
                    IsCompleted = false, CompletedAt = (DateTimeOffset?)null
                },
                new
                {
                    Id = Guid.NewGuid(), Title = "_RuntimeCompletedProbe_", CreatedAt = now.DateTime.AddDays(-1),
                    IsCompleted = true, CompletedAt = now
                }
            ];
        }
        File.WriteAllText(Path.Combine(DataDirectory, "tasks.json"), JsonSerializer.Serialize(new
        {
            Tasks = tasks,
            Lists = new[] { new { Id = Guid.NewGuid(), Name = "Runtime test list" } }
        }));

        App = LaunchTestApp();

        // Give WinUI 3 time to finish initializing all windows
        Thread.Sleep(2500);

        MainWindow = FindMainWindow();
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        if (App != null)
        {
            var process = Process.GetProcessById(App.ProcessId);
            try
            {
                App.Close();
                if (!process.WaitForExit(3000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            finally { process.Dispose(); }
        }
        Auto?.Dispose();
    }

    public static void Restart()
    {
        // Preserve the synthetic data directory; restart must never reseed the fixture.
        Cleanup();
        Auto = new UIA3Automation();
        App = LaunchTestApp();
        MainWindow = FindMainWindow();
    }

    private static Application LaunchTestApp()
    {
        // Pass an explicit environment and marker arguments to the child. The user's
        // normal Hatch instance may already own the production key, so this test run
        // uses isolated storage and a unique AppInstance key across its restarts.
        var startInfo = new ProcessStartInfo(ResolveAppExe()) { UseShellExecute = false };
        startInfo.Environment["HATCH_UI_TEST"] = "1";
        startInfo.Environment["HATCH_UI_TEST_DATA_DIR"] = DataDirectory;
        startInfo.Environment["HATCH_UI_TEST_INSTANCE_KEY"] = TestInstanceKey;
        startInfo.ArgumentList.Add("--hatch-ui-test");
        startInfo.ArgumentList.Add("--hatch-ui-test-data-dir");
        startInfo.ArgumentList.Add(DataDirectory);
        startInfo.ArgumentList.Add("--hatch-ui-test-instance-key");
        startInfo.ArgumentList.Add(TestInstanceKey);
        return Application.Launch(startInfo);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string ResolveAppExe()
    {
        var envPath = Environment.GetEnvironmentVariable("HATCH_APP_EXE");
        if (envPath is not null && File.Exists(envPath))
            return envPath;

        // Walk from test output dir up to solution root (contains *.sln)
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            if (Directory.GetFiles(dir, "*.sln").Length > 0)
            {
                var exe = Path.Combine(dir, "bin", "x64", "Debug",
                    "net10.0-windows10.0.19041.0", "hatch.exe");
                if (File.Exists(exe))
                    return exe;
            }
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null) break;
            dir = parent;
        }

        throw new FileNotFoundException(
            "Could not locate hatch.exe. Run scripts/run-ui-regression.ps1 first, " +
            "or set HATCH_APP_EXE to an app executable path.");
    }

    /// <summary>
    /// Finds the Hatch MainWindow (title "Hatch") from the UIA desktop tree.
    /// When HATCH_UI_TEST=1 the app activates it on launch, so we just wait for it.
    /// </summary>
    private static Window FindMainWindow()
    {
        int pid = App!.ProcessId;
        var windowDeadline = DateTime.UtcNow.AddSeconds(15);
        Window? hatchWindow = null;

        // Step 1: find the window titled "Hatch" belonging to our process
        while (DateTime.UtcNow < windowDeadline && hatchWindow is null)
        {
            try
            {
                var desktop = Auto!.GetDesktop();
                foreach (var child in desktop.FindAllChildren())
                {
                    try
                    {
                        if (child.Properties.ProcessId.Value == pid && child.Name == "Hatch")
                        {
                            hatchWindow = child.AsWindow();
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            if (hatchWindow is null)
            {
                // Also check via app-tracked windows
                try
                {
                    foreach (var w in App!.GetAllTopLevelWindows(Auto!))
                    {
                        if (w.Title == "Hatch") { hatchWindow = w; break; }
                    }
                }
                catch { }
            }

            if (hatchWindow is null) Thread.Sleep(400);
        }

        if (hatchWindow is null)
            throw new InvalidOperationException(
                "Could not find window titled 'Hatch' in process after 15 s. " +
                "Ensure the selected app executable starts and HATCH_UI_TEST=1 was inherited.");

        // Step 2: widen to 800px so nav rail items are in the UIA tree, then single lookup
        var transform = hatchWindow.Patterns.Transform;
        if (transform.IsSupported)
            transform.Pattern.Resize(800, hatchWindow.BoundingRectangle.Height);
        Thread.Sleep(300);

        var navItem = hatchWindow.FindFirstDescendant(cf => cf.ByAutomationId("Nav_AllTasks"));
        var onboarding = hatchWindow.FindFirstDescendant(cf => cf.ByAutomationId("Onboarding_GetStarted"));
        if (navItem is null && onboarding is null)
            throw new InvalidOperationException(
                "Nav_AllTasks not found after resizing window to 800px. " +
                "Check NavigationView AutomationId and CompactModeThresholdWidth.");

        return hatchWindow;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
