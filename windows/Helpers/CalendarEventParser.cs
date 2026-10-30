using System.Globalization;
using System.Text.Json;
using Hatch.Models;

namespace Hatch.Helpers;

internal static class CalendarEventParser
{
    internal static void Apply(
        IDictionary<string, CalendarEvent> events, JsonElement item,
        string accountId, string calendarId, string calendarName)
    {
        var id = Text(item, "id");
        if (string.IsNullOrEmpty(id)) throw new JsonException("Calendar event has no identity.");
        if (item.TryGetProperty("@removed", out _) ||
            (item.TryGetProperty("isCancelled", out var cancelled) && cancelled.ValueKind == JsonValueKind.True) ||
            (item.TryGetProperty("responseStatus", out var response) && Text(response, "response") == "declined"))
        {
            events.Remove(id);
            return;
        }

        var start = ReadTime(item.GetProperty("start"));
        var end = ReadTime(item.GetProperty("end"));
        if (end < start) throw new JsonException("Calendar event ends before it starts.");
        var allDay = item.TryGetProperty("isAllDay", out var flag) && flag.ValueKind == JsonValueKind.True;
        DateOnly? allDayStart = null, allDayEnd = null;
        if (allDay)
        {
            var zoneId = Text(item, "originalStartTimeZone") ?? "UTC";
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
            catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Local; }
            catch (InvalidTimeZoneException) { zone = TimeZoneInfo.Local; }
            allDayStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
            allDayEnd = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end, zone).DateTime);
        }

        events[id] = new CalendarEvent
        {
            Id = id, AccountId = accountId, CalendarId = calendarId, CalendarName = calendarName,
            Subject = Text(item, "subject") ?? string.Empty,
            Start = start, End = end, IsAllDay = allDay, AllDayStart = allDayStart, AllDayEnd = allDayEnd,
            EventUrl = HttpsUrl(Text(item, "webLink")),
            MeetingUrl = item.TryGetProperty("onlineMeeting", out var meeting) && meeting.ValueKind == JsonValueKind.Object
                ? HttpsUrl(Text(meeting, "joinUrl")) : null
        };
    }

    internal static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    internal static string? HttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;

    private static DateTimeOffset ReadTime(JsonElement time)
    {
        var text = Text(time, "dateTime");
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
            throw new JsonException("Calendar event has an invalid time.");
        return value;
    }
}
