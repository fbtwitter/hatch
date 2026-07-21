using Hatch.Helpers;
using Hatch.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class RecurrenceHelperTests
{
    private static DateTimeOffset Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Daily_AdvancesByOneDay()
    {
        var next = RecurrenceHelper.AdvanceDueDate(Utc(2026, 7, 10), TaskRecurrence.Daily);
        Assert.AreEqual(Utc(2026, 7, 11), next);
    }

    [TestMethod]
    public void Weekly_AdvancesBySevenDays()
    {
        var next = RecurrenceHelper.AdvanceDueDate(Utc(2026, 7, 10), TaskRecurrence.Weekly);
        Assert.AreEqual(Utc(2026, 7, 17), next);
    }

    [TestMethod]
    public void Monthly_AdvancesByOneCalendarMonth()
    {
        var next = RecurrenceHelper.AdvanceDueDate(Utc(2026, 7, 31), TaskRecurrence.Monthly);
        // Aug has 31 days too, but Jan 31 -> Feb should clamp; verify the BCL's own AddMonths behavior is used
        Assert.AreEqual(Utc(2026, 8, 31), next);
    }

    [TestMethod]
    public void Weekdays_FridayAdvancesToMonday()
    {
        // 2026-07-10 is a Friday
        var next = RecurrenceHelper.AdvanceDueDate(Utc(2026, 7, 10), TaskRecurrence.Weekdays);
        Assert.AreEqual(DayOfWeek.Monday, next.DayOfWeek);
        Assert.AreEqual(Utc(2026, 7, 13), next);
    }

    [TestMethod]
    public void Weekdays_MondayAdvancesToTuesday()
    {
        // 2026-07-13 is a Monday
        var next = RecurrenceHelper.AdvanceDueDate(Utc(2026, 7, 13), TaskRecurrence.Weekdays);
        Assert.AreEqual(Utc(2026, 7, 14), next);
    }

    [TestMethod]
    public void None_ReturnsSameCalendarDate()
    {
        var due = Utc(2026, 7, 10);
        var next = RecurrenceHelper.AdvanceDueDate(due, TaskRecurrence.None);
        Assert.AreEqual(due, next);
    }

    [TestMethod]
    public void Result_IsAlwaysZeroOffset()
    {
        // Input has a non-UTC-zero offset; output must normalize to +00:00 regardless,
        // matching the convention used by the calendar picker and date-chip flyout.
        var due = new DateTimeOffset(2026, 7, 10, 23, 0, 0, TimeSpan.FromHours(-5));
        var next = RecurrenceHelper.AdvanceDueDate(due, TaskRecurrence.Daily);
        Assert.AreEqual(TimeSpan.Zero, next.Offset);
    }
}
