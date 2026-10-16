using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Hatch.Tests.Infrastructure;

namespace Hatch.Tests;

[TestClass]
public sealed class PlannedWorkflowTests
{
    private static Window Main => TestSetup.MainWindow!;
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void SetUp()
    {
        TestSetup.ResetUi();
        Main.Patterns.Transform.Pattern.Resize(620, 640);
        Find("NewTask_TextBox").Patterns.Value.Pattern.SetValue(string.Empty);
        Find("Nav_Planned").Patterns.SelectionItem.Pattern.Select();
        Wait(() => Find("PlannedViewSelector").Name == "Agenda", "Agenda on navigation");
    }

    [TestCleanup]
    public void CleanUp()
    {
        if (TestContext.CurrentTestOutcome != UnitTestOutcome.Passed)
        {
            try { TaskWorkflowTests.CaptureState("failed-" + TestContext.TestName); }
            catch (Exception ex) { TestContext.WriteLine(ex.Message); }
        }
        Find("NewTask_TextBox").Patterns.Value.Pattern.SetValue(string.Empty);
        TestSetup.ResetUi();
    }

    [TestMethod]
    public void ViewMenuAndSelectedDay_DefaultFreshTasksAndResetAfterAdd()
    {
        SelectCalendar();
        SelectDay(DateTime.Today.AddDays(2));
        var first = AddTask();
        Assert.AreEqual(DateTime.Today.AddDays(2), Saved(first).GetProperty("DueDate").GetDateTimeOffset().Date);
        Assert.IsNotNull(Named(first));
        var second = AddTask();
        Assert.AreEqual(DateTime.Today.AddDays(2), Saved(second).GetProperty("DueDate").GetDateTimeOffset().Date);
        Assert.AreEqual("Calendar", Find("PlannedViewSelector").Name);
        TaskWorkflowTests.CaptureState("planned-compact");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EditedDraft_KeepsItsDateWhenBrowsing(bool importantFirst)
    {
        SelectCalendar();
        SelectDay(DateTime.Today.AddDays(3));
        var title = NewTitle();
        if (importantFirst) Find("NewTask_Important").Patterns.Toggle.Pattern.Toggle();
        else SetTitle(title);
        SelectDay(DateTime.Today.AddDays(4));
        SetTitle(title);
        Find("NewTask_AddButton").Patterns.Invoke.Pattern.Invoke();
        var task = Saved(title);
        Assert.AreEqual(DateTime.Today.AddDays(3), task.GetProperty("DueDate").GetDateTimeOffset().Date);
        Assert.AreEqual(importantFirst, task.GetProperty("IsStarred").GetBoolean());
    }

    [TestMethod]
    public void ExplicitDueDate_WinsOverBrowsingAndResetsForNextFreshDraft()
    {
        SelectCalendar();
        SelectDay(DateTime.Today);
        // Mid-month day numbers are unique within the native six-week view.
        var explicitDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day == 15 ? 16 : 15);
        var browsedDate = DateTime.Today.AddDays(5);
        if (browsedDate == explicitDate) browsedDate = browsedDate.AddDays(1);
        Find("NewTask_DueDate").Patterns.Invoke.Pattern.Invoke();
        SelectNativeDay("NewTask_Calendar", explicitDate, useHelpText: false);
        SelectDay(browsedDate);
        var explicitTask = AddTask();
        Assert.AreEqual(explicitDate, Saved(explicitTask).GetProperty("DueDate").GetDateTimeOffset().Date);
        var freshTask = AddTask();
        Assert.AreEqual(browsedDate, Saved(freshTask).GetProperty("DueDate").GetDateTimeOffset().Date);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompletionAndUndo_RestoreTaskAndKeepBarClearOfComposer(bool top)
    {
        SetComposerPosition(top);
        SelectCalendar();
        SelectDay(DateTime.Today.AddDays(6));
        var title = AddTask();
        var row = Named(title).Parent;
        while (row.FindFirstDescendant(cf => cf.ByAutomationId("Task_CheckBox")) is null) row = row.Parent;
        row.FindFirstDescendant(cf => cf.ByAutomationId("Task_CheckBox"))!.Patterns.Toggle.Pattern.Toggle();
        Wait(() => Saved(title).GetProperty("IsCompleted").GetBoolean(), "saved completion");
        var undoBar = Find("UndoInfoBar").BoundingRectangle;
        var input = Find("NewTask_TextBox").BoundingRectangle;
        double composerTop = input.Top - 6;
        double composerBottom = input.Bottom + 6;
        Assert.IsTrue(undoBar.Bottom <= composerTop + 1 || undoBar.Top >= composerBottom - 1,
            "Undo must not overlap either composer position.");
        if (!top) Assert.AreEqual(4, composerTop - undoBar.Bottom, 2, "Bottom Undo gap.");
        Find("UndoButton").Patterns.Invoke.Pattern.Invoke();
        Wait(() => !Saved(title).GetProperty("IsCompleted").GetBoolean(), "saved Undo");
        Assert.IsNotNull(Named(title));
    }

    [TestMethod]
    public void Recurrence_CreatesOneOccurrenceAndUndoRemovesIt()
    {
        SelectCalendar();
        var before = Tasks().Count(task => task.GetProperty("Title").GetString() == "Planned daily" && !Deleted(task));
        Assert.AreEqual(1, before, "Calendar browsing must not project recurrences.");
        var original = Tasks().Single(task => task.GetProperty("Title").GetString() == "Planned daily" && !Deleted(task));
        var row = Named("Planned daily").Parent;
        while (row.FindFirstDescendant(cf => cf.ByAutomationId("Task_CheckBox")) is null) row = row.Parent;
        var elapsed = Stopwatch.StartNew();
        row.FindFirstDescendant(cf => cf.ByAutomationId("Task_CheckBox"))!.Patterns.Toggle.Pattern.Toggle();
        Wait(() => Tasks().Count(task => task.GetProperty("Title").GetString() == "Planned daily" && !Deleted(task)) == 2,
            "one spawned occurrence");
        TestContext.WriteLine($"Spawn persisted after {elapsed.ElapsedMilliseconds}ms.");
        var spawned = Tasks().Single(task => task.GetProperty("Title").GetString() == "Planned daily"
            && !Deleted(task) && task.GetProperty("Id").GetString() != original.GetProperty("Id").GetString());
        Assert.AreEqual(DateTime.Today.AddDays(1), spawned.GetProperty("DueDate").GetDateTimeOffset().Date);
        var undo = Find("UndoButton");
        TestContext.WriteLine($"Undo found after {elapsed.ElapsedMilliseconds}ms; enabled={undo.IsEnabled}.");
        undo.Patterns.Invoke.Pattern.Invoke();
        TestContext.WriteLine($"Undo invoked after {elapsed.ElapsedMilliseconds}ms.");
        Wait(() => Tasks().Count(task => task.GetProperty("Title").GetString() == "Planned daily" && !Deleted(task)) == 1,
            "spawn removed by Undo");
        Assert.IsFalse(Saved("Planned daily").GetProperty("IsCompleted").GetBoolean());
        Assert.IsNotNull(Named("Planned daily"));
    }

    [TestMethod]
    public void OverdueShortcut_ReturnsToAgendaWithoutChangingDates()
    {
        SelectCalendar();
        var before = Saved("Planned overdue").GetProperty("DueDate").GetString();
        int overdueCount = Tasks().Count(task => !Deleted(task) && !task.GetProperty("IsCompleted").GetBoolean()
            && task.GetProperty("DueDate").GetDateTimeOffset().Date < DateTime.Today);
        Assert.AreEqual($"Overdue · {overdueCount}", Find("PlannedOverdueButton").Name);
        Find("PlannedOverdueButton").Patterns.Invoke.Pattern.Invoke();
        Wait(() => Find("PlannedViewSelector").Name == "Agenda", "Overdue selects Agenda");
        Assert.IsNotNull(Named("Planned overdue"));
        Assert.AreEqual(before, Saved("Planned overdue").GetProperty("DueDate").GetString());
    }

    [TestMethod]
    [DataRow(620)]
    [DataRow(1100)]
    public void CalendarLayout_ExposesDayCountsAndKeepsControlsWithinWindow(int width)
    {
        Main.Patterns.Transform.Pattern.Resize(width, 760);
        SelectCalendar();
        if (width == 620) Find("PlannedDateButton").Patterns.Invoke.Pattern.Invoke();
        var day = CalendarDay("PlannedCalendar", DateTime.Today.AddDays(2), true);
        int count = Tasks().Count(task => !Deleted(task) && !task.GetProperty("IsCompleted").GetBoolean()
            && task.GetProperty("DueDate").GetDateTimeOffset().Date == DateTime.Today.AddDays(2));
        StringAssert.Contains(day.Properties.HelpText.Value, count == 1 ? ": 1 task due." : $": {count} tasks due.");
        if (width == 620) TaskWorkflowTests.CaptureState("planned-calendar-flyout");
        SelectNativeDay("PlannedCalendar", DateTime.Today.AddDays(2), true);
        Assert.IsNotNull(Named("Planned future"));
        foreach (var id in new[] { "PlannedViewSelector", "PlannedTodayButton", "PlannedOverdueButton", "NewTask_TextBox", "NewTask_AddButton" })
        {
            var rect = Find(id).BoundingRectangle;
            Assert.IsTrue(rect.Width > 0 && rect.Height > 0 && Main.BoundingRectangle.Contains(rect), $"{id} must fit.");
        }
        SetTitle("Text scaling check");
        double fontSize = Convert.ToDouble(Find("NewTask_TextBox").Patterns.Text.Pattern.DocumentRange
            .GetAttributeValue(TestSetup.Auto!.TextAttributeLibrary.FontSize), CultureInfo.InvariantCulture);
        Assert.IsTrue(fontSize > 0, "Native text formatting must be available for scaling evidence.");
        var folder = Environment.GetEnvironmentVariable("HATCH_MEASUREMENTS_DIR") ?? TestSetup.DataDirectory;
        File.WriteAllText(Path.Combine(folder, "text-metrics-" + width + ".json"), JsonSerializer.Serialize(new { FontSize = fontSize }));
        Thread.Sleep(750); // Allow native list entrance animations to settle for visual review.
        TaskWorkflowTests.CaptureState("planned-layout-" + width);
    }

    private static void SelectCalendar()
    {
        Find("PlannedViewSelector").Patterns.Invoke.Pattern.Invoke();
        Find("PlannedCalendarItem").Patterns.Toggle.Pattern.Toggle();
        Wait(() => Find("PlannedViewSelector").Name == "Calendar", "Calendar selection");
    }

    private static void SelectDay(DateTime date)
    {
        Find("PlannedDateButton").Patterns.Invoke.Pattern.Invoke();
        SelectNativeDay("PlannedCalendar", date, true);
        Wait(() => Find("PlannedDateButton").Name == date.ToString("D", CultureInfo.CurrentCulture), "selected day header");
    }

    private static AutomationElement CalendarDay(string calendarId, DateTime date, bool useHelpText)
    {
        var calendar = Find(calendarId);
        return Visible(() => calendar.FindAllDescendants().FirstOrDefault(day => useHelpText
            ? day.Properties.HelpText.ValueOrDefault?.StartsWith(date.ToString("D", CultureInfo.CurrentCulture) + ":", StringComparison.Ordinal) == true
            : day.Properties.ClassName.ValueOrDefault == "CalendarViewDayItem"
                && day.Properties.Name.ValueOrDefault == date.Day.ToString(CultureInfo.CurrentCulture)), "calendar day " + date.ToString("d"));
    }

    private static void SelectNativeDay(string calendarId, DateTime date, bool useHelpText)
        => CalendarDay(calendarId, date, useHelpText).Patterns.SelectionItem.Pattern.Select();

    private static void SetComposerPosition(bool top)
    {
        Named("Settings").Patterns.SelectionItem.Pattern.Select();
        var position = Visible(() => Main.FindFirstDescendant(cf => cf.ByAutomationId("Settings_TaskInputPosition")), "task input position");
        position.AsComboBox().Select(top ? 1 : 0);
        Find("Nav_Planned").Patterns.SelectionItem.Pattern.Select();
    }

    private static string NewTitle() => "Planned check " + Guid.NewGuid().ToString("N")[..8];
    private static void SetTitle(string title)
    {
        Find("NewTask_TextBox").Patterns.Value.Pattern.SetValue(title);
        Wait(() => Find("NewTask_TextBox").Patterns.Value.Pattern.Value == title, "task title input");
    }

    private static string AddTask()
    {
        var title = NewTitle();
        SetTitle(title);
        Find("NewTask_AddButton").Patterns.Invoke.Pattern.Invoke();
        Saved(title);
        return title;
    }

    private static bool Deleted(JsonElement task) => task.TryGetProperty("IsDeleted", out var value) && value.GetBoolean();
    private static JsonElement[] Tasks()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestSetup.DataDirectory, "tasks.json")));
        return json.RootElement.GetProperty("Tasks").EnumerateArray().Select(task => task.Clone()).ToArray();
    }

    private static JsonElement Saved(string title)
    {
        JsonElement? saved = null;
        Wait(() => (saved = Tasks().FirstOrDefault(task => task.GetProperty("Title").GetString() == title && !Deleted(task))).Value.ValueKind == JsonValueKind.Object,
            "saved task " + title);
        return saved!.Value;
    }

    private static AutomationElement Find(string id) => Visible(() => TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto!)
        .Select(window => window.FindFirstDescendant(cf => cf.ByAutomationId(id))).FirstOrDefault(element => element is not null && !element.IsOffscreen), id);
    private static AutomationElement Named(string name) => Visible(() => Main.FindFirstDescendant(cf => cf.ByName(name)), name);
    private static AutomationElement Visible(Func<AutomationElement?> lookup, string description)
    {
        AutomationElement? found = null;
        Wait(() => (found = lookup()) is not null && !found.IsOffscreen, description);
        return found!;
    }

    private static void Wait(Func<bool> predicate, string description)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        do
        {
            try { if (predicate()) return; }
            catch (IOException) { }
            Thread.Sleep(60);
        } while (DateTime.UtcNow < until);
        Assert.Fail("Timed out: " + description);
    }
}
