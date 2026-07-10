using Hatch.Models;

namespace Hatch.Helpers;

public static class TaskSearchMatcher
{
    public static bool Matches(TodoItem task, string query) =>
        task.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (task.Notes != null && task.Notes.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
        task.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
}
