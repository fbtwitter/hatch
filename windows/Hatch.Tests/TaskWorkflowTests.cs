using System.Text.Json;
using System.Text.Json.Nodes;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Hatch.Tests.Infrastructure;

namespace Hatch.Tests;

[TestClass]
public sealed class TaskWorkflowTests
{
    private static Window Main => TestSetup.MainWindow!;
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void SetUp() => TestSetup.ResetUi();

    [TestCleanup]
    public void CleanUp()
    {
        if (TestContext.CurrentTestOutcome != UnitTestOutcome.Passed)
        {
            try { CaptureState("failed-" + TestContext.TestName); }
            catch (Exception ex) { TestContext.WriteLine($"Failure screenshot unavailable: {ex.Message}"); }
        }
        TestSetup.ResetUi();
    }

    [TestMethod]
    [DataRow("Nav_AllTasks")]
    [DataRow("Nav_MyDay")]
    [DataRow("Nav_Important")]
    [DataRow("Nav_Planned")]
    public void CreateFromSmartList_PersistsMembershipAcrossRestart(string navigation)
    {
        Find(navigation).Click();
        var title = CreateTask();
        var task = WaitForSaved(title, t => navigation switch
        {
            "Nav_MyDay" => t.GetProperty("IsInMyDay").GetBoolean(),
            "Nav_Important" => t.GetProperty("IsStarred").GetBoolean(),
            "Nav_Planned" => t.GetProperty("DueDate").ValueKind == JsonValueKind.String,
            _ => true
        });
        TestSetup.Restart();
        Find(navigation).Click();
        Named(title).Click();
        Assert.AreEqual(title, Find("PaneTitleBox").AsTextBox().Text);
        Assert.AreEqual(task.GetProperty("Id").GetString(), WaitForSaved(title, _ => true).GetProperty("Id").GetString());
    }

    [TestMethod]
    public void EditNotesAndCompleteStep_RestoresWithoutCompletingParent()
    {
        var original = CreateTask();
        Named(original).Click();
        var title = original + " edited";
        EnterAndWait(Find("PaneTitleBox").AsTextBox(), title);
        EnterAndWait(Find("PaneNotesBox").AsTextBox(), "First line\nSecond line");
        var input = Find("PaneStepInput").AsTextBox();
        input.Focus();
        EnterAndWait(input, "Review changes");
        Keyboard.Press(VirtualKeyShort.RETURN);
        Find("PaneStepCheck").AsCheckBox().IsChecked = true;
        WaitForSaved(title, t => t.GetProperty("Notes").GetString()!.Contains("Second line")
            && t.GetProperty("Steps").GetArrayLength() == 1
            && t.GetProperty("Steps")[0].GetProperty("IsCompleted").GetBoolean());
        TestSetup.Restart();
        Find("Nav_AllTasks").Click();
        Named(title).Click();
        Assert.IsTrue(Find("PaneNotesBox").AsTextBox().Text.Contains("Second line"));
        Assert.IsTrue(Find("PaneStepCheck").AsCheckBox().IsChecked == true);
        Assert.IsFalse(Find("PaneCompleteCheck").AsCheckBox().IsChecked == true);
        CaptureState("edited-task");
    }

    [TestMethod]
    public void DeleteAndUndo_RestoresTaskAndItsSavedState()
    {
        var title = CreateTask();
        Named(title).Click();
        var star = Find("PaneStarButton");
        star.AsButton().Invoke();
        WaitForSaved(title, t => t.GetProperty("IsStarred").GetBoolean());
        // Delete shortcut applies to the selected task when focus is outside text input.
        star.Focus();
        Keyboard.Press(VirtualKeyShort.DELETE);
        WaitForSaved(title, t => t.GetProperty("IsDeleted").GetBoolean());
        var undo = Find("UndoButton");
        undo.AsButton().Invoke();
        WaitForSaved(title, t => !t.GetProperty("IsDeleted").GetBoolean() && t.GetProperty("IsStarred").GetBoolean());
        TestSetup.Restart();
        Find("Nav_Important").Click();
        Assert.IsNotNull(Named(title));
    }

