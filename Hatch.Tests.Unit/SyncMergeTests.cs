using Hatch.Models;
using Hatch.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class SyncMergeTests
{
    private static TodoItem Task(Guid id, string title, DateTimeOffset updatedAt) => new()
    {
        Id = id,
        Title = title,
        UpdatedAt = updatedAt
    };

    [TestMethod]
    public void Merge_KeepsTasksUniqueToEachSide()
    {
        var onlyLocalId = Guid.NewGuid();
        var onlyServerId = Guid.NewGuid();
        var local = new TasksFile { Tasks = [Task(onlyLocalId, "local only", DateTimeOffset.UtcNow)] };
        var server = new TasksFile { Tasks = [Task(onlyServerId, "server only", DateTimeOffset.UtcNow)] };

        var merged = SyncMerge.Merge(local, server);

        Assert.AreEqual(2, merged.Tasks.Count);
        Assert.IsTrue(merged.Tasks.Any(t => t.Id == onlyLocalId));
        Assert.IsTrue(merged.Tasks.Any(t => t.Id == onlyServerId));
    }

    [TestMethod]
    public void Merge_SameId_KeepsLaterUpdatedAt_LocalWins()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var local = new TasksFile { Tasks = [Task(id, "edited locally", now)] };
        var server = new TasksFile { Tasks = [Task(id, "stale server copy", now.AddMinutes(-5))] };

        var merged = SyncMerge.Merge(local, server);

        Assert.AreEqual(1, merged.Tasks.Count);
        Assert.AreEqual("edited locally", merged.Tasks[0].Title);
    }

    [TestMethod]
    public void Merge_SameId_KeepsLaterUpdatedAt_ServerWins()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var local = new TasksFile { Tasks = [Task(id, "stale local copy", now.AddMinutes(-5))] };
        var server = new TasksFile { Tasks = [Task(id, "edited on another device", now)] };

        var merged = SyncMerge.Merge(local, server);

        Assert.AreEqual(1, merged.Tasks.Count);
        Assert.AreEqual("edited on another device", merged.Tasks[0].Title);
    }

    [TestMethod]
    public void Merge_NeverDropsData_UnionCountMatchesDistinctIds()
    {
        var shared = Guid.NewGuid();
        var local = new TasksFile
        {
            Tasks =
            [
                Task(shared, "local version", DateTimeOffset.UtcNow),
                Task(Guid.NewGuid(), "local extra", DateTimeOffset.UtcNow)
            ]
        };
        var server = new TasksFile
        {
            Tasks =
            [
                Task(shared, "server version", DateTimeOffset.UtcNow.AddMinutes(-1)),
                Task(Guid.NewGuid(), "server extra", DateTimeOffset.UtcNow)
            ]
        };

        var merged = SyncMerge.Merge(local, server);

        Assert.AreEqual(3, merged.Tasks.Count); // shared (deduped) + 1 local-only + 1 server-only
    }

    [TestMethod]
    public void Merge_EmptyServer_ReturnsLocalUnchanged()
    {
        var local = new TasksFile { Tasks = [Task(Guid.NewGuid(), "only task", DateTimeOffset.UtcNow)] };
        var server = new TasksFile();

        var merged = SyncMerge.Merge(local, server);

        Assert.AreEqual(1, merged.Tasks.Count);
    }
}
