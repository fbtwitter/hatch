using Hatch.Helpers;
using Hatch.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class TaskSortingTests
{
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
}
