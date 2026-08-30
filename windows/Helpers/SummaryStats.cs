using Hatch.Models;

namespace Hatch.Helpers;

// One entry per day, oldest first, always seven — today is last and flagged.
public sealed record DayCount(DateOnly Date, int Completed, bool IsToday);

// The last-7-days completion strip and the streak count on the Summary page. Transcribed
// from hatch-mobile feature/summary/SummaryStats.kt, which flagged both as Android-only with
// no desktop equivalent — this brings them back. Kept free of any WinUI using so
// Hatch.Tests.Unit can link the file directly. `today` is passed in rather than read from
// DateTime.Today so the logic is testable without straddling local midnight.
public static class SummaryStats
{
    // CompletedAt is a real instant (DateTimeOffset.Now when the box is ticked), not a
    // "day as written" like a due date — so it is read in local time: you finished it on
    // *your* Tuesday. A completed-then-deleted task never reaches here (tombstones live
    // outside MainViewModel.Tasks). Unbounded on purpose — CurrentStreak walks back past
    // the seven-day display window.
    private static Dictionary<DateOnly, int> CompletionDayCounts(IEnumerable<TodoItem> tasks)
    {
        var byDay = new Dictionary<DateOnly, int>();
        foreach (var task in tasks)
        {
            if (task.CompletedAt is not { } completed) continue;
            var day = DateOnly.FromDateTime(completed.LocalDateTime);
            byDay[day] = byDay.GetValueOrDefault(day) + 1;
        }
        return byDay;
    }

    public static IReadOnlyList<DayCount> WeekRhythm(IEnumerable<TodoItem> tasks, DateTime today)
    {
        var counts = CompletionDayCounts(tasks);
        var todayDate = DateOnly.FromDateTime(today);
        var days = new List<DayCount>(7);
        for (int back = 6; back >= 0; back--)
        {
            var day = todayDate.AddDays(-back);
            days.Add(new DayCount(day, counts.GetValueOrDefault(day), day == todayDate));
        }
        return days;
    }

    // Consecutive calendar days with >= 1 completion, walking back from today. An empty
    // today does not break an in-progress streak while the day isn't over — but a prior
    // empty day does. A quiet momentum count, not a mechanic: no freeze/save items, no
    // notification when it breaks.
    public static int CurrentStreak(IEnumerable<TodoItem> tasks, DateTime today)
    {
        var counts = CompletionDayCounts(tasks);
        var day = DateOnly.FromDateTime(today);
        if (counts.GetValueOrDefault(day) == 0)
        {
            day = day.AddDays(-1);
            if (counts.GetValueOrDefault(day) == 0) return 0;
        }

        int streak = 0;
        while (counts.GetValueOrDefault(day) > 0)
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }
}
