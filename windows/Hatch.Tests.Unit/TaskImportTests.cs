using Hatch.Helpers;
using Hatch.Models;
using Hatch.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class TaskImportTests
{
    private static readonly DateTimeOffset Old = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset New = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Parse_RoundTripsAnExport()
    {
        var source = new TasksFile { Tasks = [new TodoItem { Title = "a" }], Lists = [new TaskList { Name = "L" }] };
        var parsed = TaskImport.Parse(TaskExportFormatter.ToJson(source));

        Assert.IsNotNull(parsed);
        Assert.AreEqual(1, parsed.Tasks.Count);
        Assert.AreEqual("a", parsed.Tasks[0].Title);
    }

    [TestMethod]
    public void Parse_ReturnsNullForGarbage()
    {
        Assert.IsNull(TaskImport.Parse("not json at all"));
        Assert.IsNull(TaskImport.Parse("null"));
        Assert.IsNull(TaskImport.Parse("[1, 2, 3]"));
    }

    [TestMethod]
    public void Merge_AddsNewTasksAndReportsCount()
    {
        var local = new TasksFile { Tasks = [new TodoItem { Title = "local" }] };
        var imported = new TasksFile { Tasks = [new TodoItem { Title = "incoming" }] };

        var (merged, applied) = TaskImport.Merge(local, imported);

        Assert.AreEqual(2, merged.Tasks.Count);
        Assert.AreEqual(1, applied);
    }

    [TestMethod]
    public void Merge_ImportedWinsOnlyWhenStrictlyNewer()
    {
        var id = Guid.NewGuid();
        var local = new TasksFile { Tasks = [new TodoItem { Id = id, Title = "kept", UpdatedAt = New }] };
        var stale = new TasksFile { Tasks = [new TodoItem { Id = id, Title = "stale", UpdatedAt = Old }] };

        var (merged, applied) = TaskImport.Merge(local, stale);

        Assert.AreEqual(1, merged.Tasks.Count);
        Assert.AreEqual("kept", merged.Tasks[0].Title);
        Assert.AreEqual(0, applied);
    }

    [TestMethod]
    public void Merge_ImportedNewerReplacesLocal()
    {
        var id = Guid.NewGuid();
        var local = new TasksFile { Tasks = [new TodoItem { Id = id, Title = "old", UpdatedAt = Old }] };
        var fresh = new TasksFile { Tasks = [new TodoItem { Id = id, Title = "new", UpdatedAt = New }] };

        var (merged, applied) = TaskImport.Merge(local, fresh);

        Assert.AreEqual("new", merged.Tasks[0].Title);
        Assert.AreEqual(1, applied);
    }

    [TestMethod]
    public void Merge_NeverDeletesLocalOnlyTasks()
    {
        var local = new TasksFile { Tasks = [new TodoItem { Title = "mine" }, new TodoItem { Title = "also mine" }] };
        var imported = new TasksFile();

        var (merged, applied) = TaskImport.Merge(local, imported);

        Assert.AreEqual(2, merged.Tasks.Count);
        Assert.AreEqual(0, applied);
    }
}
