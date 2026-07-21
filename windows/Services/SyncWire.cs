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
}
