using Hatch.Helpers;
using Hatch.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

// Mirrors hatch-mobile feature/summary/SummaryStatsTest.kt's weekRhythm/currentStreak cases.
// Unlike the Kotlin original, SummaryStats takes `today` as a parameter, so fixtures use a
// fixed date rather than one captured from the wall clock.
[TestClass]
public class SummaryStatsTests
{
    private static readonly DateTime Today = new(2026, 8, 30);

    // Anchored at local midday so a LocalDateTime round-trip lands back on `date` wherever
    // the test runs.
    private static TodoItem CompletedOn(DateOnly date) => new()
    {
        Title = date.ToString(),
        IsCompleted = true,
        CompletedAt = new DateTimeOffset(
            DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Local))
    };

    private static DateOnly Day(int daysBack) => DateOnly.FromDateTime(Today).AddDays(-daysBack);

    [TestMethod]
    public void WeekRhythm_IsAlwaysSevenDaysOldestFirstWithTodayLastAndFlagged()
    {
        var rhythm = SummaryStats.WeekRhythm([], Today);

        Assert.AreEqual(7, rhythm.Count);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 7).Reverse().Select(Day).ToList(),
            rhythm.Select(d => d.Date).ToList());
        Assert.AreEqual(Day(0), rhythm[^1].Date);
        Assert.IsTrue(rhythm[^1].IsToday);
        Assert.IsTrue(rhythm.Take(6).All(d => !d.IsToday));
        Assert.IsTrue(rhythm.All(d => d.Completed == 0));
    }

    [TestMethod]
    public void WeekRhythm_CountsCompletionsPerDayWithinTheWindow()
    {
        List<TodoItem> tasks =
        [
            CompletedOn(Day(0)),
            CompletedOn(Day(0)),
            CompletedOn(Day(3)),
            CompletedOn(Day(9)),                 // older than the window — must not count
            new() { Title = "open" },             // no CompletedAt
        ];

        var rhythm = SummaryStats.WeekRhythm(tasks, Today);

        Assert.AreEqual(2, rhythm.Single(d => d.Date == Day(0)).Completed);
        Assert.AreEqual(1, rhythm.Single(d => d.Date == Day(3)).Completed);
        Assert.AreEqual(3, rhythm.Sum(d => d.Completed));
    }

    [TestMethod]
    public void Streak_IsZeroWithNoCompletions()
        => Assert.AreEqual(0, SummaryStats.CurrentStreak([], Today));

    [TestMethod]
    public void Streak_CountsConsecutiveDaysIncludingToday()
    {
        List<TodoItem> tasks =
        [
            CompletedOn(Day(0)),
            CompletedOn(Day(1)),
            CompletedOn(Day(2)),
            CompletedOn(Day(4)),   // gap at Day(3) — not counted
        ];

        Assert.AreEqual(3, SummaryStats.CurrentStreak(tasks, Today));
    }

    [TestMethod]
    public void Streak_DoesNotBreakOnAnEmptyTodayIfYesterdayWasCompleted()
    {
        List<TodoItem> tasks = [CompletedOn(Day(1)), CompletedOn(Day(2))];

        Assert.AreEqual(2, SummaryStats.CurrentStreak(tasks, Today));
    }

    [TestMethod]
    public void Streak_IsZeroWhenBothTodayAndYesterdayAreEmpty()
    {
        List<TodoItem> tasks = [CompletedOn(Day(2))];

        Assert.AreEqual(0, SummaryStats.CurrentStreak(tasks, Today));
    }

    [TestMethod]
    public void Streak_CanExtendPastTheSevenDayRhythmWindow()
    {
        var tasks = Enumerable.Range(0, 10).Select(Day).Select(CompletedOn).ToList();

        Assert.AreEqual(10, SummaryStats.CurrentStreak(tasks, Today));
    }
}
