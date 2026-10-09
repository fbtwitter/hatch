using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.Capturing;
using Hatch.Tests.Infrastructure;

namespace Hatch.Tests;

[TestClass]
public sealed class CompanionLayoutTests
{
    [TestMethod]
    public void TipSurfaces_MatchWidthAndVerticalSpacing()
    {
        if (Environment.GetEnvironmentVariable("HATCH_TEST_PROACTIVE") != "1")
            Assert.Inconclusive("Run with HATCH_TEST_PROACTIVE=1 to show the proactive tip.");

        var proactiveViewport = WaitForAnyWindow("ProactiveTip_Viewport");
        var proactiveCategoryIcon = WaitForAnyWindow("ProactiveTip_CategoryIcon");
        var proactiveTitle = WaitForAnyWindow("ProactiveTip_Title");
        var proactiveMessage = WaitForAnyWindow("ProactiveTip_Message");
        var expectsAction = Environment.GetEnvironmentVariable("HATCH_TEST_NO_ACTION_TIP") != "1";
        var proactiveAction = FindVisibleAnyWindow("ProactiveTip_Action");
        Assert.AreEqual(expectsAction, proactiveAction != null, "Proactive action visibility must match the test fixture.");
        var proactiveBounds = proactiveViewport.BoundingRectangle;
        AssertContentStartsAtCategory(proactiveCategoryIcon, proactiveMessage, proactiveAction, "Proactive tip");
        AssertBalancedWhitespace(proactiveBounds.Top, proactiveBounds.Bottom,
            proactiveTitle, proactiveAction ?? proactiveMessage, "Proactive tip");

        ShowWindow(new IntPtr(TestSetup.MainWindow!.Properties.NativeWindowHandle.Value), 0);
        var mascot = TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto!)
            .First(w => w.BoundingRectangle.Width is >= 100 and <= 160);
        var mascotBounds = mascot.BoundingRectangle;
        var mascotHandle = new IntPtr(mascot.Properties.NativeWindowHandle.Value);
        var scale = GetDpiForWindow(mascotHandle) / 96.0;
        if (scale <= 0) scale = 1;
        Assert.IsTrue(Math.Abs(proactiveBounds.Width - 308 * scale) <= 1,
            $"Proactive tip should be 308 DIPs wide at scale {scale}: {proactiveBounds}.");

        void ClickMascot()
        {
            FlaUI.Core.Input.Mouse.Click(new Point(mascotBounds.Left + mascotBounds.Width / 2,
                mascotBounds.Top + mascotBounds.Height / 2));
        }

        ClickMascot();
        Thread.Sleep(250);
        if (FindVisibleAnyWindow("Bubble_Viewport") == null) ClickMascot();

        var bubbleViewport = WaitForAnyWindow("Bubble_Viewport");
        var bubbleCategoryIcon = WaitForAnyWindow("Bubble_TipCategoryIcon");
        var bubbleTitle = WaitForAnyWindow("Bubble_TipTitle");
        var bubbleMessage = WaitForAnyWindow("Bubble_TipMessage");
        var bubbleAction = FindVisibleAnyWindow("Bubble_TipAction");
        AssertContentStartsAtCategory(bubbleCategoryIcon, bubbleMessage, bubbleAction, "Quick Add tip");
        Assert.AreEqual(expectsAction, bubbleAction != null, "Quick Add action visibility must match the test fixture.");
        var bubbleViewportBounds = bubbleViewport.BoundingRectangle;
        var bubbleSurfaceTop = bubbleViewportBounds.Top + (int)Math.Round(16 * scale);
        var bubbleSurfaceBottom = WaitForAnyWindow("Bubble_OpenMainWindow").BoundingRectangle.Top -
                                  (int)Math.Round(12 * scale);
        AssertBalancedWhitespace(bubbleSurfaceTop, bubbleSurfaceBottom,
            bubbleTitle, bubbleAction ?? bubbleMessage, "Quick Add tip");
        var bubbleSurfaceWidth = bubbleViewportBounds.Width - (int)Math.Round(32 * scale);
        Assert.IsTrue(Math.Abs(bubbleSurfaceWidth - 308 * scale) <= 1,
            $"Quick Add tip should be 308 DIPs wide at scale {scale}: {bubbleSurfaceWidth}px.");
    }

    [TestMethod]
    public void SettingsAndTipContent_AlignsWithCategoryIcon()
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
        Thread.Sleep(300);
        Assert.IsFalse(quietedTopics.IsOffscreen, "Quieted topic controls must be reachable.");
        using (var image = Capture.Rectangle(main.BoundingRectangle))
            image.ToFile(Path.Combine(output, "companion-quieted-topics.png"));

        ShowWindow(new IntPtr(main.Properties.NativeWindowHandle.Value), 0);
        Thread.Sleep(300);
        var mascot = TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto!)
            .First(w => w.BoundingRectangle.Width is >= 100 and <= 160);
        var mascotBounds = mascot.BoundingRectangle;
        FlaUI.Core.Input.Mouse.Click(new Point(mascotBounds.Left + mascotBounds.Width / 2,
            mascotBounds.Top + mascotBounds.Height / 2));
        var bubble = WaitForAnyWindow("Bubble_Viewport");
        var categoryIcon = WaitForAnyWindow("Bubble_TipCategoryIcon");
        var message = WaitForAnyWindow("Bubble_TipMessage");
        var action = FindVisibleAnyWindow("Bubble_TipAction");
        using (var image = Capture.Rectangle(bubble.BoundingRectangle))
            image.ToFile(Path.Combine(output, "companion-quick-add.png"));
        AssertContentStartsAtCategory(categoryIcon, message, action, "Quick Add tip");
    }

    [TestCleanup]
    public void RestoreDefaultUi() => TestSetup.ResetUi();

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

    private static FlaUI.Core.AutomationElements.AutomationElement? FindVisibleAnyWindow(string id)
    {
        foreach (var window in TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto!))
        {
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(id));
            if (element != null && !element.IsOffscreen) return element;
        }
        return null;
    }

    private static void AssertBalancedWhitespace(
        int surfaceTop,
        int surfaceBottom,
        FlaUI.Core.AutomationElements.AutomationElement title,
        FlaUI.Core.AutomationElements.AutomationElement message,
        string name)
    {
        var topGap = title.BoundingRectangle.Top - surfaceTop;
        var bottomGap = surfaceBottom - message.BoundingRectangle.Bottom;
        Assert.IsTrue(Math.Abs(topGap - bottomGap) <= 4,
            $"{name} has unequal visible vertical gaps: top {topGap}px, bottom {bottomGap}px; " +
            $"surface top {surfaceTop}px, bottom {surfaceBottom}px.");
    }

    private static void AssertContentStartsAtCategory(
        FlaUI.Core.AutomationElements.AutomationElement categoryIcon,
        FlaUI.Core.AutomationElements.AutomationElement message,
        FlaUI.Core.AutomationElements.AutomationElement? action,
        string name)
    {
        var categoryLeft = categoryIcon.BoundingRectangle.Left;
        Assert.IsTrue(Math.Abs(categoryLeft - message.BoundingRectangle.Left) <= 2,
            $"{name} category icon {categoryIcon.BoundingRectangle} and message {message.BoundingRectangle} must share a left edge.");
        if (action != null)
            Assert.IsTrue(Math.Abs(categoryLeft - action.BoundingRectangle.Left) <= 2,
                $"{name} category icon {categoryIcon.BoundingRectangle} and action {action.BoundingRectangle} must share a left edge.");
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
