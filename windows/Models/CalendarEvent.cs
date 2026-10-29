namespace Hatch.Models;

public sealed record CalendarEvent
{
    public string Id { get; init; } = string.Empty;
    public string CalendarId { get; init; } = string.Empty;
    public string CalendarName { get; init; } = string.Empty;
    public string AccountId { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public bool IsAllDay { get; init; }
    public DateOnly? AllDayStart { get; init; }
    public DateOnly? AllDayEnd { get; init; }
    public string? EventUrl { get; init; }
    public string? MeetingUrl { get; init; }
}
