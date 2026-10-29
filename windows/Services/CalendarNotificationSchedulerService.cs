using Hatch.Helpers;
using Hatch.Models;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Hatch.Services;

internal sealed class CalendarNotificationSchedulerService
{
    private const int ScheduledToastLimit = 4096;
    private bool _hadCalendarEvents;
    private string? _lastFailure;

    internal string? Reconcile(IEnumerable<CalendarEvent> events)
    {
        if (AppDataPath.IsUiTest) return null;
        var snapshot = events.ToArray();
        _hadCalendarEvents |= snapshot.Length > 0;
        var stage = "create notifier";
        try
        {
            var now = DateTimeOffset.UtcNow;
            var notifier = ToastNotificationManager.CreateToastNotifier();
            stage = "read scheduled notifications";
            var scheduled = notifier.GetScheduledToastNotifications();
            var own = scheduled.Where(n => n.Tag.StartsWith(CalendarReminderPlanner.TagPrefix, StringComparison.Ordinal)).ToArray();
            var available = Math.Max(0, ScheduledToastLimit - (scheduled.Count - own.Length));
            var candidates = CalendarReminderPlanner.CreateCandidates(snapshot, now)
                .Where(r => CalendarReminderPlanner.CanScheduleNew(r, now) || own.Any(n => Matches(n, r))).ToArray();
            var desired = candidates.Take(available).ToDictionary(r => r.Tag, StringComparer.Ordinal);
            var kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pending in own)
            {
                if (desired.TryGetValue(pending.Tag, out var reminder) && Matches(pending, reminder) && kept.Add(pending.Tag)) continue;
                stage = "remove stale reminder";
                notifier.RemoveFromSchedule(pending);
            }
            var failed = false;
            foreach (var reminder in desired.Values)
            {
                if (kept.Contains(reminder.Tag) || !CalendarReminderPlanner.CanScheduleNew(reminder, DateTimeOffset.UtcNow)) continue;
                stage = "build reminder toast";
                try
                {
                    var xml = new XmlDocument();
                    xml.LoadXml(CalendarReminderPlanner.BuildToastXml(reminder.Event,
                        Strings.Get("Calendar_ReminderBody"), Strings.Get("Calendar_ReminderOpenEvent"),
                        Strings.Get("Calendar_ReminderJoinMeeting"), Strings.Get("Calendar_OpenCalendar"), Strings.Get("Calendar_Untitled")));
                    notifier.AddToSchedule(new ScheduledToastNotification(xml, reminder.DeliveryTime)
                    {
                        Tag = reminder.Tag, Id = reminder.Fingerprint, Group = "calendar",
                        ExpirationTime = reminder.Event.Start
                    });
                }
                catch (Exception ex)
                {
                    LogFailure("add scheduled reminder", ex);
                    failed = true;
                }
            }
            if (failed) return Strings.Get("Calendar_ErrorNotifications");
            if (candidates.Length > available) return Strings.Get("Calendar_ReminderCapacity");
            stage = "read notification setting";
            if (candidates.Length > 0 && notifier.Setting != NotificationSetting.Enabled)
                return Strings.Get("Calendar_NotificationsDisabled");
            _hadCalendarEvents = snapshot.Length > 0;
            return null;
        }
        catch (Exception ex)
        {
            LogFailure(stage, ex);
            return _hadCalendarEvents ? Strings.Get("Calendar_ErrorNotifications") : null;
        }
    }

    private void LogFailure(string stage, Exception error)
    {
        var signature = $"{stage}:{error.GetType().FullName}:{error.HResult:X8}";
        if (signature == _lastFailure) return;
        _lastFailure = signature;
        CrashLog.Write("Calendar reminders", $"{stage}: {error.GetType().Name} (HRESULT 0x{error.HResult:X8}).");
    }

    private static bool Matches(ScheduledToastNotification pending, CalendarReminderPlanner.Reminder reminder) =>
        pending.DeliveryTime == reminder.DeliveryTime && pending.Id == reminder.Fingerprint;
}
