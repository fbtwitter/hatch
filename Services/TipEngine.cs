using Hatch.Models;

namespace Hatch.Services;

public sealed class TipEngine
{
    private static readonly string[] FallbackTips =
    [
        "You're all caught up",
        "One step at a time",
        "Stay focused",
        "Great progress so far",
        "You've got this",
        "Every task counts"
    ];

    private static int _fallbackIndex = 0;

    public string GetTip(IReadOnlyList<TodoItem> tasks)
    {
        var now = DateTime.Now;
        var today = now.Date;

        // 1. Check for overdue tasks (≥ 1)
        var overdueTasks = tasks.Count(t =>
            !t.IsCompleted && t.DueDate.HasValue &&
            t.DueDate.Value.ToLocalTime().Date < today);

        if (overdueTasks >= 1)
            return $"You have {overdueTasks} overdue task{(overdueTasks > 1 ? "s" : "")}";

        // 2. Check My Day empty before 11am
        var myDayTasks = tasks.Count(t => t.IsInMyDay && !t.IsCompleted);
        if (myDayTasks == 0 && now.Hour < 11)
            return "Your My Day list is empty—ready to plan?";

        // 3. Check ≥ 5 tasks completed (no time tracking, so this checks total completed)
        var completedCount = tasks.Count(t => t.IsCompleted);

        if (completedCount >= 5)
            return $"Great progress! {completedCount} tasks completed.";

        // 4. Check first open of day (any uncompleted task exists + first session)
        var hasOpenTasks = tasks.Any(t => !t.IsCompleted);
        if (hasOpenTasks)
            return "Ready to tackle your day?";

        // 5. No tasks exist
        if (tasks.Count == 0)
            return "Your task list is empty. Add one to get started.";

        // 6. Rotating fallback
        var tip = FallbackTips[_fallbackIndex % FallbackTips.Length];
        _fallbackIndex++;
        return tip;
    }
}
