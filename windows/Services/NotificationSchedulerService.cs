using Hatch.Models;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Hatch.Services;

public sealed class NotificationSchedulerService
{
    private const int DueHour = 9;
    private const int WarnMinutes = 30;

    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromHours(1);

    // Two tag namespaces, deliberately not one prefix. "task-{id}" and "task-{id}-warn" are the
    // due-date pair, rebuilt from scratch on any edit; "snooze-{id}" is a one-off the user asked
    // for by hand. Keeping them apart is what lets a snooze survive an unrelated rebuild — a
    // signed-in client re-schedules every five minutes on the sync pull, so a shared prefix
    // would quietly cancel the snooze long before it fired. Same reasoning as hatch-mobile's
    // separate hatch-due-snooze-$id work name (92f57fb).
    private static string ReminderPrefix(Guid taskId) => $"task-{taskId}";
    private static string SnoozeTag(Guid taskId) => $"snooze-{taskId}";

    public void ScheduleForTask(TodoItem task)
    {
        if (Hatch.Helpers.AppDataPath.IsUiTest) return;
        if (task.DueDate == null || task.IsCompleted)
        {
            UnscheduleForTask(task.Id);
            return;
        }

        var prefix = ReminderPrefix(task.Id);
        RemoveByTag(t => t.StartsWith(prefix, StringComparison.Ordinal));
        try
        {
            AddReminders(ToastNotificationManager.CreateToastNotifier(), task);
        }
        catch { }
    }

    private static void AddReminders(ToastNotifier notifier, TodoItem task)
    {
        if (task.DueDate == null || task.IsCompleted) return;

        var dueTime = GetDueTime(task.DueDate.Value);
        var warnTime = dueTime.AddMinutes(-WarnMinutes);
        var earliest = DateTimeOffset.Now.AddSeconds(30);
        var prefix = ReminderPrefix(task.Id);
        if (dueTime > earliest)
            notifier.AddToSchedule(BuildToast(task.Id, task.Title, dueTime, "Due now", prefix));
        if (warnTime > earliest)
            notifier.AddToSchedule(BuildToast(task.Id, task.Title, warnTime, "Due in 30 minutes", $"{prefix}-warn"));
    }

    // Cancels everything pending for a task, snooze included — the delete and complete paths.
    public void UnscheduleForTask(Guid taskId)
    {
        var prefix = ReminderPrefix(taskId);
        var snooze = SnoozeTag(taskId);
        RemoveByTag(t => t.StartsWith(prefix, StringComparison.Ordinal) ||
                         string.Equals(t, snooze, StringComparison.Ordinal));
    }

    // Re-fires the same reminder later, leaving the due date alone (which is what the task row's
    // own "Snooze" submenu moves instead). Replaces any pending snooze rather than queuing a
    // second one, so snoozing again from the re-fired toast just pushes it out another hour.
    public void SnoozeReminder(Guid taskId, string title, TimeSpan delay)
    {
        if (Hatch.Helpers.AppDataPath.IsUiTest) return;
        CancelSnooze(taskId);
        try
        {
            ToastNotificationManager.CreateToastNotifier()
                .AddToSchedule(BuildToast(taskId, title, DateTimeOffset.Now + delay, "Due now", SnoozeTag(taskId)));
        }
        catch { }
    }

    public void RescheduleAll(IEnumerable<TodoItem> tasks)
    {
        if (Hatch.Helpers.AppDataPath.IsUiTest) return;
        try
        {
            var list = tasks as IList<TodoItem> ?? tasks.ToList();

            var notifier = ToastNotificationManager.CreateToastNotifier();
            // Live snoozes survive; orphans (a task deleted on another device, so absent from
            // this list) do not — the same clean-up the blanket removal used to give.
            var live = list.Where(t => !t.IsCompleted && t.DueDate != null)
                .Select(t => SnoozeTag(t.Id)).ToHashSet(StringComparer.Ordinal);
            foreach (var n in notifier.GetScheduledToastNotifications().ToList())
            {
                if (n.Tag.StartsWith("snooze-", StringComparison.Ordinal) && live.Contains(n.Tag))
                    continue;
                notifier.RemoveFromSchedule(n);
            }

            foreach (var task in list)
            {
                try { AddReminders(notifier, task); }
                catch { }
            }
        }
        catch { }
    }

    private static void CancelSnooze(Guid taskId)
    {
        var tag = SnoozeTag(taskId);
        RemoveByTag(t => string.Equals(t, tag, StringComparison.Ordinal));
    }

    private static void RemoveByTag(Func<string, bool> match)
    {
        if (Hatch.Helpers.AppDataPath.IsUiTest) return;
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier();
            foreach (var n in notifier.GetScheduledToastNotifications().Where(n => match(n.Tag)).ToList())
                notifier.RemoveFromSchedule(n);
        }
        catch { }
    }

    // Date-only due dates (midnight) default to 9 AM on that day; timed due dates use their own hour.
    private static DateTimeOffset GetDueTime(DateTimeOffset dueDate)
    {
        // Midnight is detected on the value as written: converting through ToLocalTime
        // first made Hour non-zero on any non-UTC machine, so a stored midnight-+00:00 due
        // was treated as "timed" and the reminder fired at the raw converted hour — 7 AM
        // on a +07:00 machine, the previous evening west of UTC — never the 9 AM default.
        if (dueDate.Hour == 0 && dueDate.Minute == 0)
        {
            var delivery = dueDate.Date.AddHours(DueHour);
            return new DateTimeOffset(delivery, TimeZoneInfo.Local.GetUtcOffset(delivery));
        }
        return dueDate;
    }

    private static ScheduledToastNotification BuildToast(
        Guid taskId, string taskTitle, DateTimeOffset deliveryTime, string body, string tag)
    {
        var title = EscapeXml(taskTitle);
        var xml = new XmlDocument();
        xml.LoadXml($"""
            <toast activationType="protocol" launch="hatch://opentask?id={taskId}">
              <visual>
                <binding template="ToastGeneric">
                  <text>{title}</text>
                  <text>{body}</text>
                </binding>
              </visual>
              <actions>
                <action content="Mark complete"
                        arguments="hatch://complete?id={taskId}"
                        activationType="protocol" />
                <action content="Remind me in 1 hour"
                        arguments="hatch://snooze?id={taskId}"
                        activationType="protocol" />
              </actions>
            </toast>
            """);
        var toast = new ScheduledToastNotification(xml, deliveryTime);
        toast.Tag = tag;
        return toast;
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
         .Replace("<", "&lt;", StringComparison.Ordinal)
         .Replace(">", "&gt;", StringComparison.Ordinal)
         .Replace("\"", "&quot;", StringComparison.Ordinal);
}
