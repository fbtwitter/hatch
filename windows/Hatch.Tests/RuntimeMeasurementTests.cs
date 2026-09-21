using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core.Capturing;
using Hatch.Tests.Infrastructure;

namespace Hatch.Tests;

[TestClass]
public sealed class RuntimeMeasurementTests
{
    private readonly List<object> _samples = [];
    private readonly List<object> _checks = [];
    private string _output = string.Empty;
    private Process _process = null!;

    [TestMethod]
    public void RepeatedFocusSessions_ResourceLifetime()
    {
        _output = Environment.GetEnvironmentVariable("HATCH_MEASUREMENTS_DIR") ?? TestSetup.DataDirectory;
        Directory.CreateDirectory(_output);
        _process = Process.GetProcessById(TestSetup.App!.ProcessId);
        try
        {
            var main = TestSetup.MainWindow!;
            main.FindFirstDescendant(cf => cf.ByName("_RuntimeProbe_"))!.Click();
            var focus = WaitFor("PaneFocusRow").AsButton();
            focus.Invoke();
            WaitFor("FocusMode_Exit").AsButton().Invoke();
            Sample("focus_warmed_baseline");
            for (int i = 1; i <= 100; i++)
            {
                focus.Invoke();
                WaitFor("FocusMode_Exit").AsButton().Invoke();
                Thread.Sleep(100);
                if (i is 25 or 50 or 100) Sample($"after_{i}_focus_sessions");
            }
            var gcDumpTool = Environment.GetEnvironmentVariable("HATCH_GCDUMP_TOOL");
            if (!string.IsNullOrEmpty(gcDumpTool))
            {
                var start = new ProcessStartInfo(gcDumpTool) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var argument in new[] { "collect", "--process-id", _process.Id.ToString(),
                    "--output", Path.Combine(_output, "focus.gcdump"), "--timeout", "30" })
                    start.ArgumentList.Add(argument);
                using var dump = Process.Start(start)!;
                Assert.IsTrue(dump.WaitForExit(45_000), "Heap collection timed out.");
                Assert.AreEqual(0, dump.ExitCode, "Heap collection failed.");
                Sample("after_diagnostic_full_gc");
            }
        }
        finally
        {
            File.WriteAllText(Path.Combine(_output, "focus-lifetime.json"), JsonSerializer.Serialize(_samples,
                new JsonSerializerOptions { WriteIndented = true }));
            _process.Dispose();
        }
    }

    [TestMethod]
    public void PopupsAtSeededCorner_StayVisibleAndSeparate()
    {
        _output = Environment.GetEnvironmentVariable("HATCH_MEASUREMENTS_DIR") ?? TestSetup.DataDirectory;
        Directory.CreateDirectory(_output);
        using var process = Process.GetProcessById(TestSetup.App!.ProcessId);
        _process = process;
        var main = TestSetup.MainWindow!;
        main.FindFirstDescendant(cf => cf.ByName("_RuntimeProbe_"))!.Click();
        WaitFor("PaneFocusRow").AsButton().Invoke();
        ShowWindow(new IntPtr(main.Properties.NativeWindowHandle.Value), 0);
        var mascot = FindMascot();
        GetWindowRect(mascot, out var mascotRect);
        if (int.TryParse(Environment.GetEnvironmentVariable("HATCH_TEST_MASCOT_X"), out int x))
            Assert.AreEqual(x, mascotRect.Left, "Actual mascot X must match the tested corner");
        if (int.TryParse(Environment.GetEnvironmentVariable("HATCH_TEST_MASCOT_Y"), out int y))
            Assert.AreEqual(y, mascotRect.Top, "Actual mascot Y must match the tested corner");
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(mascot, 2), ref info);
        var work = info.Work.ToRectangle();
        Thread.Sleep(300);
        var focus = WaitFor("FocusMode_Viewport").BoundingRectangle;
        SaveImage("focus-corner");
        Assert.IsTrue(work.Contains(focus), $"Focus {focus} outside work area {work}");
        Assert.IsFalse(mascotRect.ToRectangle().IntersectsWith(focus));
        ClickMascot(mascot);
        var bubble = WaitFor("Bubble_Viewport").BoundingRectangle;
        SaveImage("bubble-corner");
        Assert.IsTrue(work.Contains(bubble), $"Bubble {bubble} outside work area {work}");
        Assert.IsFalse(mascotRect.ToRectangle().IntersectsWith(bubble));
        WaitFor("Bubble_Close").AsButton().Invoke();
        WaitFor("FocusMode_Exit").AsButton().Invoke();
        FlaUI.Core.Input.Mouse.Click(new Point((mascotRect.Left + mascotRect.Right) / 2,
            (mascotRect.Top + mascotRect.Bottom) / 2), FlaUI.Core.Input.MouseButton.Right);
        var menu = WaitFor("Show Main Window", byName: true).Parent;
        var menuBounds = menu.BoundingRectangle;
        Assert.IsTrue(work.Contains(menuBounds), $"Context menu {menuBounds} outside work area {work}");
        using (var image = Capture.Rectangle(menuBounds)) image.ToFile(Path.Combine(_output, "context-menu.png"));
        File.WriteAllText(Path.Combine(_output, "corner-result.json"), JsonSerializer.Serialize(new
        {
            workArea = work, mascot = mascotRect.ToRectangle(), focus, bubble, menuBounds, passed = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [TestMethod]
    public void OversizedTip_RemainsScrollableWithinWorkArea()
    {
        if (Environment.GetEnvironmentVariable("HATCH_TEST_LONG_TIP") != "1")
            Assert.Inconclusive("Run with HATCH_TEST_LONG_TIP=1 to seed oversized content.");
        _output = Environment.GetEnvironmentVariable("HATCH_MEASUREMENTS_DIR") ?? TestSetup.DataDirectory;
        Directory.CreateDirectory(_output);
        using var process = Process.GetProcessById(TestSetup.App!.ProcessId);
        _process = process;
        ShowWindow(new IntPtr(TestSetup.MainWindow!.Properties.NativeWindowHandle.Value), 0);
        var mascot = FindMascot();
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(mascot, 2), ref info);
        bool proactive = Environment.GetEnvironmentVariable("HATCH_TEST_PROACTIVE") == "1";
        if (!proactive) ClickMascot(mascot);
        var viewport = WaitFor(proactive ? "ProactiveTip_Viewport" : "Bubble_Viewport");
        SaveImage("oversized-initial");
        Thread.Sleep(500);
        DumpTree("oversized-tree");
        var visibleBounds = viewport.BoundingRectangle;
        if (!proactive)
        {
            var owner = TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto!)
                .First(w => w.FindFirstDescendant(cf => cf.ByAutomationId("Bubble_Viewport")) != null);
            GetWindowRect(new IntPtr(owner.Properties.NativeWindowHandle.Value), out var nativeBounds);
            visibleBounds = nativeBounds.ToRectangle();
        }
        Assert.IsTrue(info.Work.ToRectangle().Contains(visibleBounds),
            $"Work area {info.Work.ToRectangle()}, visible bounds {visibleBounds}");
        Assert.IsTrue(viewport.Patterns.Scroll.IsSupported, "Oversized content must be scrollable");
        Assert.IsTrue(viewport.Patterns.Scroll.Pattern.VerticallyScrollable.Value);
        viewport.Patterns.Scroll.Pattern.SetScrollPercent(-1, 0);
        SaveImage("oversized-top");
        viewport.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
        SaveImage("oversized-bottom");
        if (!proactive)
        {
            var add = WaitFor("Bubble_AddButton");
            Assert.IsTrue(info.Work.ToRectangle().Contains(add.BoundingRectangle), "Add button must be reachable by scrolling");
        }
        File.WriteAllText(Path.Combine(_output, "oversized-result.json"), JsonSerializer.Serialize(new
        {
            workArea = info.Work.ToRectangle(), visibleBounds, automationBounds = viewport.BoundingRectangle,
            scrollPercent = viewport.Patterns.Scroll.Pattern.VerticalScrollPercent.Value,
            proactive, dpi = GetDpiForWindow(mascot), passed = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [TestMethod]
    public void MeasureMemoryCpuAndPopupInteraction()
    {
        _output = Environment.GetEnvironmentVariable("HATCH_MEASUREMENTS_DIR") ?? TestSetup.DataDirectory;
        Directory.CreateDirectory(_output);
        _process = Process.GetProcessById(TestSetup.App!.ProcessId);
        try
        {
            DumpTree("initial");
            Sample("main_open");
            var main = TestSetup.MainWindow!;
            main.FindFirstDescendant(cf => cf.ByName("_RuntimeProbe_"))!.Click();
            var focus = WaitFor("PaneFocusRow");
            focus.AsButton().Invoke();
            WaitFor("FocusMode_PauseResume");
            Sample("focus_running_main_open");
            using (var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestSetup.DataDirectory, "settings.json"))))
                Assert.AreNotEqual(JsonValueKind.Null, settings.RootElement.GetProperty("FocusTaskId").ValueKind,
                    "A newly opened focus session must be persisted before pause or rename.");
            WaitFor("FocusMode_PauseResume").AsButton().Invoke();
            Sample("focus_paused_main_open");
            SaveImage("focus");
            WaitFor("FocusMode_Exit").AsButton().Invoke();

            IntPtr mainHandle = new(main.Properties.NativeWindowHandle.Value);
            ShowWindow(mainHandle, 0);
            Sample("mascot_idle");
            IntPtr mascot = FindMascot();
            GetWindowRect(mascot, out var mascotRect);
            var monitor = MonitorFromWindow(mascot, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            Assert.IsTrue(GetMonitorInfo(monitor, ref info));
            var work = info.Work.ToRectangle();
            _checks.Add(new { displayWorkArea = work, dpi = GetDpiForWindow(mascot), monitorCount = GetSystemMetrics(80) });

            for (int i = 0; i < 50; i++)
            {
                var watch = Stopwatch.StartNew();
                ClickMascot(mascot);
                var input = WaitFor("Bubble_TaskTitleBox");
                var add = WaitFor("Bubble_AddButton");
                _checks.Add(new { iteration = i, openMs = watch.Elapsed.TotalMilliseconds, inputBounds = input.BoundingRectangle, addBounds = add.BoundingRectangle });
                Assert.IsTrue(work.Contains(input.BoundingRectangle), "Quick-add input outside work area");
                Assert.IsTrue(work.Contains(add.BoundingRectangle), "Quick-add Add button outside work area");
                if (i == 0) { Sample("quick_add_open"); SaveImage("quick-add"); }
                WaitFor("Bubble_Close").AsButton().Invoke();
                Thread.Sleep(100);
                if (i == 9 || i == 24 || i == 49) Sample($"after_{i + 1}_quick_add_cycles");
            }
            SaveImage("after-cycles");
            ShowWindow(mainHandle, 5);
            WaitFor("PaneFocusRow").AsButton().Invoke();
            ShowWindow(mainHandle, 0);
            Sample("focus_running_warm");
            WaitFor("FocusMode_PauseResume").AsButton().Invoke();
            Sample("focus_paused_warm");

            WaitFor("FocusMode_Exit").AsButton().Invoke();
            ShowWindow(mainHandle, 5);
            for (int i = 0; i < 25; i++)
            {
                WaitFor("PaneFocusRow").AsButton().Invoke();
                WaitFor("FocusMode_Exit").AsButton().Invoke();
                Thread.Sleep(100);
            }
            ShowWindow(mainHandle, 0);
            Sample("after_focus_cycles");
        }
        finally
        {
            File.WriteAllText(Path.Combine(_output, "measurements.json"), JsonSerializer.Serialize(new
            {
                measuredAt = DateTimeOffset.Now, processId = _process.Id, cpuCount = Environment.ProcessorCount,
                samples = _samples, checks = _checks
            }, new JsonSerializerOptions { WriteIndented = true }));
            _process.Dispose();
        }
    }

    private AutomationElement WaitFor(string id, bool byName = false)
    {
        var deadline = Stopwatch.StartNew();
        do
        {
            foreach (var window in TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto!))
            {
                var element = window.FindFirstDescendant(cf => byName ? cf.ByName(id) : cf.ByAutomationId(id));
                if (element != null && !element.IsOffscreen) return element;
            }
            Thread.Sleep(50);
        } while (deadline.Elapsed < TimeSpan.FromSeconds(8));
        DumpTree("missing-" + id);
        throw new InvalidOperationException("Missing visible control: " + id);
    }

    private void Sample(string state)
    {
        Thread.Sleep(2000);
        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        var memory = new List<(long Working, long Private)>();
        for (int i = 0; i < 20; i++)
        {
            Thread.Sleep(400);
            _process.Refresh();
            memory.Add((_process.WorkingSet64, _process.PrivateMemorySize64));
        }
        double oneCorePercent = (_process.TotalProcessorTime - cpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds * 100;
        _samples.Add(new { state, durationSeconds = watch.Elapsed.TotalSeconds,
            workingSetMiB = memory.Average(s => s.Working) / 1048576,
            privateMiB = memory.Average(s => s.Private) / 1048576,
            cpuOneCorePercent = oneCorePercent, cpuMachinePercent = oneCorePercent / Environment.ProcessorCount,
            handles = _process.HandleCount, threads = _process.Threads.Count });
    }

    private void SaveImage(string name)
    {
        var windows = TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto!);
        var rectangles = windows.Where(w => !w.IsOffscreen && w.BoundingRectangle.Width > 0)
            .Select(w => w.BoundingRectangle).ToList();
        foreach (var window in windows)
        foreach (var id in new[] { "FocusMode_Viewport", "ProactiveTip_Viewport" })
        {
            var viewport = window.FindFirstDescendant(cf => cf.ByAutomationId(id));
            if (viewport != null && !viewport.IsOffscreen) rectangles.Add(viewport.BoundingRectangle);
        }
        var bounds = rectangles.Aggregate(Rectangle.Union);
        using var image = Capture.Rectangle(bounds);
        image.ToFile(Path.Combine(_output, name + ".png"));
    }

    private void DumpTree(string name)
    {
        var lines = new List<string>();
        foreach (var window in TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto!))
        {
            lines.Add($"WINDOW {window.Title} {window.BoundingRectangle}");
            foreach (var element in window.FindAllDescendants())
                lines.Add($"{element.Properties.ControlType.ValueOrDefault} | {element.Properties.AutomationId.ValueOrDefault} | {element.Properties.Name.ValueOrDefault} | {element.BoundingRectangle} | offscreen={element.Properties.IsOffscreen.ValueOrDefault}");
        }
        File.WriteAllLines(Path.Combine(_output, name + ".txt"), lines);
    }

    private IntPtr FindMascot()
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != _process.Id || !IsWindowVisible(hwnd)) return true;
            GetWindowRect(hwnd, out var r);
            if (r.Right - r.Left is >= 100 and <= 160 && r.Bottom - r.Top is >= 100 and <= 160) result = hwnd;
            return true;
        }, IntPtr.Zero);
        Assert.AreNotEqual(IntPtr.Zero, result, "Mascot window not found");
        return result;
    }

    private static void ClickMascot(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        FlaUI.Core.Input.Mouse.Click(new Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
