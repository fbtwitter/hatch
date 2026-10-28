using System.Globalization;
using Hatch.Converters;
using Hatch.Helpers;
using Hatch.Models;
using Microsoft.UI.Xaml.Media;

namespace Hatch.ViewModels;

public sealed partial class CalendarEventRow(CalendarEvent item, string accountEmail, DateTimeOffset now, bool quickJoin = false)
{
    public CalendarEvent Event => item;
    public string AutomationId => "CalendarEvent_" + item.Id;
    public bool IsPast { get; } = HasEnded(item, now);
    public bool ShowQuickJoin => quickJoin && HasMeetingLink && !IsPast;
    public DateTimeOffset SortStart => item.IsAllDay && item.AllDayStart is { } day
        ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue))) : item.Start;
    public string Subject => string.IsNullOrWhiteSpace(item.Subject) ? Strings.Get("Calendar_Untitled") : item.Subject;
    public string CompactTimeLabel => item.IsAllDay ? Strings.Get("Calendar_AllDay") : item.Start.ToLocalTime().ToString("t");
    public string SourceLabel => item.CalendarName + " · " + accountEmail;
    public Brush SubjectBrush => ThemeResourceHelper.GetBrush(IsPast ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
    public double TimelineMarkerOpacity => IsPast ? 0.4 : 1;
    public string AgendaTimeLabel => !IsPast && !item.IsAllDay && item.Start.ToLocalTime().Date == item.End.ToLocalTime().Date
        ? item.Start.ToLocalTime().ToString("t") + " – " + item.End.ToLocalTime().ToString("t")
        : !IsPast && item.IsAllDay && item.AllDayStart is { } first && item.AllDayEnd == first.AddDays(1)
            ? Strings.Get("Calendar_AllDay") : TimeLabel;
    public string TimeLabel
    {
        get
        {
            if (item.IsAllDay)
            {
                var date = item.AllDayStart?.ToString("D", CultureInfo.CurrentCulture) ?? item.Start.ToLocalTime().ToString("D");
                if (item.AllDayStart is { } first && item.AllDayEnd is { } last && last.DayNumber > first.DayNumber + 1)
                    date += " – " + last.AddDays(-1).ToString("D", CultureInfo.CurrentCulture);
                return date + " · " + Strings.Get("Calendar_AllDay");
            }
            var start = item.Start.ToLocalTime();
            var end = item.End.ToLocalTime();
            return start.ToString("ddd d MMM", CultureInfo.CurrentCulture) + " · " + start.ToString("t") + " – " +
                (start.Date == end.Date ? end.ToString("t") : end.ToString("ddd d MMM") + " · " + end.ToString("t"));
        }
    }
    public Uri? EventUri => SafeUri(item.EventUrl);
    public Uri? MeetingUri => SafeUri(item.MeetingUrl);
    public bool HasEventLink => EventUri != null;
    public bool HasMeetingLink => MeetingUri != null;
    public string OpenName => string.Format(Strings.Get("Calendar_OpenName"), Subject);
    public string JoinName => string.Format(Strings.Get("Calendar_JoinName"), Subject);
    public string DetailsName => string.Format(Strings.Get("Calendar_DetailsName"), Subject);

    internal static bool HasEnded(CalendarEvent item, DateTimeOffset now) => CalendarAgenda.HasEnded(item, now);

    private static Uri? SafeUri(string? value) => CalendarEventParser.HttpsUrl(value) is { } url ? new Uri(url) : null;
}
