using System.Text.Json;
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
        var showMore = Find("CompletedTasks_ShowMore");
        Assert.AreEqual("Show 2 more", showMore.Name);
        showMore.AsButton().Invoke();
        Find("FlatListView").Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);

        Assert.AreEqual(7, completedList.FindAllDescendants(cf => cf.ByAutomationId("Task_CheckBox")).Length,
            "Show more should reveal the remaining completed tasks.");
        var moreButton = Main.FindFirstDescendant(cf => cf.ByAutomationId("CompletedTasks_ShowMore"));
        Assert.IsTrue(moreButton is null || moreButton.IsOffscreen,
            "Show more should disappear after the remaining completed tasks are shown.");
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
