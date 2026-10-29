using System.Xml.Linq;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class CalendarReminderPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static CalendarEvent Meeting() => new()
    {
        Id = "occurrence", AccountId = "account", CalendarId = "calendar", CalendarName = "Work",
        Subject = "Planning", Start = Now.AddHours(1), End = Now.AddHours(2),
        EventUrl = "https://outlook.office.com/event", MeetingUrl = "https://teams.microsoft.com/meeting"
    };

    [TestMethod]
    public void ReminderIsScheduledExactlyTenMinutesBeforeTheEvent()
    {
        var reminder = CalendarReminderPlanner.CreateCandidates([Meeting()], Now).Single();
        Assert.AreEqual(Now.AddMinutes(50), reminder.DeliveryTime);
        Assert.IsTrue(CalendarReminderPlanner.CanScheduleNew(reminder, Now));
    }

    [TestMethod]
    public void AllDayAndEndedEventsDoNotProduceReminders()
    {
        var allDay = Meeting() with { IsAllDay = true };
        var ended = Meeting() with { Start = Now.AddHours(-2), End = Now.AddHours(-1) };
        Assert.IsEmpty(CalendarReminderPlanner.CreateCandidates([allDay, ended], Now));
    }

    [TestMethod]
    public void DuplicateOccurrenceProducesOneStablePendingIdentity()
    {
        var first = CalendarReminderPlanner.CreateCandidates([Meeting(), Meeting()], Now).Single();
        var restarted = CalendarReminderPlanner.CreateCandidates([Meeting()], Now.AddMinutes(5)).Single();
        Assert.AreEqual(first, restarted);
        Assert.IsTrue(first.Tag.Length <= 16);
        Assert.IsTrue(first.Fingerprint.Length <= 16);
    }

    [TestMethod]
    public void AccountAndCalendarArePartOfTheReminderIdentity()
    {
        var reminders = CalendarReminderPlanner.CreateCandidates([
            Meeting(), Meeting() with { AccountId = "another-account" }, Meeting() with { CalendarId = "another-calendar" }
        ], Now);
        Assert.HasCount(3, reminders.Select(r => r.Tag).Distinct().ToArray());
    }

    [TestMethod]
    public void ReminderIdentifierFitsWindowsScheduledToastLimit()
    {
        var reminder = CalendarReminderPlanner.CreateCandidates([Meeting()], Now).Single();
        Assert.IsTrue(reminder.Fingerprint.Length <= 15);
    }

    [TestMethod]
    public void RescheduleReplacesTheTimeUnderTheSameOccurrenceTag()
    {
        var before = CalendarReminderPlanner.CreateCandidates([Meeting()], Now).Single();
        var after = CalendarReminderPlanner.CreateCandidates([Meeting() with { Start = Now.AddHours(2) }], Now).Single();
        Assert.AreEqual(before.Tag, after.Tag);
        Assert.AreNotEqual(before.DeliveryTime, after.DeliveryTime);
        Assert.AreNotEqual(before.Fingerprint, after.Fingerprint);
    }

    [TestMethod]
    public void ChangedMeetingLinkInvalidatesThePendingPayload()
    {
        var before = CalendarReminderPlanner.CreateCandidates([Meeting()], Now).Single();
        var after = CalendarReminderPlanner.CreateCandidates([Meeting() with { MeetingUrl = "https://teams.microsoft.com/new-link" }], Now).Single();
        Assert.AreEqual(before.Tag, after.Tag);
        Assert.AreNotEqual(before.Fingerprint, after.Fingerprint);
    }

    [TestMethod]
    public void AlreadyTriggeredReminderCanBeRetainedButCannotBeScheduledAgain()
    {
        var later = Now.AddMinutes(52);
        var recent = CalendarReminderPlanner.CreateCandidates([Meeting()], later).Single();
        Assert.IsFalse(CalendarReminderPlanner.CanScheduleNew(recent, later));
        Assert.IsEmpty(CalendarReminderPlanner.CreateCandidates([Meeting()], Now.AddMinutes(56)));
    }

    [TestMethod]
    public void NearestRemindersComeFirstWhenTheWindowsQueueHasLimitedRoom()
    {
        var reminders = CalendarReminderPlanner.CreateCandidates([
            Meeting() with { Id = "later", Start = Now.AddHours(3) }, Meeting()
        ], Now);
        Assert.AreEqual("occurrence", reminders.Take(1).Single().Event.Id);
        Assert.IsEmpty(CalendarReminderPlanner.CreateCandidates([], Now));
    }

    [TestMethod]
    public void ToastContentEscapesEventTextAndOffersEventAndMeetingActions()
    {
        var item = Meeting() with { Subject = "Review <notes> & \"changes\"", MeetingUrl = "https://teams.microsoft.com/join?a=1&b=2" };
        var xml = XDocument.Parse(CalendarReminderPlanner.BuildToastXml(item, "Starts in 10 minutes", "Open event", "Join meeting", "Open calendar", "Untitled"));
        Assert.AreEqual(item.Subject, xml.Descendants("text").First().Value);
        Assert.AreEqual(item.EventUrl, xml.Root!.Attribute("launch")!.Value);
        Assert.HasCount(2, xml.Descendants("action").ToArray());
        Assert.IsTrue(xml.Descendants("action").Any(a => a.Attribute("arguments")!.Value == item.MeetingUrl));
    }

    [TestMethod]
    public void UntrustedLinksFallBackToHatchCalendar()
    {
        var item = Meeting() with { EventUrl = "file:///private.txt", MeetingUrl = "hatch://complete?id=anything" };
        var xml = XDocument.Parse(CalendarReminderPlanner.BuildToastXml(item, "Soon", "Open", "Join", "Calendar", "Untitled"));
        Assert.AreEqual("hatch://calendar", xml.Root!.Attribute("launch")!.Value);
        Assert.AreEqual("hatch://calendar", xml.Descendants("action").Single().Attribute("arguments")!.Value);
    }
}
