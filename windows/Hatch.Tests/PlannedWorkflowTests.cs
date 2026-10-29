using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
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
        Wait(() => Find("PlannedCalendarItem").Patterns.SelectionItem.Pattern.IsSelected, "Calendar on navigation");
        AssertVisiblePlannedContent(agenda: false);
    }

    [TestCleanup]
    public void CleanUp()
    {
        if (TestContext.CurrentTestOutcome != UnitTestOutcome.Passed)
        {
            try { TaskWorkflowTests.CaptureState("failed-" + TestContext.TestName); }
            catch (Exception ex) { TestContext.WriteLine(ex.Message); }
        }
        TestSetup.ResetUi();
        var composer = Main.FindFirstDescendant(cf => cf.ByAutomationId("NewTask_TextBox"));
        if (composer is { IsOffscreen: false } && composer.Patterns.Value.IsSupported)
            composer.Patterns.Value.Pattern.SetValue(string.Empty);
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
        Assert.IsTrue(Find("PlannedCalendarItem").Patterns.SelectionItem.Pattern.IsSelected);
        TaskWorkflowTests.CaptureState("planned-compact");
    }

    [TestMethod]
    public void ViewSelector_ActiveIndicatorSurvivesRepeatedChanges()
    {
        var agenda = Find("PlannedAgendaItem");
        var calendarItem = Find("PlannedCalendarItem");
        for (int i = 0; i < 50; i++)
        {
            SelectView(agenda, calendarItem);
            SelectView(calendarItem, agenda);
        }
        AssertSelectorState("PlannedCalendarItem", "PlannedAgendaItem");
    }

    [TestMethod]
    public void ViewSelector_CalendarIsDefaultAndAppearsFirst()
    {
        var calendar = Find("PlannedCalendarItem");
        var agenda = Find("PlannedAgendaItem");
        Assert.IsTrue(calendar.Patterns.SelectionItem.Pattern.IsSelected);
        Assert.IsFalse(agenda.Patterns.SelectionItem.Pattern.IsSelected);
        Assert.IsTrue(calendar.BoundingRectangle.Left < agenda.BoundingRectangle.Left,
            "The default Calendar selector item should be left of Agenda.");
    }

    [TestMethod]
    public void ViewSelector_VisibleContentTracksTheSelectedItem()
    {
        var agenda = Find("PlannedAgendaItem");
        var calendarItem = Find("PlannedCalendarItem");
        TestContext.WriteLine($"Selector Invoke pattern: Agenda={agenda.Patterns.Invoke.IsSupported}, Calendar={calendarItem.Patterns.Invoke.IsSupported}.");
        for (int i = 0; i < 10; i++)
        {
            ActivateView(agenda);
            Wait(() => agenda.Patterns.SelectionItem.Pattern.IsSelected, "Agenda selected after activation");
            AssertVisiblePlannedContent(agenda: true);
            ActivateView(calendarItem);
            Wait(() => calendarItem.Patterns.SelectionItem.Pattern.IsSelected, "Calendar selected after activation");
            AssertVisiblePlannedContent(agenda: false);
        }
    }

    [TestMethod]
    public void ViewSelector_HoverAndPaddingClicksPreserveActiveItem()
    {
        SelectCalendar();
        var calendarItem = Find("PlannedCalendarItem");
        var calendarBounds = calendarItem.BoundingRectangle;
        Mouse.MoveTo(Center(calendarBounds));
        Thread.Sleep(150);
        AssertSelectorState("PlannedCalendarItem", "PlannedAgendaItem");
        TaskWorkflowTests.CaptureState("planned-selector-calendar-active-hover");

        Mouse.MoveTo(Center(Find("PlannedDay_Count").BoundingRectangle));
        Thread.Sleep(150);
        AssertSelectorState("PlannedCalendarItem", "PlannedAgendaItem");
        TaskWorkflowTests.CaptureState("planned-selector-calendar-active-after-hover");

        var agendaHit = new Point((int)Find("PlannedAgendaItem").BoundingRectangle.Right - 4,
            (int)Find("PlannedAgendaItem").BoundingRectangle.Top + (int)(Find("PlannedAgendaItem").BoundingRectangle.Height / 2));
        Mouse.MoveTo(agendaHit);
        Thread.Sleep(150);
        TaskWorkflowTests.CaptureState("planned-selector-agenda-unselected-hover");
        if (!TestSetup.IsMainWindowForeground)
            Assert.Inconclusive("This desktop has no foreground window; Windows blocks physical pointer clicks.");
        Mouse.Click(agendaHit);
        AssertSelectorState("PlannedAgendaItem", "PlannedCalendarItem");
        TaskWorkflowTests.CaptureState("planned-selector-agenda-active-hover");

        var calendarHitBounds = calendarItem.BoundingRectangle;
        var calendarHit = new Point((int)calendarHitBounds.Right - 4,
            (int)calendarHitBounds.Top + (int)(calendarHitBounds.Height / 2));
        Mouse.MoveTo(calendarHit);
        Thread.Sleep(150);
        Mouse.Click(calendarHit);
        AssertSelectorState("PlannedCalendarItem", "PlannedAgendaItem");
        TaskWorkflowTests.CaptureState("planned-selector-calendar-padding-click");
    }

    [TestMethod]
    public void AgendaTaskRow_SelectsTaskAndItsStarActionKeepsDetailsOnThatTask()
    {
        SelectAgenda();
        var agendaList = Find("GroupedListView");
        if (agendaList.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
            agendaList.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
        var title = "Planned future";
        var selectableTaskRow = Named(title);
        while (!selectableTaskRow.Patterns.SelectionItem.IsSupported)
            selectableTaskRow = selectableTaskRow.Parent
                ?? throw new AssertFailedException("The Agenda task row must expose selection.");
        selectableTaskRow.Patterns.SelectionItem.Pattern.Select();
        Wait(() => Find("PaneTitleBox").AsTextBox().Text == title, "Agenda task details");

        var taskRow = Named(title);
        AutomationElement? star = null;
        while (star == null && taskRow.Parent is { } parent)
        {
            taskRow = parent;
            star = taskRow.FindFirstDescendant(cf => cf.ByAutomationId("Task_StarButton"));
        }
        Assert.IsNotNull(star, "The Agenda task item must have its own star action.");

        bool wasStarred = Starred(Saved(title));
        star!.Patterns.Invoke.Pattern.Invoke();
        Wait(() => Starred(Saved(title)) != wasStarred, "Agenda task star persistence");
        Assert.AreEqual(title, Find("PaneTitleBox").AsTextBox().Text,
            "Using a task action must keep details on that task.");
        TaskWorkflowTests.CaptureState("planned-agenda-task-details");
    }

    [TestMethod]
    public void TaskMetadata_NotesIndicatorIsConsistentAcrossPlannedViews()
    {
        Main.Patterns.Transform.Pattern.Resize(1100, 760);
        SelectCalendar();
        SelectNativeDay("PlannedCalendar", DateTime.Today.AddDays(2), useHelpText: true);
        AssertTaskHasNotesIndicator("Planned future");
        TaskWorkflowTests.CaptureState("planned-calendar-notes-indicator");

        SelectAgenda();
        var agenda = Find("GroupedListView");
        if (agenda.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
            agenda.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
        AssertTaskHasNotesIndicator("Planned future");
        TaskWorkflowTests.CaptureState("planned-agenda-notes-indicator");
    }

    [TestMethod]
    public void PlannedControls_ExposeAccessibleNamesAndActions()
    {
        var agenda = Find("PlannedAgendaItem");
        var calendar = Find("PlannedCalendarItem");
        Assert.AreEqual("Agenda", agenda.Name);
        Assert.AreEqual("Calendar", calendar.Name);
        Assert.IsTrue(agenda.Properties.IsKeyboardFocusable.ValueOrDefault);
        Assert.IsTrue(calendar.Properties.IsKeyboardFocusable.ValueOrDefault);
        Assert.IsTrue(agenda.Patterns.SelectionItem.IsSupported);
        Assert.IsTrue(calendar.Patterns.SelectionItem.IsSupported);

        var dateButton = Find("PlannedDateButton");
        StringAssert.Contains(dateButton.Properties.HelpText.ValueOrDefault ?? string.Empty, "calendar events and tasks");
        Assert.IsFalse(string.IsNullOrWhiteSpace(dateButton.Name), "The selected date needs an accessible name.");
        Assert.IsTrue(dateButton.Properties.IsKeyboardFocusable.ValueOrDefault);
        Assert.IsTrue(dateButton.Patterns.Invoke.IsSupported);

        var today = Find("PlannedTodayButton");
        Assert.AreEqual("Today", today.Name);
        Assert.IsTrue(today.Properties.IsKeyboardFocusable.ValueOrDefault);
        Assert.IsTrue(today.Patterns.Invoke.IsSupported);

        dateButton.Patterns.Invoke.Pattern.Invoke();
        SelectNativeDay("PlannedCalendar", DateTime.Today.AddDays(2), useHelpText: true);
        var futureRow = FindTaskRow("Planned future");
        var completion = futureRow.FindFirstDescendant(cf => cf.ByAutomationId("Task_CheckBox"));
        Assert.IsNotNull(completion);
        StringAssert.Contains(completion!.Name, "Planned future");
        Assert.IsTrue(completion.Properties.IsKeyboardFocusable.ValueOrDefault);
        Assert.IsTrue(completion.Patterns.Toggle.IsSupported);

        var dateChip = futureRow.FindFirstDescendant(cf => cf.ByAutomationId("Task_DateChipButton"));
        Assert.IsNotNull(dateChip);
        Assert.IsFalse(string.IsNullOrWhiteSpace(dateChip!.Name), "The due-date action needs an accessible name.");
        StringAssert.Contains(dateChip.Properties.HelpText.ValueOrDefault ?? string.Empty, "due date");
        Assert.IsTrue(dateChip.Properties.IsKeyboardFocusable.ValueOrDefault);
        Assert.IsTrue(dateChip.Patterns.Invoke.IsSupported);

        var star = futureRow.FindFirstDescendant(cf => cf.ByAutomationId("Task_StarButton"));
        Assert.IsNotNull(star);
        Assert.IsFalse(string.IsNullOrWhiteSpace(star!.Name), "The star action needs an accessible name.");
        Assert.IsTrue(star.Properties.IsKeyboardFocusable.ValueOrDefault);
        Assert.IsTrue(star.Patterns.Invoke.IsSupported);

        var overflow = futureRow.FindFirstDescendant(cf => cf.ByAutomationId("Task_OverflowButton"));
        Assert.IsNotNull(overflow);
        Assert.AreEqual("More options", overflow!.Name);
        Assert.IsTrue(overflow.Properties.IsKeyboardFocusable.ValueOrDefault);
        Assert.IsTrue(overflow.Patterns.Invoke.IsSupported);

        today.Focus();
        Assert.IsTrue(today.Properties.HasKeyboardFocus.ValueOrDefault, "Today must receive keyboard focus.");
        today.Patterns.Invoke.Pattern.Invoke();
        Wait(() => Find("PlannedDateButton").Name == DateTime.Today.ToString("D", CultureInfo.CurrentCulture),
            "Today action resets the selected date");
    }

    [TestMethod]
    public void MeetingDetails_OpenFromAgendaAndCalendarRows()
    {
        SelectAgenda();
        var agenda = Find("GroupedListView");
        if (agenda.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
            agenda.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
        var agendaMeeting = Find("Agenda_MeetingDetails");
        agendaMeeting.Patterns.Invoke.Pattern.Invoke();
        var agendaJoin = Find("Agenda_DetailJoin");
        StringAssert.Contains(agendaJoin.Name, "Planned test meeting");
        Assert.IsTrue(Find("Agenda_OpenEvent").IsEnabled);
        TaskWorkflowTests.CaptureState("planned-agenda-meeting-details");

        TestSetup.Restart();
        Main.Patterns.Transform.Pattern.Resize(620, 640);
        Find("Nav_Planned").Patterns.SelectionItem.Pattern.Select();
        Wait(() => Find("PlannedCalendarItem").Patterns.SelectionItem.Pattern.IsSelected,
            "Calendar after reopening the test app");
        SelectCalendar();
        SelectDay(DateTime.Today.AddDays(2));
        Find("CalendarEvent_planned-test-meeting").Patterns.Invoke.Pattern.Invoke();
        var calendarJoin = Find("Calendar_DetailJoin");
        StringAssert.Contains(calendarJoin.Name, "Planned test meeting");
        Assert.IsTrue(Find("Calendar_OpenEvent").IsEnabled);
        TaskWorkflowTests.CaptureState("planned-calendar-meeting-details");
        TestSetup.Restart();
    }

    [TestMethod]
    public void ReturningFromSettingsToAllTasksHidesTheCachedAgendaList()
    {
        SelectAgenda();
        Find("GroupedListView");

        Named("Settings").Patterns.SelectionItem.Pattern.Select();
        Find("Settings_ContentScroll");
        var allTasks = Find("Nav_AllTasks");
        allTasks.Patterns.SelectionItem.Pattern.Select();
        Wait(() => allTasks.Patterns.SelectionItem.Pattern.IsSelected, "All Tasks navigation selection");

        Find("FlatListView");
        var groupedList = Main.FindFirstDescendant(cf => cf.ByAutomationId("GroupedListView"));
        Assert.IsTrue(groupedList is null || groupedList.IsOffscreen,
            "The cached Planned Agenda list must be hidden on All Tasks.");
        Named("Planned today");

        Find("Nav_Planned").Patterns.SelectionItem.Pattern.Select();
        Wait(() => Find("PlannedCalendarItem").Patterns.SelectionItem.Pattern.IsSelected,
            "Calendar selected when returning to Planned after Settings and All Tasks");
        AssertVisiblePlannedContent(agenda: false);
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
        var checkbox = row.FindFirstDescendant(cf => cf.ByAutomationId("Task_CheckBox"))!;
        checkbox.Patterns.Toggle.Pattern.Toggle();
        Wait(() => checkbox.Patterns.Toggle.Pattern.ToggleState == FlaUI.Core.Definitions.ToggleState.On,
            "visible completion state");
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
        Wait(() => Find("PlannedAgendaItem").Patterns.SelectionItem.Pattern.IsSelected, "Overdue selects Agenda");
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
        foreach (var id in new[] { "PlannedAgendaItem", "PlannedCalendarItem", "PlannedTodayButton", "PlannedOverdueButton", "NewTask_TextBox", "NewTask_AddButton" })
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
        Find("PlannedCalendarItem").Patterns.SelectionItem.Pattern.Select();
        Wait(() => Find("PlannedCalendarItem").Patterns.SelectionItem.Pattern.IsSelected, "Calendar selection");
        AssertVisiblePlannedContent(agenda: false);
        Thread.Sleep(200);
    }

    private static void SelectAgenda() => SelectView("PlannedAgendaItem", "PlannedCalendarItem");

    private static void SelectView(string selectedId, string unselectedId)
        => SelectView(Find(selectedId), Find(unselectedId));

    private static void SelectView(AutomationElement selected, AutomationElement unselected)
    {
        selected.Patterns.SelectionItem.Pattern.Select();
        Wait(() => selected.Patterns.SelectionItem.Pattern.IsSelected
            && !unselected.Patterns.SelectionItem.Pattern.IsSelected,
            selected.AutomationId + " selected exclusively");
    }

    private static void ActivateView(AutomationElement item)
    {
        if (item.Patterns.Invoke.IsSupported) item.Patterns.Invoke.Pattern.Invoke();
        else item.Patterns.SelectionItem.Pattern.Select();
    }

    private static void AssertSelectorState(string selectedId, string unselectedId)
    {
        Assert.IsTrue(Find(selectedId).Patterns.SelectionItem.Pattern.IsSelected, selectedId + " must be selected.");
        Assert.IsFalse(Find(unselectedId).Patterns.SelectionItem.Pattern.IsSelected, unselectedId + " must be unselected.");
    }

    private static void AssertVisiblePlannedContent(bool agenda)
    {
        bool IsVisible(string id)
        {
            var element = Main.FindFirstDescendant(cf => cf.ByAutomationId(id));
            return element is not null && !element.IsOffscreen;
        }

        Wait(() => IsVisible("GroupedListView") == agenda && IsVisible("PlannedDayList") == !agenda,
            $"visible Planned content to match {(agenda ? "Agenda" : "Calendar")}");
    }

    private static Point Center(Rectangle bounds) => new((int)(bounds.Left + bounds.Width / 2),
        (int)(bounds.Top + bounds.Height / 2));

    private static void SelectDay(DateTime date)
    {
        var dateButton = Find("PlannedDateButton");
        if (!TestSetup.IsMainWindowForeground)
            Assert.Inconclusive("This desktop has no foreground window; Windows blocks physical pointer clicks.");
        Mouse.Click(Center(dateButton.BoundingRectangle));
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

    private static void AssertTaskHasNotesIndicator(string title)
    {
        var task = FindTaskRow(title);
        var notes = task.FindFirstDescendant(cf => cf.ByName("Has notes"));
        Assert.IsNotNull(notes, $"{title} should show the same notes indicator in both Planned views.");
        Assert.IsFalse(notes!.IsOffscreen, $"{title}'s notes indicator should be visible.");
    }

    private static AutomationElement FindTaskRow(string title)
    {
        var task = Named(title);
        while (task.FindFirstDescendant(cf => cf.ByAutomationId("Task_CheckBox")) is null)
            task = task.Parent ?? throw new AssertFailedException($"Could not find the task row for {title}.");
        return task;
    }

    private static void SetComposerPosition(bool top)
    {
        Named("Settings").Patterns.SelectionItem.Pattern.Select();
        SelectSettingsCategory("Quick Add");
        var position = FindAny("Settings_TaskInputPosition");
        if (position.Patterns.ScrollItem.IsSupported)
            position.Patterns.ScrollItem.Pattern.ScrollIntoView();
        position.AsComboBox().Select(top ? 1 : 0);
        Find("Nav_Planned").Patterns.SelectionItem.Pattern.Select();
    }

    private static void SelectSettingsCategory(string name)
    {
        var originalBounds = Main.BoundingRectangle;
        Main.Patterns.Transform.Pattern.Resize(1400, originalBounds.Height);
        try
        {
            var item = Visible(() => Main.FindAllDescendants(cf => cf.ByName(name)
                .And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.TabItem)))
                .FirstOrDefault(candidate => candidate.Patterns.SelectionItem.IsSupported), "Settings category " + name);
            item.Patterns.SelectionItem.Pattern.Select();
            Wait(() => item.Patterns.SelectionItem.Pattern.IsSelected, "selected Settings category " + name);
            Wait(() => Main.FindFirstDescendant(cf => cf.ByAutomationId("Settings_TaskInputPosition")) is not null,
                "Quick Add category content");
        }
        finally
        {
            Main.Patterns.Transform.Pattern.Resize(originalBounds.Width, originalBounds.Height);
        }
    }

    private static AutomationElement FindAny(string id)
    {
        AutomationElement? element = null;
        Wait(() => (element = Main.FindFirstDescendant(cf => cf.ByAutomationId(id))) is not null, id);
        return element!;
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
    private static bool Starred(JsonElement task) => task.TryGetProperty("IsStarred", out var value) && value.GetBoolean();
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

    private static AutomationElement Find(string id) => Visible(() =>
    {
        var condition = TestSetup.Auto!.ConditionFactory.ByAutomationId(id);
        var mainWindowElement = Main.FindFirstDescendant(condition);
        if (mainWindowElement is { IsOffscreen: false }) return mainWindowElement;

        var mainHandle = Main.Properties.NativeWindowHandle.Value;
        foreach (var window in TestSetup.App!.GetAllTopLevelWindows(TestSetup.Auto))
        {
            if (window.Properties.NativeWindowHandle.Value == mainHandle) continue;
            try
            {
                var element = window.FindFirstDescendant(condition);
                if (element is { IsOffscreen: false }) return element;
            }
            catch (COMException ex) when (IsUiAutomationTimeout(ex)) { }
        }
        return mainWindowElement;
    }, id);
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
            catch (COMException ex) when (IsUiAutomationTimeout(ex)) { }
            Thread.Sleep(60);
        } while (DateTime.UtcNow < until);
        Assert.Fail("Timed out: " + description);
    }

    private static bool IsUiAutomationTimeout(COMException ex) => ex.HResult == unchecked((int)0x80131505);
}
