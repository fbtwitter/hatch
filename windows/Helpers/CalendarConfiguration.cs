using System.Reflection;

namespace Hatch.Helpers;

internal static class CalendarConfiguration
{
#if DEBUG
    // The installed Store app may own hatch:// on the developer's PC.
    internal const string RedirectUri = "hatch-dev://calendar-auth-callback";
#else
    internal const string RedirectUri = "hatch://calendar-auth-callback";
#endif
    internal const string Scope = "https://graph.microsoft.com/Calendars.Read offline_access";
    internal const string GoogleScope = "https://www.googleapis.com/auth/calendar.events.owned.readonly https://www.googleapis.com/auth/calendar.calendarlist.readonly";

    internal static string ClientId { get; } = typeof(CalendarConfiguration).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "HatchCalendarClientId")?.Value ?? string.Empty;
    internal static string GoogleClientId { get; } = typeof(CalendarConfiguration).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "HatchGoogleCalendarClientId")?.Value ?? string.Empty;
    internal static string GoogleClientSecret { get; } = typeof(CalendarConfiguration).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "HatchGoogleCalendarClientSecret")?.Value ?? string.Empty;
}
