using Hatch.Models;

namespace Hatch.Helpers;

public static class RecurrenceHelper
{
    // Anchored to the original due date (not "today") so a Monday task stays on Monday
    // even if completed late. The date is read as written (the value's own offset), never
    // through a time-zone conversion: due dates are stored as midnight +00:00, so
    // ToLocalTime().Date shifts them back a day on any machine west of UTC. Matches the
    // Kotlin port, which reads the calendar date from the ISO text.
    public static DateTimeOffset AdvanceDueDate(DateTimeOffset due, TaskRecurrence recurrence)
    {
        var date = due.Date;
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
