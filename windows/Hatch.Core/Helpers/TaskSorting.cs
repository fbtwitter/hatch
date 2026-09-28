using Hatch.Models;

namespace Hatch.Helpers;

public static class TaskSorting
{
    // CreatedAt keeps whatever kind its writer stamped — this app local, the companion
    // UTC — and DateTime compares raw ticks, so the two sources interleave hours apart.
    public static DateTime CreatedInstant(TodoItem task) => task.CreatedAt.ToUniversalTime();

    public static IEnumerable<TodoItem> NewestFirst(IEnumerable<TodoItem> tasks) =>
        tasks.OrderByDescending(CreatedInstant);

    // Important smart list: highest priority first, then newest-first within a tier.
    public static IEnumerable<TodoItem> ForImportant(IEnumerable<TodoItem> tasks) =>
        tasks.OrderByDescending(t => t.Priority).ThenByDescending(CreatedInstant);
}
