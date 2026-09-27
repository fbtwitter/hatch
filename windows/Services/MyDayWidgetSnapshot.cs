namespace Hatch.Services;

internal sealed record MyDayWidgetSnapshot(
    IReadOnlyList<MyDayWidgetTask> OpenTasks,
    int Done,
    int Total,
    DateOnly Date);
