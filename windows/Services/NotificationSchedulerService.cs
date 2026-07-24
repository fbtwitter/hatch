using Hatch.Models;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Hatch.Services;

public sealed class NotificationSchedulerService
{
    private const int DueHour = 9;
    private const int WarnMinutes = 30;

    public void ScheduleForTask(TodoItem task)
    {
        UnscheduleForTask(task.Id);
        if (task.DueDate == null || task.IsCompleted) return;

        var dueTime = GetDueTime(task.DueDate.Value);
        var warnTime = dueTime.AddMinutes(-WarnMinutes);
        var now = DateTimeOffset.Now;

        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier();
            if (dueTime > now.AddSeconds(30))
                notifier.AddToSchedule(BuildToast(task, dueTime, "Due now", $"task-{task.Id}"));
            if (warnTime > now.AddSeconds(30))
                notifier.AddToSchedule(BuildToast(task, warnTime, "Due in 30 minutes", $"task-{task.Id}-warn"));
        }
        catch { }
    }

    public void UnscheduleForTask(Guid taskId)
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier();
            var prefix = $"task-{taskId}";
            foreach (var n in notifier.GetScheduledToastNotifications()
                                       .Where(n => n.Tag.StartsWith(prefix, StringComparison.Ordinal))
                                       .ToList())
                notifier.RemoveFromSchedule(n);
        }
        catch { }
    }

    public void RescheduleAll(IEnumerable<TodoItem> tasks)
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier();
            foreach (var n in notifier.GetScheduledToastNotifications().ToList())
                notifier.RemoveFromSchedule(n);
            foreach (var task in tasks)
                ScheduleForTask(task);
        }
        catch { }
    }

    // Date-only due dates (midnight) default to 9 AM on that day; timed due dates use their own hour.
    private static DateTimeOffset GetDueTime(DateTimeOffset dueDate)
    {
        var local = dueDate.ToLocalTime();
        if (local.Hour == 0 && local.Minute == 0)
            return new DateTimeOffset(local.Date.AddHours(DueHour), local.Offset);
        return local;
    }

    private static ScheduledToastNotification BuildToast(
        TodoItem task, DateTimeOffset deliveryTime, string body, string tag)
    {
        var title = EscapeXml(task.Title);
        var xml = new XmlDocument();
        xml.LoadXml($"""
            <toast activationType="protocol" launch="hatch://opentask?id={task.Id}">
              <visual>
                <binding template="ToastGeneric">
                  <text>{title}</text>
                  <text>{body}</text>
                </binding>
              </visual>
              <actions>
                <action content="Mark complete"
                        arguments="hatch://complete?id={task.Id}"
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
