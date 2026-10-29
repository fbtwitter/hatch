using Hatch.Helpers;
using Hatch.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class CalendarAgendaTests
{
    [TestMethod]
    public void DayMembership_RespectsLocalTimeExclusiveEndsAndLiteralAllDayDates()
    {
        var day = new DateOnly(2026, 10, 27);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test +7", TimeSpan.FromHours(7), "Test +7", "Test +7");
        var midnight = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.FromHours(7));
        var item = new CalendarEvent { Start = midnight.AddHours(-1), End = midnight };
        Assert.IsFalse(CalendarAgenda.IncludesDay(item, day, zone));
        Assert.IsTrue(CalendarAgenda.IncludesDay(item, day.AddDays(-1), zone));
        Assert.IsTrue(CalendarAgenda.IncludesDay(item with { End = midnight.AddMinutes(1) }, day, zone));
        Assert.IsTrue(CalendarAgenda.IncludesDay(item with { Start = midnight, End = midnight }, day, zone));
        item = item with { IsAllDay = true, AllDayStart = day, AllDayEnd = day.AddDays(2) };
        Assert.IsTrue(CalendarAgenda.IncludesDay(item, day, zone));
        Assert.IsTrue(CalendarAgenda.IncludesDay(item, day.AddDays(1), zone));
        Assert.IsFalse(CalendarAgenda.IncludesDay(item, day.AddDays(2), zone));
    }

    [TestMethod]
    public void QuickJoin_ChoosesOngoingThenNextSafeTimedMeetingOnlyForToday()
    {
        var now = new DateTimeOffset(DateTime.Today.AddHours(12));
        var day = DateOnly.FromDateTime(now.LocalDateTime);
        var ongoing = new CalendarEvent { Id = "ongoing", Start = now.AddMinutes(-1), End = now.AddMinutes(10), MeetingUrl = "https://meet.google.com/sample" };
        var later = ongoing with { Id = "later", Start = now.AddMinutes(15), End = now.AddMinutes(30) };
        var invalid = later with { Id = "invalid", Start = now, MeetingUrl = "file:///private" };
        var ended = ongoing with { Id = "ended", End = now };
        var allDay = ongoing with { Id = "all-day", IsAllDay = true, AllDayStart = day, AllDayEnd = day.AddDays(1) };
        CalendarEvent[] events = [later, invalid, ended, allDay, ongoing];
        Assert.AreSame(ongoing, CalendarAgenda.QuickJoin(events, day, now));
        Assert.IsNull(CalendarAgenda.QuickJoin(events, day.AddDays(1), now));
        Assert.AreSame(later, CalendarAgenda.QuickJoin([later, invalid, ended, allDay], day, now));
        Assert.IsTrue(CalendarAgenda.HasEnded(ended, now));
        Assert.IsFalse(CalendarAgenda.HasEnded(allDay, now));
    }

    [TestMethod]
    public void AgendaDate_UsesTodayForOngoingOrMultiDayEventsAndStartDateOtherwise()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test +7", TimeSpan.FromHours(7), "Test +7", "Test +7");
        var today = new DateOnly(2026, 10, 27);
        var now = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.FromHours(7));
        var yesterday = new DateTimeOffset(2026, 10, 26, 23, 0, 0, TimeSpan.FromHours(7));

        var ongoing = new CalendarEvent { Start = yesterday, End = now.AddHours(2) };
        var tomorrow = new CalendarEvent { Start = now.AddDays(1), End = now.AddDays(1).AddHours(1) };
        var allDay = new CalendarEvent
        {
            IsAllDay = true,
            AllDayStart = today.AddDays(-1),
            AllDayEnd = today.AddDays(2)
        };

        Assert.AreEqual(today, CalendarAgenda.AgendaDate(ongoing, now, zone));
        Assert.AreEqual(today.AddDays(1), CalendarAgenda.AgendaDate(tomorrow, now, zone));
        Assert.AreEqual(today, CalendarAgenda.AgendaDate(allDay, now, zone));
    }
}
