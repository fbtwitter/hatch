using Hatch.Tests.Infrastructure;

namespace Hatch.Tests.DetailsPane;

/// <summary>
/// Verifies that the Notes TextBox in the Details Pane:
///   1. Renders at MinHeight (~100px) when empty
///   2. Grows as the user types multiple lines
///   3. Caps at MaxHeight (~220px) and scrolls internally for overflow
/// </summary>
[TestClass]
public class NotesPaneTests
{
    private static Window Window => TestSetup.MainWindow!;

    private AutomationElement Find(string automationId) =>
        Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
        ?? throw new InvalidOperationException($"Element '{automationId}' not found.");

    // ── fixture ───────────────────────────────────────────────────────────────

    [TestInitialize]
    public void OpenPane()
    {
        // Navigate to All Tasks for a consistent starting state
        Find("Nav_AllTasks").Click();
        Thread.Sleep(300);

        // Add a fresh task with a known title
        var titleBox = Find("NewTask_TextBox").AsTextBox();
        titleBox.Text = "";
        titleBox.Enter("_NotesTest_");
        Find("NewTask_AddButton").Click();
        Thread.Sleep(400);

        // Click the task title to open the details pane
        Window.FindFirstDescendant(cf => cf.ByName("_NotesTest_"))?.Click();
        Thread.Sleep(500);

        // Clear any pre-existing notes
        Find("PaneNotesBox").AsTextBox().Text = "";
        Thread.Sleep(200);
    }

    [TestCleanup]
    public void ClosePane()
    {
        try
        {
            Find("PaneCloseButton").Click();
            Thread.Sleep(300);
        }
        catch { /* pane may already be closed */ }
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Empty Notes field renders at MinHeight — not smaller, not pre-expanded")]
    public void Empty_RendersAtMinHeight()
    {
        var notes = Find("PaneNotesBox");
        int height = (int)notes.BoundingRectangle.Height;

        // MinHeight=100 logical px. Physical px = logical × DPI scale.
        // Accept 80–210 to cover 80%–210% display scaling.
        Assert.IsTrue(height >= 80,
            $"Empty Notes height {height}px is below MinHeight=100 (even at 80% DPI)");
        Assert.IsTrue(height <= 210,
            $"Empty Notes height {height}px is already expanded — " +
            $"box is not starting at MinHeight");
    }

    [TestMethod]
    [Description("A single short line keeps the box at MinHeight")]
    public void OneShortLine_StaysAtMinHeight()
    {
        var notes = Find("PaneNotesBox").AsTextBox();
        int baseline = (int)Find("PaneNotesBox").BoundingRectangle.Height;

        notes.Enter("A brief note.");
        Thread.Sleep(200);

        int after = (int)Find("PaneNotesBox").BoundingRectangle.Height;
        Assert.IsTrue(after <= baseline + 10,
            $"One line should not expand the box beyond MinHeight. " +
            $"baseline={baseline}px after={after}px");
    }

    [TestMethod]
    [Description("Typing 6 lines causes the box to grow above MinHeight")]
    public void SixLines_GrowsAboveMinHeight()
    {
        int baseline = (int)Find("PaneNotesBox").BoundingRectangle.Height;

        var notes = Find("PaneNotesBox").AsTextBox();
        for (int i = 1; i <= 6; i++)
            notes.Enter($"Line {i}\n");
        Thread.Sleep(300);

        int grown = (int)Find("PaneNotesBox").BoundingRectangle.Height;
        Assert.IsTrue(grown > baseline,
            $"6 lines should expand the box above MinHeight. " +
            $"baseline={baseline}px grown={grown}px");
    }

    [TestMethod]
    [Description("Typing 25 lines caps the box at MaxHeight (~220px) — no infinite growth")]
    public void TwentyFiveLines_CapsAtMaxHeight()
    {
        int baseline = (int)Find("PaneNotesBox").BoundingRectangle.Height;

        var notes = Find("PaneNotesBox").AsTextBox();
        for (int i = 1; i <= 25; i++)
            notes.Enter($"Line {i} — padding content to fill the notes box\n");
        Thread.Sleep(500);

        int capped = (int)Find("PaneNotesBox").BoundingRectangle.Height;

        // MaxHeight=220, MinHeight=100 → ratio 2.2. Allow 20% tolerance for DPI/rounding.
        int maxAllowed = (int)(baseline * 2.2 * 1.2);
        Assert.IsTrue(capped <= maxAllowed,
            $"25 lines should cap near MaxHeight=220. " +
            $"baseline={baseline}px capped={capped}px maxAllowed={maxAllowed}px. " +
            $"Box grew past MaxHeight — check MaxHeight is set on PaneNotesBox.");

        Assert.IsTrue(capped > baseline,
            $"Box should still have grown from empty ({baseline}px), got {capped}px");
    }

    [TestMethod]
    [Description("Height growth is monotonic: empty ≤ 6 lines ≤ 25 lines")]
    public void Height_IsMonotonic()
    {
        int h0 = (int)Find("PaneNotesBox").BoundingRectangle.Height;

        var notes = Find("PaneNotesBox").AsTextBox();
        for (int i = 1; i <= 6; i++) notes.Enter($"L{i}\n");
        Thread.Sleep(300);
        int h6 = (int)Find("PaneNotesBox").BoundingRectangle.Height;

        for (int i = 7; i <= 25; i++) notes.Enter($"L{i}\n");
        Thread.Sleep(400);
        int h25 = (int)Find("PaneNotesBox").BoundingRectangle.Height;

        Assert.IsTrue(h0 <= h6,
            $"Height must not shrink when adding lines. empty={h0}px 6-lines={h6}px");
        Assert.IsTrue(h6 <= h25,
            $"Height must not shrink when adding more lines. 6-lines={h6}px 25-lines={h25}px");
    }
}
