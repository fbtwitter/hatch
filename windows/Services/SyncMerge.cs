using Hatch.Models;

namespace Hatch.Services;

// Record-level (not field-level) last-write-wins union of two TasksFiles by Id.
// A task/list edited on only one side is kept as-is; a task/list present on both
// sides keeps whichever copy has the later UpdatedAt. Nothing is ever dropped —
// this is the non-destructive alternative to "pick a side, lose the other".
public static class SyncMerge
{
    public static TasksFile Merge(TasksFile local, TasksFile server) => new()
    {
        Tasks = MergeById(local.Tasks, server.Tasks, t => t.Id, t => t.UpdatedAt),
        Lists = MergeById(local.Lists, server.Lists, l => l.Id, l => l.UpdatedAt)
    };

    private static List<T> MergeById<T>(
        List<T> local, List<T> server, Func<T, Guid> idOf, Func<T, DateTimeOffset> updatedAtOf)
    {
        var merged = new Dictionary<Guid, T>();
        foreach (var item in server)
            merged[idOf(item)] = item;

        foreach (var item in local)
        {
            var id = idOf(item);
            if (!merged.TryGetValue(id, out var existing) || updatedAtOf(item) >= updatedAtOf(existing))
                merged[id] = item;
        }

        return [.. merged.Values];
    }
}
