namespace Hatch.Models;

public sealed class TaskList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#0078D4";
    public bool IsPinned { get; set; } = false;
    public int SortOrder { get; set; } = 0;
    public string? CustomIcon { get; set; } = null;

    // Tombstone — see TodoItem.IsDeleted.
    public bool IsDeleted { get; set; }

    // Stamped explicitly by MainViewModel on rename/recolor/pin/icon/reorder.
    // Used by SyncMerge to resolve which side wins when the same list changed on both.
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
