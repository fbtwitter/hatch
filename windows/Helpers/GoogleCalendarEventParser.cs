using System.Globalization;
using System.Text.Json;
using Hatch.Models;

namespace Hatch.Helpers;

internal static class GoogleCalendarEventParser
{
    internal static bool HasMatchingDataOwner(bool isPrimary, string? dataOwner, string primaryEmail) =>
        isPrimary || string.Equals(dataOwner, primaryEmail, StringComparison.OrdinalIgnoreCase);

    internal static void Apply(IDictionary<string, CalendarEvent> events, JsonElement item,
        string accountId, string calendarId, string name, string timeZone)
    {
        var id = CalendarEventParser.Text(item, "id");
        if (string.IsNullOrEmpty(id)) throw new JsonException("Google event has no identity.");
        var declined = item.TryGetProperty("attendees", out var attendees) && attendees.ValueKind == JsonValueKind.Array &&
            attendees.EnumerateArray().Any(a => a.TryGetProperty("self", out var self) && self.ValueKind == JsonValueKind.True &&
                CalendarEventParser.Text(a, "responseStatus") == "declined");
        if (CalendarEventParser.Text(item, "status") == "cancelled" || declined)
        {
            events.Remove(id);
            return;
        }
        var start = ReadTime(item.GetProperty("start"), timeZone, out var startDate);
        var end = ReadTime(item.GetProperty("end"), timeZone, out var endDate);
        if (end < start) throw new JsonException("Google event ends before it starts.");
        string? meetingUrl = null;
        if (item.TryGetProperty("conferenceData", out var conference) && conference.ValueKind == JsonValueKind.Object &&
            conference.TryGetProperty("entryPoints", out var entries) && entries.ValueKind == JsonValueKind.Array)
            meetingUrl = entries.EnumerateArray().Where(e => CalendarEventParser.Text(e, "entryPointType") == "video")
                .Select(e => CalendarEventParser.HttpsUrl(CalendarEventParser.Text(e, "uri"))).FirstOrDefault(u => u != null);
        meetingUrl ??= CalendarEventParser.HttpsUrl(CalendarEventParser.Text(item, "hangoutLink"));
        events[id] = new CalendarEvent
        {
            Id = id, AccountId = accountId, CalendarId = calendarId, CalendarName = name,
            Subject = CalendarEventParser.Text(item, "summary") ?? string.Empty,
            Start = start, End = end, IsAllDay = startDate != null, AllDayStart = startDate, AllDayEnd = endDate,
            EventUrl = CalendarEventParser.HttpsUrl(CalendarEventParser.Text(item, "htmlLink")), MeetingUrl = meetingUrl
        };
    }

    private static DateTimeOffset ReadTime(JsonElement point, string calendarZone, out DateOnly? day)
    {
        day = null;
        var date = CalendarEventParser.Text(point, "date");
        var text = CalendarEventParser.Text(point, "dateTime");
        if (date != null)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new JsonException("Invalid all-day date.");
            day = parsed;
            var midnight = parsed.ToDateTime(TimeOnly.MinValue);
            // All-day values are calendar dates, not timed instants; the original date remains authoritative.
            return new DateTimeOffset(midnight, TimeSpan.Zero);
        }
        var hasOffset = text != null && (text.EndsWith('Z') ||
            (text.Length >= 6 && (text[^6] == '+' || text[^6] == '-') && text[^3] == ':'));
        if (hasOffset && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var instant)) return instant;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(CalendarEventParser.Text(point, "timeZone") ?? calendarZone);
            return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        }
        throw new JsonException("Invalid Google event time.");
    }
}
