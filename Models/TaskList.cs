namespace Hatch.Models;

public sealed class TaskList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#0078D4"; // Default blue
    public bool IsPinned { get; set; } = true;
    public int SortOrder { get; set; } = 0;
}
