using Hatch.Models;

namespace Hatch.Services;

public sealed class TipEngine
{
    private static readonly string[] MorningGreetings =
    [
        "Good morning",
        "What's the priority today?",
        "Ready to plan your day?"
    ];

    private static readonly string[] AfternoonGreetings =
    [
        "Small steps count",
        "Let's clear one thing first",
        "How's the day going?",
        "Keep the momentum"
    ];

    private static readonly string[] EveningGreetings =
    [
        "Great work today",
        "Tomorrow's a new slate",
        "You earned this",
        "Wrap up strong"
    ];

    private static readonly string[] AnytimeGreetings =
    [
        "You've got this",
        "One task at a time",
        "You're all caught up",
        "Every task counts"
    ];

    private static int _greetingIndex = 0;

    public Tip GetTip(IReadOnlyList<TodoItem> tasks)
    {
        var now = DateTime.Now;
        var today = now.Date;

        // 1. Check for overdue tasks (≥ 1)
        var overdueTasks = tasks.Count(t =>
            !t.IsCompleted && t.DueDate.HasValue &&
            t.DueDate.Value.ToLocalTime().Date < today);

        if (overdueTasks >= 1)
            return new Tip
            {
                Text = $"You have {overdueTasks} overdue task{(overdueTasks > 1 ? "s" : "")}",
                Action = new TipAction { Label = "View overdue", Type = TipActionType.ViewOverdue }
            };

        // 2. Check My Day empty (no time condition — prompt planning any time)
        var myDayTasks = tasks.Count(t => t.IsInMyDay && !t.IsCompleted);
        if (myDayTasks == 0)
            return new Tip
            {
                Text = "Your My Day list is empty—ready to plan?",
                Action = new TipAction { Label = "Plan My Day", Type = TipActionType.ViewMyDay }
            };

        // 3. Check ≥ 5 tasks completed (no time tracking, so this checks total completed)
        var completedCount = tasks.Count(t => t.IsCompleted);

        if (completedCount >= 5)
            return new Tip
            {
                Text = $"Great progress! {completedCount} tasks completed.",
                Action = null
            };

        // 4. Check first open of day (any uncompleted task exists)
        var hasOpenTasks = tasks.Any(t => !t.IsCompleted);
        if (hasOpenTasks)
            return new Tip
            {
                Text = GetTimeBasedGreeting(),
                Action = null
            };

        // 5. No tasks exist
        if (tasks.Count == 0)
            return new Tip
            {
                Text = "Your task list is empty. Add one to get started.",
                Action = new TipAction { Label = "Add sample task", Type = TipActionType.AddSampleTask }
            };

        // 6. Rotating anytime greetings
        var tipText = AnytimeGreetings[_greetingIndex % AnytimeGreetings.Length];
        _greetingIndex++;
        return new Tip
        {
            Text = tipText,
            Action = null
        };
    }

    private string GetTimeBasedGreeting()
    {
        var hour = DateTime.Now.Hour;
        var index = _greetingIndex++;

        return hour < 12
            ? MorningGreetings[index % MorningGreetings.Length]
            : hour < 18
                ? AfternoonGreetings[index % AfternoonGreetings.Length]
                : EveningGreetings[index % EveningGreetings.Length];
    }
}
