using Hatch.Models;

namespace Hatch.Helpers;

public static class MascotOpenPageHelper
{
    public const string SummaryFallbackTag = "summary";

    public static string? Resolve(string? storedTag, IEnumerable<TaskList> customLists)
    {
        if (string.IsNullOrEmpty(storedTag)) return storedTag;

        if (Guid.TryParse(storedTag, out var listId) && !customLists.Any(l => l.Id == listId))
            return SummaryFallbackTag;

        return storedTag;
    }
}
