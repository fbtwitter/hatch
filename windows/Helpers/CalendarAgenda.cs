using Hatch.Models;

namespace Hatch.Helpers;

internal static class CalendarAgenda
{
    internal static bool IncludesDay(CalendarEvent item, DateOnly day, TimeZoneInfo zone)
    {
        if (item.IsAllDay && item.AllDayStart is { } first && item.AllDayEnd is { } last)
            return first <= day && day < last;
        var start = TimeZoneInfo.ConvertTime(item.Start, zone).DateTime;
        var end = TimeZoneInfo.ConvertTime(item.End, zone).DateTime;
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        return start < dayStart.AddDays(1) && (end > dayStart || start == dayStart);
    }

    internal static bool HasEnded(CalendarEvent item, DateTimeOffset now) =>
        item.IsAllDay && item.AllDayEnd is { } day
            ? day <= DateOnly.FromDateTime(now.LocalDateTime) : item.End <= now;

    internal static DateOnly AgendaDate(CalendarEvent item, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var hasEnded = item.IsAllDay && item.AllDayEnd is { } allDayEnd
            ? allDayEnd <= today : item.End <= now;
        if (!hasEnded && IncludesDay(item, today, zone)) return today;
        if (item.IsAllDay && item.AllDayStart is { } allDayStart) return allDayStart;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.Start, zone).DateTime);
    }

    internal static CalendarEvent? QuickJoin(IEnumerable<CalendarEvent> events, DateOnly day, DateTimeOffset now) =>
        day != DateOnly.FromDateTime(now.LocalDateTime) ? null : events
            .Where(item => !item.IsAllDay && item.End > now && IncludesDay(item, day, TimeZoneInfo.Local)
                && CalendarEventParser.HttpsUrl(item.MeetingUrl) != null)
            .OrderBy(item => item.Start <= now ? 0 : 1)
            .ThenBy(item => item.Start).ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
}
