using System.Text.Json;
using Hatch.Models;

namespace Hatch.Services;

// The JSON half of the sync wire contract (docs/sync-protocol.md is the spec; the
// Hatch.Tests.Unit/Fixtures golden files are the executable contract every client —
// this app and the future Kotlin core — must pass). Settings are pinned here so a
// System.Text.Json default change can never silently alter the wire.
// Compiled into the WinUI-free test project — BCL only.
public static class SyncWire
{
    // Pinned: PascalCase property names, enums as integers, ISO-8601 dates, nulls
    // written, computed get-only properties excluded. Rows written by versions that
    // included computed properties still parse — readers ignore unknown fields.
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        IgnoreReadOnlyProperties = true
    };

    public static string Serialize(TasksFile data)
        => JsonSerializer.Serialize(data, JsonOptions);

    public static TasksFile? Deserialize(string json)
        => JsonSerializer.Deserialize<TasksFile>(json, JsonOptions);

    // Order-insensitive: SyncMerge.Merge rebuilds its result from a Dictionary, so it
    // doesn't preserve list order even when no task or list actually changed. A plain
    // Serialize(a) == Serialize(b) would report "changed" on every merge.
    public static bool IsEquivalent(TasksFile a, TasksFile b)
    {
        string Canonical(TasksFile f) => Serialize(new TasksFile
        {
            Tasks = [.. f.Tasks.OrderBy(t => t.Id)],
            Lists = [.. f.Lists.OrderBy(l => l.Id)]
        });
        return Canonical(a) == Canonical(b);
    }
}
