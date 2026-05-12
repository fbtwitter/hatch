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

    // Thresholds for smart fallback suppression
    private const int InactivityThresholdMinutes = 5;
    private const int MeaningfulTipThresholdHours = 4;

    public Tip? GetTip(IReadOnlyList<TodoItem> tasks, DateTime? lastMeaningfulTip = null, DateTime? lastActivity = null)
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
                Message = $"You have {overdueTasks} overdue task{(overdueTasks > 1 ? "s" : "")}",
                Severity = TipSeverity.Critical,
                Action = new TipAction { Label = "View overdue", Type = TipActionType.ViewOverdue },
                DismissAfterMs = 0,  // 0 = indefinite (user dismisses)
                IsMeaningful = true
            };

        // 2. Check My Day empty (no time condition — prompt planning any time)
        var myDayTasks = tasks.Count(t => t.IsInMyDay && !t.IsCompleted);
        if (myDayTasks == 0)
            return new Tip
            {
                Message = "Your My Day list is empty—ready to plan?",
                Severity = TipSeverity.Critical,
                Action = new TipAction { Label = "Plan My Day", Type = TipActionType.ViewMyDay },
                DismissAfterMs = 0,  // indefinite
                IsMeaningful = true
            };

        // 3. Check ≥ 5 tasks completed (no time tracking, so this checks total completed)
        var completedCount = tasks.Count(t => t.IsCompleted);

        if (completedCount >= 5)
            return new Tip
            {
                Message = $"Great progress! {completedCount} tasks completed.",
                Severity = TipSeverity.Info,
                Action = null,
                DismissAfterMs = 3000,  // 3s for celebratory tip
                IsMeaningful = true
            };

        // 4. Check first open of day (any uncompleted task exists) — fallback tip
        var hasOpenTasks = tasks.Any(t => !t.IsCompleted);
        if (hasOpenTasks)
        {
            var fallbackTip = new Tip
            {
                Message = GetTimeBasedGreeting(),
                Severity = TipSeverity.Warning,
                Action = null,
                DismissAfterMs = 5000,  // 5s for greeting
                IsMeaningful = false
            };
            return ShouldSuppressFallback(lastMeaningfulTip, lastActivity) ? null : fallbackTip;
        }

        // 5. No tasks exist — fallback tip
        if (tasks.Count == 0)
        {
            var fallbackTip = new Tip
            {
                Message = "Your task list is empty. Add one to get started.",
                Severity = TipSeverity.Warning,
                Action = new TipAction { Label = "Add sample task", Type = TipActionType.AddSampleTask },
                DismissAfterMs = 0,  // indefinite with action
                IsMeaningful = false
            };
            return ShouldSuppressFallback(lastMeaningfulTip, lastActivity) ? null : fallbackTip;
        }

        // 6. Rotating anytime greetings — fallback tip
        var tipText = AnytimeGreetings[_greetingIndex % AnytimeGreetings.Length];
        _greetingIndex++;
        var anytimeTip = new Tip
        {
            Message = tipText,
            Severity = TipSeverity.Info,
            Action = null,
            DismissAfterMs = 3000,  // 3s for fallback greeting
            IsMeaningful = false
        };
        return ShouldSuppressFallback(lastMeaningfulTip, lastActivity) ? null : anytimeTip;
    }

    private bool ShouldSuppressFallback(DateTime? lastMeaningfulTip, DateTime? lastActivity)
    {
        // Show fallback only if user inactive for X minutes OR no meaningful tip for Y hours
        var now = DateTime.Now;
        var inactiveDuration = now - (lastActivity ?? now);
        var timeSinceMeaningful = now - (lastMeaningfulTip ?? DateTime.MinValue);

        // If user is active and we've shown a meaningful tip recently, suppress fallback
        if (inactiveDuration.TotalMinutes < InactivityThresholdMinutes &&
            timeSinceMeaningful.TotalHours < MeaningfulTipThresholdHours)
        {
            return true;  // Suppress (silence is better than filler)
        }

        return false;  // Show fallback
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
