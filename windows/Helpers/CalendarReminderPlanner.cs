using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Hatch.Models;

namespace Hatch.Helpers;

internal static class CalendarReminderPlanner
{
    internal const string TagPrefix = "cal-";
    internal static readonly TimeSpan DeliveryWindow = TimeSpan.FromMinutes(5);

    internal sealed record Reminder(string Tag, string Fingerprint, DateTimeOffset DeliveryTime, CalendarEvent Event);

    internal static List<Reminder> CreateCandidates(IEnumerable<CalendarEvent> events, DateTimeOffset now) => events
        .Where(e => !e.IsAllDay && e.Start > now && e.Start.AddMinutes(-10) > now - DeliveryWindow)
        .DistinctBy(e => (e.AccountId, e.CalendarId, e.Id))
        .Select(e => new Reminder(TagPrefix + Digest(Pack(e.AccountId, e.CalendarId, e.Id))[..12],
            Digest(Pack(e.Subject, e.CalendarName, e.EventUrl ?? string.Empty, e.MeetingUrl ?? string.Empty,
                e.Start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)))[..16], e.Start.AddMinutes(-10), e))
        .OrderBy(r => r.DeliveryTime).ThenBy(r => r.Tag, StringComparer.Ordinal).ToList();

    internal static bool CanScheduleNew(Reminder reminder, DateTimeOffset now) => reminder.DeliveryTime > now.AddSeconds(1);

    internal static string BuildToastXml(CalendarEvent item, string body, string openEvent, string joinMeeting,
        string openCalendar, string untitled)
    {
        var eventUrl = CalendarEventParser.HttpsUrl(item.EventUrl);
        var meetingUrl = CalendarEventParser.HttpsUrl(item.MeetingUrl);
        var launch = eventUrl ?? meetingUrl ?? "hatch://calendar";
        var actions = new StringBuilder();
        if (eventUrl != null) Action(openEvent, eventUrl);
        if (meetingUrl != null) Action(joinMeeting, meetingUrl);
        if (actions.Length == 0) Action(openCalendar, "hatch://calendar");
        return $"""
            <toast activationType="protocol" launch="{Escape(launch)}">
              <visual><binding template="ToastGeneric">
                <text>{Escape(string.IsNullOrWhiteSpace(item.Subject) ? untitled : item.Subject)}</text>
                <text>{Escape(body + " · " + item.CalendarName)}</text>
              </binding></visual>
              <actions>{actions}</actions>
            </toast>
            """;

        void Action(string label, string url) => actions.Append("<action content=\"").Append(Escape(label))
            .Append("\" arguments=\"").Append(Escape(url)).Append("\" activationType=\"protocol\" />");
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Pack(params string[] values) => string.Concat(values.Select(v => v.Length.ToString(CultureInfo.InvariantCulture) + ":" + v));
    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
