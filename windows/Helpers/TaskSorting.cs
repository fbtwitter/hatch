using Hatch.Models;

namespace Hatch.Helpers;

public static class TaskSorting
{
    // Important smart list: highest priority first, then newest-first within a tier.
    public static IEnumerable<TodoItem> ForImportant(IEnumerable<TodoItem> tasks) =>
        tasks.OrderByDescending(t => t.Priority).ThenByDescending(t => t.CreatedAt);
}
