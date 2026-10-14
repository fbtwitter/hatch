using Hatch.Helpers;
using Hatch.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class TaskSortingTests
{
    [TestMethod]
    public void ForMyDaySuggestions_TodayWinsOverALargeOverdueBacklog()
    {
        var today = new DateTime(2026, 10, 14);
        var dueToday = new TodoItem { Title = "Today", DueDate = new DateTimeOffset(today, TimeSpan.Zero) };
        var backlog = Enumerable.Range(1, 15).Select(days => new TodoItem
        {
            Title = "Overdue " + days,
            DueDate = new DateTimeOffset(today.AddDays(-days), TimeSpan.Zero),
            Priority = TaskPriority.High
        });
        var future = new TodoItem { DueDate = new DateTimeOffset(today.AddDays(1), TimeSpan.Zero), Priority = TaskPriority.High };

        var ordered = TaskSorting.ForMyDaySuggestions(backlog.Append(future).Append(dueToday), today).ToList();

        Assert.AreSame(dueToday, ordered[0]);
        Assert.IsTrue(ordered.Skip(1).Take(15).All(t => t.DueDate!.Value.Date < today));
        Assert.AreSame(future, ordered[^1]);
    }

    [TestMethod]
    public void ForMyDaySuggestions_OrdersEachGroupByPriorityDateAndCreationInstant()
    {
        var today = new DateTime(2026, 10, 14);
        var utcNoon = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var newer = MakeTask("Newer", TaskPriority.High, utcNoon);
        newer.DueDate = new DateTimeOffset(today.AddDays(1), TimeSpan.Zero);
        var older = MakeTask("Older", TaskPriority.High, utcNoon.AddMinutes(-10).ToLocalTime());
        older.DueDate = newer.DueDate;
        var later = MakeTask("Later", TaskPriority.High, utcNoon.AddHours(1));
        later.DueDate = new DateTimeOffset(today.AddDays(2), TimeSpan.Zero);
        var undated = MakeTask("Undated", TaskPriority.High, utcNoon.AddHours(2));
        var lowerPriority = MakeTask("Lower", TaskPriority.Low, utcNoon.AddHours(3));
        lowerPriority.DueDate = newer.DueDate;

        var ordered = TaskSorting.ForMyDaySuggestions([lowerPriority, undated, later, older, newer], today).ToList();

        CollectionAssert.AreEqual(new[] { newer, older, later, undated, lowerPriority }, ordered);
    }

    [TestMethod]
    public void ForMyDaySuggestions_UsesWrittenCalendarDayAndExcludesUnavailableTasks()
    {
        var today = new DateTime(2026, 10, 14);
        var dueToday = new TodoItem { DueDate = new DateTimeOffset(today, TimeSpan.FromHours(14)) };
        var dueTomorrow = new TodoItem { DueDate = new DateTimeOffset(today.AddDays(1), TimeSpan.FromHours(14)) };
        var completed = new TodoItem { IsCompleted = true, DueDate = dueToday.DueDate };
        var deleted = new TodoItem { IsDeleted = true, DueDate = dueToday.DueDate };
        var selected = new TodoItem { IsInMyDay = true, DueDate = dueToday.DueDate };

        var ordered = TaskSorting.ForMyDaySuggestions([dueTomorrow, completed, deleted, selected, dueToday], today).ToList();

        CollectionAssert.AreEqual(new[] { dueToday, dueTomorrow }, ordered);
    }

    [TestMethod]
    public void ForMyDaySuggestions_BreaksExactTiesById()
    {
        var today = new DateTime(2026, 10, 14);
        var first = new TodoItem { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), CreatedAt = today };
        var second = new TodoItem { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), CreatedAt = today };

        var ordered = TaskSorting.ForMyDaySuggestions([second, first], today).ToList();

        CollectionAssert.AreEqual(new[] { first, second }, ordered);
    }

    private static TodoItem MakeTask(string title, TaskPriority priority, DateTime createdAt) => new()
    {
        Title = title,
        Priority = priority,
        CreatedAt = createdAt
    };

    [TestMethod]
    public void ForImportant_OrdersByPriorityDescending()
    {
        var low = MakeTask("low", TaskPriority.Low, DateTime.Now);
        var high = MakeTask("high", TaskPriority.High, DateTime.Now);
        var medium = MakeTask("medium", TaskPriority.Medium, DateTime.Now);
        var none = MakeTask("none", TaskPriority.None, DateTime.Now);

        var ordered = TaskSorting.ForImportant([low, high, medium, none]).ToList();

        CollectionAssert.AreEqual(
            new[] { high, medium, low, none },
            ordered);
    }

    [TestMethod]
    public void ForImportant_BreaksTiesByNewestFirst()
    {
        var older = MakeTask("older", TaskPriority.High, DateTime.Now.AddMinutes(-10));
        var newer = MakeTask("newer", TaskPriority.High, DateTime.Now);

        var ordered = TaskSorting.ForImportant([older, newer]).ToList();

        CollectionAssert.AreEqual(new[] { newer, older }, ordered);
    }

    // This app stamps CreatedAt local, the Android companion stamps it UTC. Raw ticks put
    // the phone's task hours behind one created after it on the desktop.
    [TestMethod]
    public void NewestFirst_ComparesInstantsNotTicksAcrossKinds()
    {
        var utcNoon = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);
        var fromPhone = MakeTask("from phone", TaskPriority.None, utcNoon);
        var fromDesktop = MakeTask(
            "from desktop", TaskPriority.None, utcNoon.AddMinutes(-10).ToLocalTime());

        var ordered = TaskSorting.NewestFirst([fromDesktop, fromPhone]).ToList();

        CollectionAssert.AreEqual(new[] { fromPhone, fromDesktop }, ordered);
    }

    [TestMethod]
    public void ForImportant_BreaksTiesByInstantAcrossKinds()
    {
        var utcNoon = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);
        var fromPhone = MakeTask("from phone", TaskPriority.High, utcNoon);
        var fromDesktop = MakeTask(
            "from desktop", TaskPriority.High, utcNoon.AddMinutes(-10).ToLocalTime());

        var ordered = TaskSorting.ForImportant([fromDesktop, fromPhone]).ToList();

        CollectionAssert.AreEqual(new[] { fromPhone, fromDesktop }, ordered);
    }
}
