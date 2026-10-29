using Hatch.Helpers;
using Hatch.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class IcsCalendarImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static string Calendar(string events) => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Hatch tests//EN\r\n" + events + "\r\nEND:VCALENDAR";
    private static CalendarAccount Read(string events) => IcsCalendarImport.Parse(Calendar(events), "sample.ics", "sample", Now);

    [TestMethod]
    public void FoldedEscapedTextAndMeetingLinksAreProjectedWithoutRawDescriptions()
    {
        var result = Read("BEGIN:VEVENT\r\nUID:one\r\nDTSTART:20261026T100000Z\r\nDTEND:20261026T110000Z\r\nSUMMARY:Design\\, planning\\;\r\n continued\r\nDESCRIPTION:Join https://teams.microsoft.com/meet/123?x=1&y=2\r\nURL:http://unsafe.invalid\r\nEND:VEVENT");
        var item = result.Events.Single();
        Assert.AreEqual("Design, planning;continued", item.Subject);
        Assert.AreEqual("https://teams.microsoft.com/meet/123?x=1&y=2", item.MeetingUrl);
        Assert.IsNull(item.EventUrl);
        Assert.AreEqual(CalendarProvider.Ics, result.Provider);
    }

    [TestMethod]
    public void AllDayDatesRemainLiteralAndUseAnExclusiveEnd()
    {
        var item = Read("BEGIN:VEVENT\r\nUID:day\r\nDTSTART;VALUE=DATE:20261026\r\nSUMMARY:Research\r\nEND:VEVENT").Events.Single();
        Assert.IsTrue(item.IsAllDay);
        Assert.AreEqual(new DateOnly(2026, 10, 26), item.AllDayStart);
        Assert.AreEqual(new DateOnly(2026, 10, 27), item.AllDayEnd);
    }

    [TestMethod]
    public void RecurrencesApplyExclusionsReschedulesAndCancellations()
    {
        var events = "BEGIN:VEVENT\r\nUID:series\r\nDTSTART:20261026T100000Z\r\nDTEND:20261026T110000Z\r\nRRULE:FREQ=DAILY;COUNT=5\r\nEXDATE:20261027T100000Z\r\nSUMMARY:Original\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:series\r\nRECURRENCE-ID:20261028T100000Z\r\nDTSTART:20261028T120000Z\r\nDTEND:20261028T130000Z\r\nSUMMARY:Moved\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:series\r\nRECURRENCE-ID:20261029T100000Z\r\nSTATUS:CANCELLED\r\nEND:VEVENT";
        var result = Read(events);
        Assert.HasCount(3, result.Events);
        Assert.AreEqual(12, result.Events.Single(e => e.Subject == "Moved").Start.UtcDateTime.Hour);
        Assert.IsFalse(result.Events.Any(e => e.Start.Day == 27 || e.Start.Day == 29));
    }

    [TestMethod]
    public void WeeklyRecurrencePreservesLocalTimeAcrossDaylightSaving()
    {
        var result = Read("BEGIN:VEVENT\r\nUID:dst\r\nDTSTART;TZID=America/New_York:20261026T090000\r\nDTEND;TZID=America/New_York:20261026T100000\r\nRRULE:FREQ=WEEKLY;COUNT=2\r\nSUMMARY:DST review\r\nEND:VEVENT");
        CollectionAssert.AreEqual(new[] { 13, 14 }, result.Events.OrderBy(e => e.Start).Select(e => e.Start.UtcDateTime.Hour).ToArray());
    }

    [TestMethod]
    public void LatestSequenceWinsAndRescheduledSingleEventKeepsItsIdentity()
    {
        var original = "BEGIN:VEVENT\r\nUID:stable\r\nSEQUENCE:0\r\nDTSTART:20261026T100000Z\r\nSUMMARY:Old\r\nEND:VEVENT";
        var newer = "BEGIN:VEVENT\r\nUID:stable\r\nSEQUENCE:1\r\nDTSTART:20261026T120000Z\r\nSUMMARY:New\r\nEND:VEVENT";
        var first = Read(original).Events.Single();
        var changed = Read(original + "\r\n" + newer).Events.Single();
        Assert.AreEqual("New", changed.Subject);
        Assert.AreEqual(first.Id, changed.Id);
        Assert.AreNotEqual(first.Start, changed.Start);
    }

    [TestMethod]
    public void EmptySnapshotAndCancelledMasterAreValid()
    {
        Assert.IsEmpty(Read("").Events);
        Assert.IsEmpty(Read("BEGIN:VEVENT\r\nUID:cancelled\r\nSTATUS:CANCELLED\r\nEND:VEVENT").Events);
    }

    [TestMethod]
    public void InvalidFileAndExcessiveRecurrenceAreRejected()
    {
        Assert.ThrowsExactly<FormatException>(() => IcsCalendarImport.Parse("not a calendar", "bad.ics", "sample", Now));
        Assert.ThrowsExactly<FormatException>(() => Read("BEGIN:VEVENT\r\nSUMMARY:Missing identity and time\r\nEND:VEVENT"));
        Assert.ThrowsExactly<FormatException>(() => Read("BEGIN:VEVENT\r\nUID:many\r\nDTSTART:20261026T100000Z\r\nRRULE:FREQ=SECONDLY;COUNT=20000\r\nEND:VEVENT"));
    }

    [TestMethod]
    public void WeeklyIntervalAndMonthlyPositionsMatchExportedRules()
    {
        var weekly = Read("BEGIN:VEVENT\r\nUID:weekly\r\nDTSTART:20261027T100000Z\r\nRRULE:FREQ=WEEKLY;INTERVAL=2;COUNT=4;BYDAY=TU,TH;WKST=MO\r\nEND:VEVENT");
        CollectionAssert.AreEqual(new[] { "20261027", "20261029", "20261110", "20261112" },
            weekly.Events.Select(e => e.Start.ToString("yyyyMMdd")).ToArray());
        var monthly = Read("BEGIN:VEVENT\r\nUID:monthly\r\nDTSTART:20260930T100000Z\r\nRRULE:FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1\r\nEND:VEVENT");
        Assert.AreEqual("20261030", monthly.Events.Single().Start.ToString("yyyyMMdd"));
        var secondMonday = Read("BEGIN:VEVENT\r\nUID:ordinal\r\nDTSTART:20261012T100000Z\r\nRRULE:FREQ=MONTHLY;BYDAY=2MO\r\nEND:VEVENT");
        Assert.AreEqual("20261109", secondMonday.Events.Single().Start.ToString("yyyyMMdd"));
    }

    [TestMethod]
    public void RecurrenceDatesExclusionsAndUntilAreBounded()
    {
        var result = Read("BEGIN:VEVENT\r\nUID:dates\r\nDTSTART:20261026T100000Z\r\nRRULE:FREQ=DAILY;UNTIL=20261028T100000Z\r\nEXDATE:20261027T100000Z\r\nRDATE:20261030T100000Z\r\nEND:VEVENT");
        CollectionAssert.AreEqual(new[] { 26, 28, 30 }, result.Events.Select(e => e.Start.Day).ToArray());
    }

    [TestMethod]
    public void WindowsTimezoneAndNominalDurationAreSupported()
    {
        var windows = Read("BEGIN:VEVENT\r\nUID:windows\r\nDTSTART;TZID=Tokyo Standard Time:20261026T100000\r\nDURATION:PT45M\r\nEND:VEVENT").Events.Single();
        Assert.AreEqual(1, windows.Start.UtcDateTime.Hour);
        Assert.AreEqual(TimeSpan.FromMinutes(45), windows.End - windows.Start);
        var dst = Read("BEGIN:VEVENT\r\nUID:duration\r\nDTSTART;TZID=America/New_York:20261101T003000\r\nDURATION:P1D\r\nEND:VEVENT").Events.Single();
        Assert.AreEqual(TimeSpan.FromHours(25), dst.End - dst.Start);
    }

    [TestMethod]
    public void DateOnlyMultiDayAndFloatingTimesKeepTheirMeaning()
    {
        var allDay = Read("BEGIN:VEVENT\r\nUID:multi\r\nDTSTART;VALUE=DATE:20261026\r\nDTEND;VALUE=DATE:20261029\r\nEND:VEVENT").Events.Single();
        Assert.AreEqual(new DateOnly(2026, 10, 29), allDay.AllDayEnd);
        var floating = Read("BEGIN:VEVENT\r\nUID:float\r\nDTSTART:20261026T100000\r\nEND:VEVENT").Events.Single();
        Assert.AreEqual(10, floating.Start.ToLocalTime().Hour);
    }

    [TestMethod]
    public void UnsupportedRulesUnknownTimezoneAndInvalidDateRejectTheFile()
    {
        foreach (var fields in new[] { "DTSTART:20261326T100000Z", "DTSTART;TZID=Not/A_Real_Zone:20261026T100000",
            "DTSTART:20261026T100000Z\r\nRRULE:FREQ=DAILY;BYHOUR=12",
            "DTSTART:20261026T100000Z\r\nRECURRENCE-ID;RANGE=THISANDFUTURE:20261026T100000Z" })
            Assert.ThrowsExactly<FormatException>(() => Read("BEGIN:VEVENT\r\nUID:invalid\r\n" + fields + "\r\nEND:VEVENT"));
    }

    [TestMethod]
    public void SameStartMeetingsStaySeparateAndUnsafeMeetingHostsAreNotPromoted()
    {
        var result = Read("BEGIN:VEVENT\r\nUID:first\r\nDTSTART:20261026T100000Z\r\nDESCRIPTION:https://teams.microsoft.com.evil.invalid/meeting\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:second\r\nDTSTART:20261026T100000Z\r\nEND:VEVENT");
        Assert.HasCount(2, result.Events);
        Assert.IsTrue(result.Events.All(e => e.MeetingUrl == null));
        Assert.HasCount(2, result.Events.Select(e => e.Id).Distinct().ToList());
    }

    [TestMethod]
    public void LargerGoogleExportsFitWithinTheBoundedFileLimit()
    {
        var result = Read("BEGIN:VEVENT\r\nUID:large\r\nDTSTART:20261026T100000Z\r\nDESCRIPTION:" + new string('x', 2 * 1024 * 1024) + "\r\nEND:VEVENT");
        Assert.HasCount(1, result.Events);
        Assert.ThrowsExactly<FormatException>(() => IcsCalendarImport.Parse(new string('x', IcsCalendarImport.MaxFileBytes + 1), "large.ics", "sample", Now));
    }

    [TestMethod]
    public void UnsupportedExclusionRuleRejectsTheWholeSnapshot()
    {
        Assert.ThrowsExactly<FormatException>(() => Read("BEGIN:VEVENT\r\nUID:excluded\r\nDTSTART:20261026T100000Z\r\nRRULE:FREQ=DAILY;COUNT=5\r\nEXRULE:FREQ=DAILY;COUNT=5\r\nEND:VEVENT"));
    }

    [TestMethod]
    public void DevelopmentAndProductionCallbacksStaySeparate()
    {
        Assert.IsTrue(CalendarOAuthCallback.IsExpectedRedirect(new("hatch-dev://calendar-auth-callback?code=test"), "hatch-dev://calendar-auth-callback"));
        Assert.IsFalse(CalendarOAuthCallback.IsExpectedRedirect(new("hatch://calendar-auth-callback?code=test"), "hatch-dev://calendar-auth-callback"));
        Assert.IsFalse(CalendarOAuthCallback.IsExpectedRedirect(new("hatch-dev://calendar-auth-callback/other?code=test"), "hatch-dev://calendar-auth-callback"));
    }
}
