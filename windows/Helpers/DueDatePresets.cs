namespace Hatch.Helpers;

internal static class DueDatePresets
{
    public static DateTime GetToday(DateTime today) => today;

    public static DateTime GetTomorrow(DateTime today) => today.AddDays(1);

    // Saturday of the current week. Sunday snaps to next Saturday (weekend already passed).
    // Formula: ((6 - dayOfWeek + 7) % 7) gives 0 on Sat, 6 on Sun, correct for all others.
    public static DateTime GetThisWeekend(DateTime today)
    {
        int daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(daysUntilSaturday);
    }

    // Monday of next week; if today is already Monday, return next Monday (+7).
    public static DateTime GetNextWeek(DateTime today)
    {
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(daysUntilMonday == 0 ? 7 : daysUntilMonday);
    }
}
