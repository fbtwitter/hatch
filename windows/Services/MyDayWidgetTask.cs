namespace Hatch.Services;

internal sealed record MyDayWidgetTask(
    Guid Id,
    string Title,
    string? Metadata,
    string PriorityColor,
    bool IsStarred);
