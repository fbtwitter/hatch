using Hatch.Models;

namespace Hatch.Helpers;

// Resolves AppSettings.MascotOpenPageTag against the custom lists that currently exist. A
// pinned custom list can be deleted after being chosen; GUIDs are never reused, so a tag
// pointing at a deleted list will never become valid again — falls back to the Summary page
// rather than navigating to (or displaying as selected) a list that's gone.
// WinUI-free: linked into Hatch.Tests.Unit.
public static class MascotOpenPageHelper
{
    public const string SummaryFallbackTag = "summary";

    public static string? Resolve(string? storedTag, IEnumerable<TaskList> customLists)
    {
        if (string.IsNullOrEmpty(storedTag)) return storedTag; // "remember last page"

        if (Guid.TryParse(storedTag, out var listId) && !customLists.Any(l => l.Id == listId))
            return SummaryFallbackTag;

        return storedTag;
    }
}
