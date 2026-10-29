namespace Hatch.Models;

public sealed record CalendarSource
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? DeltaLink { get; init; }
}
