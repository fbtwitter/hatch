using Hatch.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class TaskPropertyChangeTests
{
    [TestMethod]
    public void ReassigningUnchangedFieldsDoesNotReportAnEdit()
    {
        var date = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        var task = new TodoItem { Title = "Review", Notes = "Notes", DueDate = date, Priority = TaskPriority.Low, Recurrence = TaskRecurrence.Weekly };
        var changes = new List<string?>();
        task.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        task.Title = "Review";
        task.Notes = "Notes";
        task.DueDate = date;
        task.Priority = TaskPriority.Low;
        task.Recurrence = TaskRecurrence.Weekly;
        Assert.AreEqual(0, changes.Count);
    }

    [TestMethod]
    public void ActualChangesStillNotifyFieldsAndDerivedMetadata()
    {
        var task = new TodoItem();
        var changes = new HashSet<string?>();
        task.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        task.Title = "Review";
        task.Notes = "Notes";
        task.DueDate = DateTimeOffset.UtcNow;
        task.Priority = TaskPriority.High;
        task.Recurrence = TaskRecurrence.Daily;
        foreach (var property in new[] { nameof(TodoItem.Title), nameof(TodoItem.Notes), nameof(TodoItem.DueDate),
                     nameof(TodoItem.Priority), nameof(TodoItem.Recurrence), nameof(TodoItem.HasNotes),
                     nameof(TodoItem.HasPriority), nameof(TodoItem.HasRecurrence), nameof(TodoItem.ShowAddDateHint) })
            Assert.IsTrue(changes.Contains(property), property + " must notify after a real change.");
    }

    [TestMethod]
    public void DueDateKeepsWrittenDayChangesWhenInstantsMatch()
    {
        var date = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        var task = new TodoItem { DueDate = date };
        bool changed = false;
        task.PropertyChanged += (_, args) => changed |= args.PropertyName == nameof(TodoItem.DueDate);
        task.DueDate = date.ToOffset(TimeSpan.FromHours(-4));
        Assert.IsTrue(changed);
        Assert.AreEqual(new DateTime(2026, 10, 13), task.DueDate!.Value.Date);
        Assert.AreEqual(date.UtcDateTime, task.DueDate.Value.UtcDateTime);
    }
}
