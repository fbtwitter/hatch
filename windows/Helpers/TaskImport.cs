using System.Text.Json;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.Helpers;

// Pure import logic — parse a Hatch JSON export and merge it into local state with the same
// record-level last-write-wins as sync. No file I/O (that's SettingsViewModel.ImportAsync).
// BCL + Services only, so it links into Hatch.Tests.Unit.
public static class TaskImport
{
    public static TasksFile? Parse(string json)
    {
        try
        {
            var file = SyncWire.Deserialize(json);
            // A bare JSON scalar ("null", "5") deserializes without throwing but isn't a file.
            return file is { Tasks: not null, Lists: not null } ? file : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // imported is passed as SyncMerge's "server" side so the local copy wins ties — an
    // imported row lands only when strictly newer (or new). Applied counts the rows the file
    // actually contributed: SyncMerge keeps the winning object by reference.
    public static (TasksFile Merged, int Applied) Merge(TasksFile local, TasksFile imported)
    {
        var merged = SyncMerge.Merge(local, imported);
        var importedById = imported.Tasks.ToDictionary(t => t.Id);
        var applied = merged.Tasks.Count(t => importedById.TryGetValue(t.Id, out var it) && ReferenceEquals(it, t));
        return (merged, applied);
    }
}
