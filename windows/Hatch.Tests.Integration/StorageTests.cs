using System.Text.Json;

namespace Hatch.Tests.Integration;

[TestClass]
public sealed class StorageTests
{
    // Retain only synthetic fixtures for diagnosing failed runs; never touch app data.
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Hatch.Integration", Guid.NewGuid().ToString("N"));
    private TaskStorageService Storage => new(_folder);
    private string TaskPath => Path.Combine(_folder, "tasks.json");

    [TestMethod]
    public async Task FirstRun_HasNoTasksOrLists()
    {
        var data = await Storage.LoadAsync();
        Assert.AreEqual(0, data.Tasks.Count);
        Assert.AreEqual(0, data.Lists.Count);
    }

    [TestMethod]
    public async Task Save_ReloadsAllUserFieldsThroughANewService()
    {
        var list = new TaskList { Name = "Work" };
        var task = new TodoItem
        {
            Title = "Ship café 🐣", Notes = "first\nsecond", ListId = list.Id,
            IsStarred = true, Priority = TaskPriority.High, Tags = ["work", "release"],
            Recurrence = TaskRecurrence.Weekly, DueDate = DateTimeOffset.UtcNow.AddDays(2),
            Steps = [new Step { Title = "Review", IsCompleted = true }, new Step { Title = "Publish" }],
            UpdatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)
        };
        task.SetMyDay(true);
        var expected = new TasksFile { Tasks = [task], Lists = [list] };
        await Storage.SaveAsync(expected);
        var actual = await Storage.LoadAsync();
        Assert.IsTrue(SyncWire.IsEquivalent(expected, actual), "A fresh service must restore the complete wire state.");
        Assert.AreEqual(task.Notes, actual.Tasks.Single().Notes);
        Assert.AreEqual(task.UpdatedAt, actual.Tasks.Single().UpdatedAt, "Loading must not make a task a newer sync edit.");
    }

    [TestMethod]
    public async Task LegacyArray_MigratesMyDayWithoutLosingTasks()
    {
        _ = Storage;
        var task = new TodoItem { Title = "Legacy", IsInMyDay = true };
        await File.WriteAllTextAsync(TaskPath, JsonSerializer.Serialize(new[] { task }));
        var loaded = await Storage.LoadAsync();
        Assert.AreEqual(task.Id, loaded.Tasks.Single().Id);
        Assert.AreEqual(DateOnly.FromDateTime(DateTime.Today), loaded.Tasks.Single().MyDayDate);
        Assert.AreEqual(0, loaded.Lists.Count);
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(TaskPath));
        Assert.AreEqual(JsonValueKind.Object, persisted.RootElement.ValueKind);
    }

    [TestMethod]
    public async Task NewDay_ResetsMembershipButKeepsDateAndOtherFields()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        var task = new TodoItem { Title = "Carry forward", IsInMyDay = true, MyDayDate = yesterday, IsStarred = true };
        await Storage.SaveAsync(new TasksFile { Tasks = [task] });
        var loaded = (await Storage.LoadAsync()).Tasks.Single();
        Assert.IsFalse(loaded.IsInMyDay);
        Assert.AreEqual(yesterday, loaded.MyDayDate);
        Assert.IsTrue(loaded.IsStarred);
        Assert.AreEqual(task.UpdatedAt, loaded.UpdatedAt);
        Assert.IsFalse((await Storage.LoadAsync()).Tasks.Single().IsInMyDay);
    }

    [TestMethod]
    public async Task CorruptFile_IsBackedUpBeforeSubsequentSave()
    {
        _ = Storage;
        const string damaged = "{\"Tasks\":[{\"Title\":\"recover me\"";
        await File.WriteAllTextAsync(TaskPath, damaged);
        Assert.AreEqual(0, (await Storage.LoadAsync()).Tasks.Count);
        await Storage.SaveAsync(new TasksFile { Tasks = [new TodoItem { Title = "new" }] });
        Assert.AreEqual(damaged, await File.ReadAllTextAsync(TaskPath + ".bak"));
        Assert.AreEqual("new", (await Storage.LoadAsync()).Tasks.Single().Title);
    }

    [TestMethod]
    public async Task ConcurrentSavesAndLoads_NeverExposePartialJson()
    {
        await Storage.SaveAsync(new TasksFile { Tasks = [new TodoItem { Title = "seed" }] });
        var operations = Enumerable.Range(0, 30).Select(async i =>
        {
            var data = new TasksFile { Tasks = [new TodoItem { Title = new string((char)('a' + i % 26), 4096) }] };
            await Storage.SaveAsync(data);
            Assert.AreEqual(1, (await Storage.LoadAsync()).Tasks.Count);
        });
        await Task.WhenAll(operations);
        Assert.AreEqual(1, (await Storage.LoadAsync()).Tasks.Count);
        Assert.IsFalse(File.Exists(TaskPath + ".bak"), "Valid concurrent writes must not trigger corruption recovery.");
    }

    [TestMethod]
    public async Task Settings_ExplicitSaveRestoresPreferences()
    {
        var settings = new SettingsService(_folder);
        settings.Current.Theme = AppTheme.Dark;
        settings.Current.ShowQuickTips = false;
        settings.Current.CustomTips = ["Take a break"];
        settings.Current.TipTopics["planning"] = new() { Dismissals = 3, QuietUntil = DateTime.Today.AddDays(30) };
        await settings.SaveAsync();
        var reloaded = new SettingsService(_folder);
        await reloaded.LoadAsync();
        Assert.AreEqual(JsonSerializer.Serialize(settings.Current), JsonSerializer.Serialize(reloaded.Current));
    }

    [TestMethod]
    public async Task Settings_ExitFlushPersistsTheLastDebouncedChange()
    {
        var settings = new SettingsService(_folder);
        settings.Current.MascotX = 10;
        settings.SaveDebounced();
        settings.Current.MascotX = 450;
        settings.Current.ShowTipsAutomatically = true;
        settings.SaveDebounced();
        settings.FlushPendingSave();
        var reloaded = new SettingsService(_folder);
        await reloaded.LoadAsync();
        Assert.AreEqual(450, reloaded.Current.MascotX);
        Assert.IsTrue(reloaded.Current.ShowTipsAutomatically);
    }

    [TestMethod]
    public async Task Settings_CorruptionFallsBackToOfflineOptInDefaults()
    {
        var settings = new SettingsService(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, "settings.json"), "{truncated");
        await settings.LoadAsync();
        Assert.IsFalse(settings.Current.FirstRunComplete);
        Assert.IsFalse(settings.Current.ShowTipsAutomatically);
        Assert.IsNull(settings.Current.SyncAccessToken);
        Assert.IsNull(settings.Current.SyncRefreshToken);
    }
}
