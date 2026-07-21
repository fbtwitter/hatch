using Hatch.Models;

namespace Hatch.Helpers;

public static class RecurrenceHelper
{
    // Anchored to the original due date (not "today") so a Monday task stays on Monday
    // even if completed late. DueDate is normalized to local calendar date + zero offset,
    // matching the convention used by the calendar picker and date-chip flyout.
    public static DateTimeOffset AdvanceDueDate(DateTimeOffset due, TaskRecurrence recurrence)
    {
        var date = due.ToLocalTime().Date;
        var next = recurrence switch
        {
            TaskRecurrence.Daily    => date.AddDays(1),
            TaskRecurrence.Weekly   => date.AddDays(7),
            TaskRecurrence.Monthly  => date.AddMonths(1),
            TaskRecurrence.Weekdays => AdvanceSkippingWeekend(date),
            _                       => date
        };
        return new DateTimeOffset(next, TimeSpan.Zero);
    }

    private static DateTime AdvanceSkippingWeekend(DateTime date)
    {
        var next = date.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            next = next.AddDays(1);
        return next;
    }
}
