using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.Capturing;
using Hatch.Tests.Infrastructure;

namespace Hatch.Tests;

[TestClass]
public sealed class CompanionLayoutTests
{
    [TestMethod]
    public void SettingsAndTipContent_KeepTheirColumnsAligned()
    {
        var main = TestSetup.MainWindow!;
        var settingsItem = main.FindFirstDescendant(cf => cf.ByName("Settings"));
        Assert.IsNotNull(settingsItem, "Settings navigation item must be reachable.");
        settingsItem.Click();

        var sound = WaitFor(main, "Settings_MascotSound");
        var tipTime = WaitFor(main, "Settings_TipTime");
        if (sound.Patterns.ScrollItem.IsSupported)
            sound.Patterns.ScrollItem.Pattern.ScrollIntoView();
        Thread.Sleep(600);
        Assert.IsFalse(sound.IsOffscreen, "Mascot sound choice must be visible after scrolling to it.");
        Assert.IsTrue(sound.BoundingRectangle.Width >= 140, "Sound choice needs room for its labels.");
        Assert.IsTrue(tipTime.BoundingRectangle.Width >= 140, "Time window choice needs room for its labels.");

        var output = Environment.GetEnvironmentVariable("HATCH_MEASUREMENTS_DIR") ?? TestSetup.DataDirectory;
        Directory.CreateDirectory(output);
        using (var image = Capture.Rectangle(main.BoundingRectangle))
            image.ToFile(Path.Combine(output, "companion-settings.png"));
        var quietedTopics = WaitFor(main, "Settings_QuietedTopics");
        if (quietedTopics.Patterns.ScrollItem.IsSupported)
            quietedTopics.Patterns.ScrollItem.Pattern.ScrollIntoView();
        for (var parent = quietedTopics.Parent; parent != null; parent = parent.Parent)
        {
            if (!parent.Patterns.Scroll.IsSupported || !parent.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
                continue;
            var current = parent.Patterns.Scroll.Pattern.VerticalScrollPercent.Value;
            parent.Patterns.Scroll.Pattern.SetScrollPercent(-1, Math.Min(100, current + 8));
            break;
        }
        Thread.Sleep(300);
        Assert.IsFalse(quietedTopics.IsOffscreen, "Quieted topic controls must be reachable.");
        using (var image = Capture.Rectangle(main.BoundingRectangle))
            image.ToFile(Path.Combine(output, "companion-quieted-topics.png"));

        // The title and message share the same left edge, matching TeachingTip content.
        ShowWindow(new IntPtr(main.Properties.NativeWindowHandle.Value), 0);
        Thread.Sleep(300);
        var mascot = TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto!)
            .First(w => w.BoundingRectangle.Width is >= 100 and <= 160);
        var mascotBounds = mascot.BoundingRectangle;
        FlaUI.Core.Input.Mouse.Click(new Point(mascotBounds.Left + mascotBounds.Width / 2,
            mascotBounds.Top + mascotBounds.Height / 2));
        var bubble = WaitForAnyWindow("Bubble_Viewport");
        var title = WaitForAnyWindow("Bubble_TipTitle");
        var message = WaitForAnyWindow("Bubble_TipMessage");
        Assert.IsTrue(Math.Abs(title.BoundingRectangle.Left - message.BoundingRectangle.Left) <= 2,
            $"Title {title.BoundingRectangle} and message {message.BoundingRectangle} must align.");
        using (var image = Capture.Rectangle(bubble.BoundingRectangle))
            image.ToFile(Path.Combine(output, "companion-quick-add.png"));
    }

    private static FlaUI.Core.AutomationElements.AutomationElement WaitFor(
        FlaUI.Core.AutomationElements.Window window, string id)
    {
        for (var i = 0; i < 80; i++)
        {
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(id));
            if (element != null) return element;
            Thread.Sleep(100);
        }
        Assert.Fail($"Missing {id}");
        throw new InvalidOperationException();
    }

    private static FlaUI.Core.AutomationElements.AutomationElement WaitForAnyWindow(string id)
    {
        for (var i = 0; i < 80; i++)
        {
            foreach (var window in TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto!))
            {
                var element = window.FindFirstDescendant(cf => cf.ByAutomationId(id));
                if (element != null && !element.IsOffscreen) return element;
            }
            Thread.Sleep(100);
        }
        Assert.Fail($"Missing visible {id}");
        throw new InvalidOperationException();
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
