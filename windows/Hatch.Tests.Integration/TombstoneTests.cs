namespace Hatch.Tests.Integration;

[TestClass]
public sealed class TombstoneTests
{
    [TestMethod]
    public void Reload_HidesDeletedRecordsButIncludesThemInSyncSnapshot()
    {
        var live = new TodoItem { Title = "Visible" };
        var deleted = new TodoItem { Title = "Deleted", IsDeleted = true };
        var list = new TaskList { Name = "Deleted list", IsDeleted = true };
        var store = new TaskTombstoneStore();
        var visible = store.ResetFrom(new TasksFile { Tasks = [live, deleted], Lists = [list] });
        Assert.AreEqual(live.Id, visible.Tasks.Single().Id);
        Assert.AreEqual(0, visible.Lists.Count);
        var snapshot = store.CreateSnapshot(visible.Tasks, visible.Lists);
        Assert.AreEqual(2, snapshot.Tasks.Count);
        Assert.IsTrue(snapshot.Tasks.Single(t => t.Id == deleted.Id).IsDeleted);
        Assert.IsTrue(snapshot.Lists.Single().IsDeleted);
    }

    [TestMethod]
    public void DeleteThenUndo_WinsAgainstTheOlderDeletionWithoutDuplicates()
    {
        var store = new TaskTombstoneStore();
        var task = new TodoItem { Title = "Restore me" };
        var deletedAt = DateTimeOffset.UtcNow;
        store.MarkDeleted(task, deletedAt);
        var remote = SyncWire.Deserialize(SyncWire.Serialize(store.CreateSnapshot([], [])));
        Assert.IsNotNull(remote);
        store.Restore(task, deletedAt.AddSeconds(1));
        var local = store.CreateSnapshot([task], []);
        Assert.AreEqual(1, local.Tasks.Count);
        var merged = SyncMerge.Merge(local, remote);
        Assert.IsFalse(merged.Tasks.Single().IsDeleted);
        Assert.AreEqual(deletedAt.AddSeconds(1), merged.Tasks.Single().UpdatedAt);
    }

    [TestMethod]
    public void Reload_ReplacesPreviouslyRetainedTombstones()
    {
        var store = new TaskTombstoneStore();
        store.MarkDeleted(new TodoItem(), DateTimeOffset.UtcNow);
        store.ResetFrom(new TasksFile());
        Assert.AreEqual(0, store.CreateSnapshot([], []).Tasks.Count);
    }
}
