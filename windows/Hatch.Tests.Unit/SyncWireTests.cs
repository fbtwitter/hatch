using System.Text.Json.Nodes;
using Hatch.Models;
using Hatch.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

// Executable half of docs/sync-protocol.md §4. The golden fixture is the contract:
// the Kotlin core must serialize these same canonical objects to a value-equal payload
// and restore every field from it.
[TestClass]
public class SyncWireTests
{
    private static string GoldenJson => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "tasks-golden.json"));

    private static TasksFile CanonicalData()
    {
        var full = new TodoItem
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Title = "Water the plants",
            IsStarred = true,
            IsInMyDay = true,
            MyDayDate = new DateOnly(2026, 1, 15),
            DueDate = new DateTimeOffset(2026, 1, 16, 9, 0, 0, TimeSpan.Zero),
            ListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Recurrence = TaskRecurrence.Weekly,
            Priority = TaskPriority.High,
            Tags = ["home", "green"],
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            UpdatedAt = new DateTimeOffset(2026, 1, 15, 10, 20, 30, TimeSpan.Zero),
            Notes = "Kitchen and balcony"
        };

        // IsCompleted's setter stamps CompletedAt with the current time — override after.
        var completed = new TodoItem
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Title = "Ship v1",
            IsCompleted = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero)
        };
        completed.CompletedAt = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero);

        // Fields are retained rather than blanked, so undo restores the task exactly.
        var deleted = new TodoItem
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Title = "Cancel the gym membership",
            IsDeleted = true,
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTimeOffset(2026, 1, 12, 9, 30, 0, TimeSpan.Zero)
        };

        var list = new TaskList
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Name = "Home",
            AccentColor = "#0078D4",
            IsPinned = true,
            SortOrder = 0,
            CustomIcon = "🌿",
            UpdatedAt = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero)
        };

        return new TasksFile { Tasks = [full, completed, deleted], Lists = [list] };
    }

    [TestMethod]
    public void Serialize_CanonicalObjects_MatchesGoldenFixture()
    {
        var produced = JsonNode.Parse(SyncWire.Serialize(CanonicalData()));
        var golden = JsonNode.Parse(GoldenJson);

        Assert.IsTrue(JsonNode.DeepEquals(produced, golden),
            $"Wire payload drifted from the golden fixture.\nProduced: {produced}");
    }

    [TestMethod]
    public void Deserialize_GoldenFixture_RestoresEveryField()
    {
        var data = SyncWire.Deserialize(GoldenJson)!;

        Assert.AreEqual(3, data.Tasks.Count);
        Assert.AreEqual(1, data.Lists.Count);

        var full = data.Tasks[0];
        Assert.AreEqual(Guid.Parse("11111111-1111-1111-1111-111111111111"), full.Id);
        Assert.AreEqual("Water the plants", full.Title);
        Assert.IsFalse(full.IsCompleted);
        Assert.IsNull(full.CompletedAt);
        Assert.IsTrue(full.IsStarred);
        Assert.IsTrue(full.IsInMyDay);
        Assert.AreEqual(new DateOnly(2026, 1, 15), full.MyDayDate);
        Assert.AreEqual(new DateTimeOffset(2026, 1, 16, 9, 0, 0, TimeSpan.Zero), full.DueDate);
        Assert.AreEqual(TaskRecurrence.Weekly, full.Recurrence);
        Assert.AreEqual(TaskPriority.High, full.Priority);
        CollectionAssert.AreEqual(new[] { "home", "green" }, full.Tags);
        Assert.AreEqual(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), full.CreatedAt);
        Assert.AreEqual(new DateTimeOffset(2026, 1, 15, 10, 20, 30, TimeSpan.Zero), full.UpdatedAt);
        Assert.AreEqual("Kitchen and balcony", full.Notes);

        var completed = data.Tasks[1];
        Assert.IsTrue(completed.IsCompleted);
        Assert.AreEqual(new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero), completed.CompletedAt);
        Assert.AreEqual(Guid.Empty, completed.ListId);
        Assert.AreEqual(TaskRecurrence.None, completed.Recurrence);
        Assert.AreEqual(0, completed.Tags.Count);
        Assert.IsNull(completed.Notes);

        var deleted = data.Tasks[2];
        Assert.IsTrue(deleted.IsDeleted);
        Assert.AreEqual("Cancel the gym membership", deleted.Title);
        Assert.AreEqual(new DateTimeOffset(2026, 1, 12, 9, 30, 0, TimeSpan.Zero), deleted.UpdatedAt);

        Assert.IsFalse(full.IsDeleted);
        Assert.IsFalse(completed.IsDeleted);

        var list = data.Lists[0];
        Assert.AreEqual("Home", list.Name);
        Assert.AreEqual("#0078D4", list.AccentColor);
        Assert.IsTrue(list.IsPinned);
        Assert.AreEqual("🌿", list.CustomIcon);
        Assert.IsFalse(list.IsDeleted);
    }

    [TestMethod]
    public void Deserialize_V1PayloadWithoutIsDeleted_TreatsEveryRecordAsLive()
    {
        var json = """
            {"Tasks":[{"Id":"55555555-5555-5555-5555-555555555555","Title":"from v1",
            "IsCompleted":false,"Tags":[],"ListId":"00000000-0000-0000-0000-000000000000",
            "CreatedAt":"2026-01-01T00:00:00Z","UpdatedAt":"2026-01-01T00:00:00+00:00"}],
            "Lists":[{"Id":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","Name":"Old",
            "AccentColor":"#0078D4","UpdatedAt":"2026-01-01T00:00:00+00:00"}]}
            """;

        var data = SyncWire.Deserialize(json)!;

        Assert.IsFalse(data.Tasks[0].IsDeleted);
        Assert.IsFalse(data.Lists[0].IsDeleted);
    }

    [TestMethod]
    public void Deserialize_IgnoresLegacyComputedAndUnknownFields()
    {
        // Older writers emitted derived properties; future writers may add fields.
        var json = """
            {"Tasks":[{"Id":"33333333-3333-3333-3333-333333333333","Title":"legacy",
            "IsCompleted":false,"HasRecurrence":true,"ShowAddDateHint":false,
            "SomeFutureField":{"nested":1},"Tags":[]}],"Lists":[]}
            """;

        var data = SyncWire.Deserialize(json)!;

        Assert.AreEqual("legacy", data.Tasks[0].Title);
        Assert.AreEqual(TaskRecurrence.None, data.Tasks[0].Recurrence);
    }

    [TestMethod]
    public void Serialize_ExcludesComputedAndLocalOnlyFields()
    {
        var json = SyncWire.Serialize(CanonicalData());

        Assert.IsFalse(json.Contains("HasRecurrence"));
        Assert.IsFalse(json.Contains("ShowAddDateHint"));
        Assert.IsFalse(json.Contains("HasMetaSeparator"));
        Assert.IsFalse(json.Contains("ListName"));
    }

    [TestMethod]
    public void IsEquivalent_SameContentDifferentOrder_ReturnsTrue()
    {
        var data = CanonicalData();
        var reordered = new TasksFile
        {
            Tasks = [.. data.Tasks.AsEnumerable().Reverse()],
            Lists = data.Lists
        };

        Assert.IsTrue(SyncWire.IsEquivalent(data, reordered));
    }

    [TestMethod]
    public void IsEquivalent_DifferentTaskField_ReturnsFalse()
    {
        var data = CanonicalData();
        var changed = CanonicalData();
        changed.Tasks[0].Title = "Different title";

        Assert.IsFalse(SyncWire.IsEquivalent(data, changed));
    }

    [TestMethod]
    public void IsEquivalent_ExtraTaskOnOneSide_ReturnsFalse()
    {
        var data = CanonicalData();
        var withExtra = CanonicalData();
        withExtra.Tasks.Add(new TodoItem { Id = Guid.NewGuid(), Title = "New" });

        Assert.IsFalse(SyncWire.IsEquivalent(data, withExtra));
    }
}
