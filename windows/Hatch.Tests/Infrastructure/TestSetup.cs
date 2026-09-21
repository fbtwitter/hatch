using System.Diagnostics;
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

    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        Auto = new UIA3Automation();

        DataDirectory = Environment.GetEnvironmentVariable("HATCH_UI_TEST_DATA_DIR")
            ?? Path.Combine(Path.GetTempPath(), "Hatch.UiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);
        bool longTip = Environment.GetEnvironmentVariable("HATCH_TEST_LONG_TIP") == "1";
        int customCount = 1;
        long dayNumber = DateTime.Today.Ticks / TimeSpan.TicksPerDay;
        while (dayNumber % (12 + customCount) < 12) customCount++;
        var customTips = longTip ? Enumerable.Repeat(string.Join("\n", Enumerable.Range(1, 45)
            .Select(i => $"Runtime layout test line {i}: this deliberately long tip must scroll without hiding controls.")), customCount).ToArray() : [];
        File.WriteAllText(Path.Combine(DataDirectory, "settings.json"), JsonSerializer.Serialize(new
        {
            FirstRunComplete = true,
            MascotX = int.TryParse(Environment.GetEnvironmentVariable("HATCH_TEST_MASCOT_X"), out var x) ? x : 700,
            MascotY = int.TryParse(Environment.GetEnvironmentVariable("HATCH_TEST_MASCOT_Y"), out var y) ? y : 500,
            MascotSize = 120, HideWhenFullscreen = false, MinimizeToTray = true,
            ShowTipsAutomatically = Environment.GetEnvironmentVariable("HATCH_TEST_PROACTIVE") == "1",
            ActiveNavItem = "alltasks", MuteAnimation = Environment.GetEnvironmentVariable("HATCH_TEST_MUTE") == "1",
            CustomTips = customTips
        }));
        File.WriteAllText(Path.Combine(DataDirectory, "tasks.json"), JsonSerializer.Serialize(new
        {
            Tasks = new[] { new { Id = Guid.NewGuid(), Title = "_RuntimeProbe_", CreatedAt = DateTimeOffset.UtcNow } },
            Lists = new[] { new { Id = Guid.NewGuid(), Name = "Runtime test list" } }
        }));

        // HATCH_UI_TEST=1 is inherited by the child process and forces the
        // main window to activate even when RunAtStartup=true suppresses it.
        Environment.SetEnvironmentVariable("HATCH_UI_TEST", "1");
        Environment.SetEnvironmentVariable("HATCH_UI_TEST_DATA_DIR", DataDirectory);
        App = Application.Launch(ResolveAppExe());
        Environment.SetEnvironmentVariable("HATCH_UI_TEST", null);

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
            App.Close();
            if (!process.WaitForExit(3000)) process.Kill();
            process.Dispose();
        }
        Auto?.Dispose();
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
            "Could not locate hatch.exe. Build the main project (Debug|x64) first, " +
            "or set HATCH_APP_EXE to the exe path.");
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
                "Ensure the main project is built (Debug|x64) and HATCH_UI_TEST=1 was inherited.");

        // Step 2: widen to 800px so nav rail items are in the UIA tree, then single lookup
        var transform = hatchWindow.Patterns.Transform;
        if (transform.IsSupported)
            transform.Pattern.Resize(800, hatchWindow.BoundingRectangle.Height);
        Thread.Sleep(300);

        var navItem = hatchWindow.FindFirstDescendant(cf => cf.ByAutomationId("Nav_AllTasks"));
        if (navItem is null)
            throw new InvalidOperationException(
                "Nav_AllTasks not found after resizing window to 800px. " +
                "Check NavigationView AutomationId and CompactModeThresholdWidth.");

        return hatchWindow;
    }
}
