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

    private AutomationElement WaitForName(string name)
    {
        for (int i = 0; i < 80; i++)
        {
            var element = Window.FindFirstDescendant(cf => cf.ByName(name));
            if (element is not null) return element;
            Thread.Sleep(100);
        }
        throw new InvalidOperationException($"Task '{name}' was not visible.");
    }

    private bool WaitForVisible(string automationId, int timeoutMs)
    {
        for (int i = 0; i < timeoutMs / 100; i++)
        {
            var element = Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element is not null && !element.IsOffscreen) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    [TestInitialize]
    public void OpenPane()
    {
        if (WaitForVisible("PaneNotesBox", 300))
        {
            ClearNotesText();
            return;
        }

        TestSetup.ResetUi();

        // Navigate to All Tasks for a consistent starting state
        Find("Nav_AllTasks").Click();
        Thread.Sleep(300);

        // Use the fixture's seeded task so Notes tests do not mutate the task list.
        var probe = WaitForName("_RuntimeProbe_");
        probe.Click();
        if (!WaitForVisible("PaneNotesBox", 3000)) probe.Click();
        Assert.IsTrue(WaitForVisible("PaneNotesBox", 5000), "The Details Pane must open for the seeded task.");
        Thread.Sleep(500);

        // Clear any pre-existing notes
        ClearNotesText();
    }

    [ClassCleanup]
    public static void RestoreDefaultUi() => TestSetup.ResetUi();

    [TestCleanup]
    public void ClearNotes()
    {
        try { ClearNotesText(); }
        catch { }
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

        EnterAndWait(notes, "A brief note.");

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
        var expected = string.Join(Environment.NewLine,
            Enumerable.Range(1, 6).Select(i => $"Line {i}"));
        EnterAndWait(notes, expected);

        Assert.IsTrue(notes.Text.Contains("Line 6"),
            $"UI Automation did not enter the expected multiline note: '{notes.Text}'");
        int grown = WaitForHeightGreaterThan(notes, baseline);
        Assert.IsTrue(grown > baseline,
            $"6 lines should expand the box above MinHeight. " +
            $"baseline={baseline}px grown={grown}px textLength={notes.Text.Length}");
    }

    [TestMethod]
    [Description("Typing 25 lines caps the box at MaxHeight (~220px) — no infinite growth")]
    public void TwentyFiveLines_CapsAtMaxHeight()
    {
        int baseline = (int)Find("PaneNotesBox").BoundingRectangle.Height;

        var notes = Find("PaneNotesBox").AsTextBox();
        var expected = string.Join(Environment.NewLine,
            Enumerable.Range(1, 25).Select(i => $"Line {i} — padding content to fill the notes box"));
        EnterAndWait(notes, expected);

        int capped = WaitForHeightGreaterThan(notes, baseline);

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
        EnterAndWait(notes, string.Join(Environment.NewLine, Enumerable.Range(1, 6).Select(i => $"L{i}")));
        int h6 = WaitForHeightGreaterThan(notes, h0);

        EnterAndWait(notes, string.Join(Environment.NewLine, Enumerable.Range(1, 25).Select(i => $"L{i}")));
        int h25 = (int)Find("PaneNotesBox").BoundingRectangle.Height;

        Assert.IsTrue(h0 <= h6,
            $"Height must not shrink when adding lines. empty={h0}px 6-lines={h6}px");
        Assert.IsTrue(h6 <= h25,
            $"Height must not shrink when adding more lines. 6-lines={h6}px 25-lines={h25}px");
    }

    private void ClearNotesText()
    {
        var notes = Find("PaneNotesBox").AsTextBox();
        notes.Text = string.Empty;
        WaitForText(notes, string.Empty);
    }

    private static void EnterAndWait(TextBox notes, string expected)
    {
        notes.Focus();
        notes.Enter(expected);
        WaitForText(notes, expected);
    }

    private static void WaitForText(TextBox notes, string expected)
    {
        string normalizedExpected = NormalizeLineEndings(expected);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (NormalizeLineEndings(notes.Text) == normalizedExpected) return;
            Thread.Sleep(50);
        }
        Assert.AreEqual(normalizedExpected, NormalizeLineEndings(notes.Text),
            "Wait for the full note text to reach the WinUI control.");
    }

    private static int WaitForHeightGreaterThan(TextBox notes, int baseline)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        int height = (int)notes.BoundingRectangle.Height;
        while (height <= baseline && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
            height = (int)notes.BoundingRectangle.Height;
        }
        return height;
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n');
}