    [TestMethod]
    public void Search_NavigatesToTheMatchingTask()
    {
        var title = CreateTask();
        var search = Find("Search_AutoSuggestBox");
        var edit = search.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
        Assert.IsNotNull(edit, "Search must expose an editable field.");
        EnterAndWait(edit.AsTextBox(), title);
        Keyboard.Press(VirtualKeyShort.RETURN);
        Find("Search_Results");
        Find("Nav_AllTasks").Click();
        Find("NewTask_TextBox");
        Assert.AreEqual(string.Empty, edit.AsTextBox().Text, "Invoking the current list must exit search.");
        EnterAndWait(edit.AsTextBox(), title);
        var results = Find("Search_Results");
        var result = results.FindFirstDescendant(cf => cf.ByName(title));
        Assert.IsNotNull(result, "Search results must contain the new task.");
        result.Click();
        Assert.AreEqual(title, Find("PaneTitleBox").AsTextBox().Text);
    }

    [TestMethod]
    public void EmptyTask_IsRejected_AndCompletionCanBeUndone()
    {
        var input = Find("NewTask_TextBox").AsTextBox();
        EnterAndWait(input, "   ");
        Assert.IsFalse(Find("NewTask_AddButton").IsEnabled, "Whitespace must not create a task.");
        var title = CreateTask();
        Named(title).Click();
        Find("PaneCompleteCheck").AsCheckBox().IsChecked = true;
        WaitForSaved(title, t => t.GetProperty("IsCompleted").GetBoolean());
        Find("UndoButton").AsButton().Invoke();
        WaitForSaved(title, t => !t.GetProperty("IsCompleted").GetBoolean());
        Assert.IsNotNull(Named(title));
    }

    [TestMethod]
    public void CompletedGroup_ShowsFiveTasksUntilShowMoreIsPressed()
    {
        Find("Nav_AllTasks").Click();
        var titles = Enumerable.Range(0, 7).Select(_ => CreateTask()).ToArray();

        foreach (var title in titles)
        {
            Named(title).Click();
            Find("PaneCompleteCheck").AsCheckBox().IsChecked = true;
            WaitForSaved(title, task => task.GetProperty("IsCompleted").GetBoolean());
        }

        Find("FlatListView").Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
        var expander = Find("CompletedTasks_Expander");
        var expandCollapse = expander.Patterns.ExpandCollapse.Pattern;
        if (expandCollapse.ExpandCollapseState.ToString() != "Expanded")
            expandCollapse.Expand();

        Find("FlatListView").Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
        var completedList = Find("CompletedTasks_List");
        Assert.AreEqual(5, completedList.FindAllDescendants(cf => cf.ByAutomationId("Task_CheckBox")).Length);
        // Expansion changes the scroll extent after the UIA call returns.
        // Follow that extent until the button is actually in the viewport.
        var showMore = WaitFor(() =>
        {
            Find("FlatListView").Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
            return Main.FindFirstDescendant(cf => cf.ByAutomationId("CompletedTasks_ShowMore"));
        }, "CompletedTasks_ShowMore");
        Assert.AreEqual("Show 2 more", showMore.Name);
        showMore.AsButton().Invoke();
        Find("FlatListView").Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);

        completedList = WaitFor(() =>
        {
            var list = Main.FindFirstDescendant(cf => cf.ByAutomationId("CompletedTasks_List"));
            return list?.FindAllDescendants(cf => cf.ByAutomationId("Task_CheckBox")).Length == 7 ? list : null;
        }, "seven completed tasks");

