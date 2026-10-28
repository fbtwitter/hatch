using System.Runtime.InteropServices;
using System.Text.Json;
using Hatch.Tests.Infrastructure;

namespace Hatch.Tests;

[TestClass]
public sealed class TaskPageSpacingTests
{
    private static Window Main => TestSetup.MainWindow!;
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(620)]
    [DataRow(1400)]
    public void TaskPages_KeepSectionGapOutsideScrollingContent(int width)
    {
        var tasksPath = Path.Combine(TestSetup.DataDirectory, "tasks.json");
        var settingsPath = Path.Combine(TestSetup.DataDirectory, "settings.json");
        var originalTasks = File.ReadAllText(tasksPath);
        var originalSettings = File.ReadAllText(settingsPath);
        var listId = Guid.NewGuid();
        var tasks = Enumerable.Range(0, 18).Select(index => (object)new
        {
            Id = Guid.NewGuid(), Title = "Spacing task " + index,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-index), IsStarred = true,
            IsInMyDay = true, MyDayDate = DateTime.Today.ToString("yyyy-MM-dd"),
            DueDate = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified), TimeSpan.Zero),
            ListId = listId
        }).Append(new { Id = Guid.NewGuid(), Title = "Spacing suggestion", CreatedAt = DateTimeOffset.UtcNow }).ToArray();
        var fixture = JsonSerializer.Serialize(new { Tasks = tasks, Lists = new[] { new { Id = listId, Name = "Runtime test list" } } });
        try
        {
            TestSetup.Restart(() => File.WriteAllText(tasksPath, fixture));
            Main.Patterns.Transform.Pattern.Resize(width, 650);
            var scale = GetDpiForWindow(new IntPtr(Main.Properties.NativeWindowHandle.Value)) / 96.0;
            foreach (bool top in new[] { false, true })
            {
                Named("Settings").Patterns.SelectionItem.Pattern.Select();
                SelectSettingsCategory("Quick Add");
                var position = FindAny("Settings_TaskInputPosition");
                if (position.Patterns.ScrollItem.IsSupported) position.Patterns.ScrollItem.Pattern.ScrollIntoView();
                for (int attempt = 0; attempt < 60 && position.IsOffscreen; attempt++) Thread.Sleep(80);
                Assert.IsFalse(position.IsOffscreen, "Task input position must be reachable after scrolling to it.");
                position.AsComboBox().Select(top ? 1 : 0);
                foreach (string navigation in new[] { "Nav_AllTasks", "Nav_Important", "Nav_MyDay", "Nav_Planned", "Runtime test list" })
                {
                    TestContext.WriteLine($"Checking width {width}, input {(top ? "top" : "bottom")}, page {navigation}");
                    if (navigation == "Runtime test list") Main.Patterns.Transform.Pattern.Resize(1400, 650);
                    var item = navigation.StartsWith("Nav_", StringComparison.Ordinal) ? Find(navigation) : Named(navigation);
                    while (!item.Patterns.SelectionItem.IsSupported) item = item.Parent;
                    item.Patterns.SelectionItem.Pattern.Select();
                    if (navigation == "Runtime test list")
                    {
                        Wait(() => Main.FindFirstDescendant(cf => cf.ByAutomationId("TaskPage_Title").And(cf.ByName(navigation))), navigation + " page");
                        Main.Patterns.Transform.Pattern.Resize(width, 650);
                    }
                    Thread.Sleep(350);
                    if (navigation == "Nav_MyDay")
                        Assert.AreEqual(16 * scale, Find("Suggestions_Button").BoundingRectangle.Top - Find("MyDay_Summary").BoundingRectangle.Bottom,
                            2, "Suggestions must have the same section gap above it.");
                    if (navigation == "Nav_Planned")
                    {
                        Find("PlannedAgendaItem").Patterns.SelectionItem.Pattern.Select();
                        Thread.Sleep(350);
                    }
                    CheckScrollGap(navigation == "Nav_Planned" ? "GroupedListView" : "FlatListView", top,
                        navigation == "Nav_MyDay" ? "Suggestions_Button" : "TaskPage_Title", scale);
                    if (navigation == "Nav_Planned")
                    {
                        Find("PlannedCalendarItem").Patterns.SelectionItem.Pattern.Select();
                        Thread.Sleep(350);
                        CheckScrollGap("PlannedDayList", false, "PlannedDay_Count", scale);
                    }
                }
                TaskWorkflowTests.CaptureState($"spacing-{width}-{(top ? "top" : "bottom")}");
            }
            Find("Nav_MyDay").Patterns.SelectionItem.Pattern.Select();
            Thread.Sleep(350);
            var suggestionsTop = Find("Suggestions_Button").BoundingRectangle.Top;
            var listTop = Find("FlatListView").BoundingRectangle.Top;
            for (int index = 0; index < 18; index++)
            {
                var scroll = Find("FlatListView").Patterns.Scroll.Pattern;
                if (scroll.VerticallyScrollable.Value) scroll.SetScrollPercent(-1, 0);
                var checkbox = Main.FindAllDescendants(cf => cf.ByAutomationId("Task_CheckBox"))
                    .First(element => !element.IsOffscreen && element.Patterns.Toggle.Pattern.ToggleState == FlaUI.Core.Definitions.ToggleState.Off);
                checkbox.Patterns.Toggle.Pattern.Toggle();
                Thread.Sleep(250);
            }
            Assert.IsTrue(Find("MyDay_Summary").Name.StartsWith("All 18 done", StringComparison.Ordinal));
            Assert.AreEqual(suggestionsTop, Find("Suggestions_Button").BoundingRectangle.Top, 2,
                "Completing the plan must not move Suggestions.");
            Assert.AreEqual(listTop, Find("FlatListView").BoundingRectangle.Top, 2,
                "Completing the plan must not move the task-list boundary.");
            TaskWorkflowTests.CaptureState("spacing-all-done-" + width);
        }
        finally
        {
            TestSetup.Restart(() =>
            {
                File.WriteAllText(tasksPath, originalTasks);
                File.WriteAllText(settingsPath, originalSettings);
            });
        }
    }

    private static void CheckScrollGap(string listId, bool top, string aboveId, double scale)
    {
        var scroller = Find(listId);
        var viewportTop = scroller.BoundingRectangle.Top;
        double aboveBottom = top ? Find("NewTask_TextBox").BoundingRectangle.Bottom + 6 * scale
            : Find(aboveId).BoundingRectangle.Bottom;
        Assert.AreEqual(16 * scale, viewportTop - aboveBottom, 2, listId + " section gap");
        foreach (double percent in new[] { 65.0, 0.0 })
        {
            scroller.Patterns.Scroll.Pattern.SetScrollPercent(-1, percent);
            Thread.Sleep(250);
            Assert.AreEqual(viewportTop, Find(listId).BoundingRectangle.Top, 2,
                "Scrolling must not consume the gap above " + listId);
        }
    }

    private static AutomationElement Find(string id) => Wait(() => Main.FindFirstDescendant(cf => cf.ByAutomationId(id)), id);
    private static AutomationElement FindAny(string id)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var element = Main.FindFirstDescendant(cf => cf.ByAutomationId(id));
            if (element is not null) return element;
            Thread.Sleep(80);
        }
        throw new AssertFailedException("Missing element: " + id);
    }

    private static AutomationElement Named(string name) => Wait(() => Main.FindFirstDescendant(cf => cf.ByName(name)), name);
    private static void SelectSettingsCategory(string name)
    {
        var item = Wait(() => Main.FindAllDescendants(cf => cf.ByName(name)
                .And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.TabItem)))
            .FirstOrDefault(candidate => candidate.Patterns.SelectionItem.IsSupported), "Settings category " + name);
        item.Patterns.SelectionItem.Pattern.Select();
        WaitUntil(() => item.Patterns.SelectionItem.Pattern.IsSelected, "selected Settings category " + name);
        WaitUntil(() => Main.FindFirstDescendant(cf => cf.ByAutomationId("Settings_TaskInputPosition")) is not null,
            "Quick Add category content");
    }

    private static AutomationElement Wait(Func<AutomationElement?> lookup, string description)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var element = lookup();
            if (element is not null && !element.IsOffscreen) return element;
            Thread.Sleep(80);
        }
        throw new AssertFailedException("Missing visible element: " + description);
    }

    private static void WaitUntil(Func<bool> predicate, string description)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (predicate()) return;
            Thread.Sleep(80);
        }
        throw new AssertFailedException("Timed out: " + description);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
