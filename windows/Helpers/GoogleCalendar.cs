namespace Hatch.Helpers;

internal sealed record GoogleCalendar(string Id, string Name, string TimeZone, bool IsPrimary, string? DataOwner);
