namespace Hatch.Models;

public sealed record CalendarAccount
{
    public string Id { get; init; } = string.Empty;
    public CalendarProvider Provider { get; init; } = CalendarProvider.Microsoft;
    public string Email { get; init; } = string.Empty;
    public string DefaultCalendarId { get; init; } = string.Empty;
    public DateTimeOffset WindowStart { get; init; }
    public DateTimeOffset WindowEnd { get; init; }
    public DateTimeOffset? LastSyncedAt { get; init; }
    public List<CalendarSource> Calendars { get; init; } = [];
    public List<CalendarEvent> Events { get; init; } = [];
}