        Assert.AreEqual(7, completedList.FindAllDescendants(cf => cf.ByAutomationId("Task_CheckBox")).Length,
            "Show more should reveal the remaining completed tasks.");
        var moreButton = Main.FindFirstDescendant(cf => cf.ByAutomationId("CompletedTasks_ShowMore"));
        Assert.IsTrue(moreButton is null || moreButton.IsOffscreen,
            "Show more should disappear after the remaining completed tasks are shown.");
    }

    [TestMethod]
    public void MyDaySuggestions_StaysOffPageAndRefillsShortlist()
    {
        Find("Nav_MyDay").Click();
        Find("Nav_AllTasks").Click();
        for (int i = 0; i < 7; i++)
        {
            Main.Focus();
            Find("NewTask_TextBox").Focus();
            CreateTask();
        }
        Find("Nav_MyDay").Click();
        var hiddenList = Main.FindFirstDescendant(cf => cf.ByAutomationId("Suggestions_List"));
        Assert.IsTrue(hiddenList is null || hiddenList.IsOffscreen,
            "Suggestions must not occupy the My Day task page before they are requested.");
        CaptureState("myday-suggestions-closed");

        Find("Suggestions_Button").AsButton().Invoke();
        var summary = Find("Suggestions_Summary");
        Assert.IsTrue(summary.Name.StartsWith("Showing 5 of ", StringComparison.Ordinal));
        int total = int.Parse(summary.Name.Split(' ')[^1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreEqual("Show all", Find("Suggestions_Toggle").Name);
        CaptureState("myday-suggestions-preview");

        Find("Suggestions_Toggle").AsButton().Invoke();
        WaitFor(() => Find("Suggestions_Summary").Name == $"Showing {total} of {total}"
            ? Find("Suggestions_Summary") : null, "all suggestions");
        Assert.AreEqual("Show fewer", Find("Suggestions_Toggle").Name);
        Assert.IsTrue(Find("Suggestions_List").BoundingRectangle.Bottom < Main.BoundingRectangle.Bottom,
            "The expanded suggestions list must stay inside its separate scrollable surface.");
        CaptureState("myday-suggestions-all");

        Find("Suggestions_Toggle").AsButton().Invoke();
        WaitFor(() => Find("Suggestions_Summary").Name == $"Showing 5 of {total}"
            ? Find("Suggestions_Summary") : null, "five suggestions");
        var add = Find("Suggestions_List").FindFirstDescendant(cf => cf.ByAutomationId("Suggestion_AddButton"));
        Assert.IsNotNull(add);
        var title = add.Parent?.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))?.Name
            ?? throw new AssertFailedException("The suggestion row must expose its task title.");
        add.AsButton().Invoke();
        WaitForSaved(title, task => task.GetProperty("IsInMyDay").GetBoolean());
        WaitFor(() => Find("Suggestions_Summary").Name == $"Showing 5 of {total - 1}"
            ? Find("Suggestions_Summary") : null, "refilled shortlist");
        WaitFor(() => Find("Suggestions_List").FindFirstDescendant(cf => cf.ByAutomationId("Suggestion_AddButton")),
            "refilled suggestion row");
        Assert.IsTrue(Find("Suggestions_List").FindFirstDescendant(cf => cf.ByName(title)) is null,
            "A chosen task must leave the suggestions.");

        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Named(title);
        hiddenList = Main.FindFirstDescendant(cf => cf.ByAutomationId("Suggestions_List"));
        Assert.IsTrue(hiddenList is null || hiddenList.IsOffscreen);
        CaptureState("myday-suggestions-chosen");
        Find("Suggestions_Button").AsButton().Invoke();
        Assert.AreEqual($"Showing 5 of {total - 1}", Find("Suggestions_Summary").Name);
        var suggestedTitle = Find("Suggestions_List").FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
        Assert.IsNotNull(suggestedTitle);
        var selectedTitle = suggestedTitle.Name;
        suggestedTitle.Click();
        Assert.AreEqual(selectedTitle, Find("PaneTitleBox").AsTextBox().Text);
        hiddenList = Main.FindFirstDescendant(cf => cf.ByAutomationId("Suggestions_List"));
        Assert.IsTrue(hiddenList is null || hiddenList.IsOffscreen,
            "Opening task details must dismiss the suggestions surface.");
    }

    [TestMethod]
    public void MyDaySuggestions_ClosesWhenLastSuggestionIsAdded()
    {
        Find("Nav_AllTasks").Click();
        Main.Focus();
        Find("NewTask_TextBox").Focus();
        var title = CreateTask();
        Find("Nav_MyDay").Click();
        Find("Suggestions_Button").AsButton().Invoke();
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestSetup.DataDirectory, "tasks.json")));
        int total = saved.RootElement.GetProperty("Tasks").EnumerateArray().Count(task =>
            !task.GetProperty("IsCompleted").GetBoolean() && !task.GetProperty("IsDeleted").GetBoolean()
            && !task.GetProperty("IsInMyDay").GetBoolean());
        Assert.IsTrue(total > 0);

        for (int remaining = total; remaining > 0; remaining--)
        {
            var add = WaitFor(() => Main.FindFirstDescendant(cf => cf.ByAutomationId("Suggestions_List"))?
                .FindFirstDescendant(cf => cf.ByAutomationId("Suggestion_AddButton")), "next suggested task");
            var suggestedTitle = add.Parent?.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))?.Name
                ?? throw new AssertFailedException("The suggestion must expose its task title.");
            add.AsButton().Invoke();
            WaitForSaved(suggestedTitle, task => task.GetProperty("IsInMyDay").GetBoolean());
        }

        WaitFor(() =>
        {
            var description = Main.FindFirstDescendant(cf => cf.ByName("Choose tasks to add to your My Day plan."));
            var button = Main.FindFirstDescendant(cf => cf.ByAutomationId("Suggestions_Button"));
            return (description is null || description.IsOffscreen) && (button is null || button.IsOffscreen) ? Main : null;
        }, "empty suggestions popup and anchor dismissed");
        WaitForSaved(title, task => task.GetProperty("IsInMyDay").GetBoolean());
        CaptureState("myday-suggestions-exhausted");
    }

    [TestMethod]
    [DataRow(620)]
    [DataRow(1000)]
    public void TaskDetails_DueDatePresetsSelectClearAndPersist(int width)
    {
        var originalBounds = Main.BoundingRectangle;
        try
        {
            Main.Patterns.Transform.Pattern.Resize(width, 760);
            Find("Nav_AllTasks").Click();
            Main.Focus();
            Find("NewTask_TextBox").Focus();
            var title = CreateTask();
            Named(title).Click();
            ScrollToPresets();
            var today = DateTime.Today;
            var weekend = today;
            while (weekend.DayOfWeek != DayOfWeek.Saturday) weekend = weekend.AddDays(1);
            var nextWeek = today.AddDays(1);
            while (nextWeek.DayOfWeek != DayOfWeek.Monday) nextWeek = nextWeek.AddDays(1);
            var presets = new[]
            {
                ("PaneDuePreset_Today", "Today", today),
                ("PaneDuePreset_Tomorrow", "Tomorrow", today.AddDays(1)),
                ("PaneDuePreset_Weekend", "This weekend", weekend),
                ("PaneDuePreset_NextWeek", "Next week", nextWeek)
            };
            var viewport = Find("DetailsPaneScrollViewer").BoundingRectangle;
            var buttonWidth = Find(presets[0].Item1).BoundingRectangle.Width;
            foreach (var (id, label, expected) in presets)
            {
                var button = Find(id);
                Assert.AreEqual(label, button.Name);
                Assert.IsTrue(viewport.Contains(button.BoundingRectangle), "Preset must fit inside the details pane.");
                Assert.AreEqual(buttonWidth, button.BoundingRectangle.Width, 1, "Preset grid columns must be equal.");
                Assert.AreEqual(Find("PaneDueDatePicker").BoundingRectangle.Height, button.BoundingRectangle.Height, 1,
                    "Presets must match the Due date control height.");
                button.AsButton().Invoke();
                WaitForSaved(title, task => task.GetProperty("DueDate").ValueKind == JsonValueKind.String
                    && task.GetProperty("DueDate").GetDateTimeOffset().Date == expected
                    && task.GetProperty("DueDate").GetDateTimeOffset().Offset == TimeSpan.Zero);
                WaitFor(() => Find(id).Patterns.Toggle.Pattern.ToggleState == FlaUI.Core.Definitions.ToggleState.On ? Find(id) : null,
                    "selected preset " + label);
                Assert.IsTrue(Find("PaneRepeatCombo").IsEnabled);
                CaptureState($"due-preset-{width}-{id}");
                Find(id).AsButton().Invoke();
                WaitForSaved(title, task => task.GetProperty("DueDate").ValueKind == JsonValueKind.Null);
                Assert.IsFalse(Find("PaneRepeatCombo").IsEnabled);
            }
            Find("PaneDuePreset_NextWeek").Click();
            WaitForSaved(title, task => task.GetProperty("DueDate").ValueKind == JsonValueKind.String);
            TestSetup.Restart();
            Main.Patterns.Transform.Pattern.Resize(width, 760);
            Find("Nav_AllTasks").Click();
            Named(title).Click();
            ScrollToPresets();
            Assert.AreEqual(FlaUI.Core.Definitions.ToggleState.On, Find("PaneDuePreset_NextWeek").Patterns.Toggle.Pattern.ToggleState);
            Assert.AreEqual(nextWeek, WaitForSaved(title, _ => true).GetProperty("DueDate").GetDateTimeOffset().Date);
        }
        finally { Main.Patterns.Transform.Pattern.Resize(originalBounds.Width, originalBounds.Height); }

        static void ScrollToPresets()
        {
            var viewport = Find("DetailsPaneScrollViewer");
            for (int percent = 0; percent <= 100; percent += 10)
            {
                if (viewport.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
                    viewport.Patterns.Scroll.Pattern.SetScrollPercent(-1, percent);
                Thread.Sleep(100);
                var first = Main.FindFirstDescendant(cf => cf.ByAutomationId("PaneDuePreset_Today"));
                var last = Main.FindFirstDescendant(cf => cf.ByAutomationId("PaneDuePreset_NextWeek"));
                if (first is not null && last is not null && viewport.BoundingRectangle.Contains(first.BoundingRectangle)
                    && viewport.BoundingRectangle.Contains(last.BoundingRectangle)) return;
            }
            Assert.Fail("The due-date presets could not be brought into view.");
        }
    }

    [TestMethod]
    [DataRow(620, 2)]
    [DataRow(1000, 9)]
    public void TaskDetails_FooterShowsTimestampsAndDeleteSupportsUndo(int width, int ageDays)
    {
        var originalBounds = Main.BoundingRectangle;
        try
        {
            Main.Patterns.Transform.Pattern.Resize(width, 760);
            Find("Nav_AllTasks").Click();
            Main.Focus();
            Find("NewTask_TextBox").Focus();
            var title = CreateTask();
            var path = Path.Combine(TestSetup.DataDirectory, "tasks.json");
            var data = JsonNode.Parse(File.ReadAllText(path))!;
            var fixtureTask = data["Tasks"]!.AsArray().Single(task => task!["Title"]!.GetValue<string>() == title)!;
            fixtureTask["CreatedAt"] = JsonValue.Create(DateTime.UtcNow.AddDays(-ageDays));
            fixtureTask["UpdatedAt"] = JsonValue.Create(DateTimeOffset.UtcNow.AddMinutes(-5));
            var fixture = data.ToJsonString();
            TestSetup.Restart(() => File.WriteAllText(path, fixture));
            Main.Patterns.Transform.Pattern.Resize(width, 760);
            Find("Nav_AllTasks").Click();
            var taskScroller = Find("FlatListView");
            if (taskScroller.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
                taskScroller.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
            Named(title).Click();
            var viewport = Find("DetailsPaneScrollViewer");
            if (viewport.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
                viewport.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
            var created = Find("PaneCreatedAtText");
            var updated = Find("PaneUpdatedAtText");
            var delete = Find("Pane_DeleteTask");
            Assert.AreEqual("Delete task", delete.Name);
            if (ageDays == 2) Assert.AreEqual("2 days ago", created.Name);
            else Assert.IsTrue(created.Name.Length > 0 && !created.Name.Contains("ago", StringComparison.Ordinal),
                "Older timestamps must show an absolute date.");
            StringAssert.EndsWith(updated.Name, "min ago");
            var initialCreated = created.Name;
            Assert.IsTrue(created.BoundingRectangle.Top < updated.BoundingRectangle.Top);
            Assert.AreEqual(created.BoundingRectangle.Right, updated.BoundingRectangle.Right, 1,
                "Timestamp values must share the right edge.");
            Assert.IsTrue(delete.BoundingRectangle.Top > updated.BoundingRectangle.Bottom);
            Assert.IsTrue(viewport.BoundingRectangle.Contains(delete.BoundingRectangle));
            CaptureState("task-footer-" + width);
            var editedTitle = title + " edited";
            EnterAndWait(Find("PaneTitleBox").AsTextBox(), editedTitle);
            WaitForSaved(editedTitle, _ => true);
            WaitFor(() => Find("PaneUpdatedAtText").Name == "just now" ? Find("PaneUpdatedAtText") : null, "updated timestamp after edit");
            Assert.AreEqual(initialCreated, Find("PaneCreatedAtText").Name);
            Find("Pane_DeleteTask").AsButton().Invoke();
            WaitForSaved(editedTitle, task => task.GetProperty("IsDeleted").GetBoolean());
            Find("UndoButton").AsButton().Invoke();
            WaitForSaved(editedTitle, task => !task.GetProperty("IsDeleted").GetBoolean());
            TestSetup.Restart();
            Find("Nav_AllTasks").Click();
            var restoredScroller = Find("FlatListView");
            if (restoredScroller.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
                restoredScroller.Patterns.Scroll.Pattern.SetScrollPercent(-1, 0);
            Assert.IsNotNull(Named(editedTitle));
        }
        finally { Main.Patterns.Transform.Pattern.Resize(originalBounds.Width, originalBounds.Height); }
    }

    private static string CreateTask()
    {
        var title = "Regression " + Guid.NewGuid().ToString("N")[..10];
        EnterAndWait(Find("NewTask_TextBox").AsTextBox(), title);
        Find("NewTask_AddButton").AsButton().Invoke();
        Named(title);
        WaitForSaved(title, _ => true);
        return title;
    }

    private static void EnterAndWait(TextBox input, string expected)
    {
        string normalizedExpected = NormalizeLineEndings(expected);
        input.Enter(expected);
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (NormalizeLineEndings(input.Text) == normalizedExpected) return;
            Thread.Sleep(50);
        }
        Assert.AreEqual(normalizedExpected, NormalizeLineEndings(input.Text),
            "Wait for the full text change to reach the WinUI control before acting on it.");
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n');

    internal static AutomationElement Find(string id) => WaitFor(() =>
        Main.FindFirstDescendant(cf => cf.ByAutomationId(id)), id);

    private static AutomationElement Named(string name) => WaitFor(() =>
        Main.FindFirstDescendant(cf => cf.ByName(name)), name);

    private static AutomationElement WaitFor(Func<AutomationElement?> lookup, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        do
        {
            var element = lookup();
            if (element is not null && !element.IsOffscreen) return element;
            Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);
        throw new AssertFailedException($"Visible control not found: {description}");
    }

    private static JsonElement WaitForSaved(string title, Func<JsonElement, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        do
        {
            try
            {
                using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestSetup.DataDirectory, "tasks.json")));
                foreach (var task in data.RootElement.GetProperty("Tasks").EnumerateArray())
                    if (task.GetProperty("Title").GetString() == title && predicate(task)) return task.Clone();
            }
            catch (IOException) { }
            Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);
        throw new AssertFailedException($"Expected saved task state did not arrive: {title}");
    }

    internal static void CaptureState(string name)
    {
        Thread.Sleep(350); // Let the 200ms pane transition finish before visual review.
        var folder = Environment.GetEnvironmentVariable("HATCH_MEASUREMENTS_DIR") ?? TestSetup.DataDirectory;
        Directory.CreateDirectory(folder);
        using var capture = Capture.Rectangle(Main.BoundingRectangle);
        capture.ToFile(Path.Combine(folder, name + ".png"));
    }
}
