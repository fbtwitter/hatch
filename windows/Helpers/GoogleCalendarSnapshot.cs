using Hatch.Models;

namespace Hatch.Helpers;

internal sealed record GoogleCalendarSnapshot(List<CalendarEvent> Events, List<CalendarSource> Sources);
