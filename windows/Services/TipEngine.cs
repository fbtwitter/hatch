using Hatch.Models;

namespace Hatch.Services;

public sealed class TipEngine
{
    private static readonly string[] MorningGreetingKeys =
    [
        "Tip_Greeting_Morning_0",
        "Tip_Greeting_Morning_1",
        "Tip_Greeting_Morning_2"
    ];

    private static readonly string[] AfternoonGreetingKeys =
    [
        "Tip_Greeting_Afternoon_0",
        "Tip_Greeting_Afternoon_1",
        "Tip_Greeting_Afternoon_2",
        "Tip_Greeting_Afternoon_3"
    ];

    private static readonly string[] EveningGreetingKeys =
    [
        "Tip_Greeting_Evening_0",
        "Tip_Greeting_Evening_1",
        "Tip_Greeting_Evening_2",
        "Tip_Greeting_Evening_3"
    ];

    private static readonly string[] AnytimeGreetingKeys =
    [
        "Tip_Greeting_Anytime_0",
        "Tip_Greeting_Anytime_1",
        "Tip_Greeting_Anytime_2",
        "Tip_Greeting_Anytime_3"
    ];

    private static int _greetingIndex = 0;

    private const int InactivityThresholdMinutes = 5;
    private const int MeaningfulTipThresholdHours = 4;
    private const int CompletedTodayCelebrationThreshold = 5;
    private const int StaleTaskThresholdDays = 14;
    private const int EveningHour = 18;

    private readonly Func<string, string> _resolve;

    // This file is compiled into the WinUI-free test project, so it must not touch the
    // resource pipeline itself — TipCoordinator injects Strings.Get; the identity default
    // means Message carries raw resource keys, which is exactly what the tests assert on.
    public TipEngine(Func<string, string>? resolve = null)
    {
        _resolve = resolve ?? (key => key);
    }

    public Tip? GetTip(IReadOnlyList<TodoItem> tasks, DateTime? lastMeaningfulTip = null,
                       DateTime? lastActivity = null, DateTime? now = null)
    {
        var current = now ?? DateTime.Now;
        var today = current.Date;

        // Due dates are calendar days read as written (stored midnight +00:00, or local
        // midnight from a preset) — a time-zone conversion shifts the day west of UTC.
        var overdueTasks = tasks.Count(t =>
            !t.IsCompleted && t.DueDate.HasValue &&
            t.DueDate.Value.Date < today);

        if (overdueTasks >= 1)
            return new Tip
            {
                Message = overdueTasks == 1
                    ? _resolve("Tip_Overdue_One")
                    : string.Format(_resolve("Tip_Overdue_Many"), overdueTasks),
                Severity = TipSeverity.Critical,
                Action = new TipAction { Label = _resolve("Tip_Action_ViewOverdue"), Type = TipActionType.ViewOverdue },
                DismissAfterMs = 0,
                IsMeaningful = true
            };

        var dueToday = tasks.Count(t =>
            !t.IsCompleted && t.DueDate.HasValue &&
            t.DueDate.Value.Date == today);

        if (dueToday >= 1)
            return new Tip
            {
                Message = dueToday == 1
                    ? _resolve("Tip_DueToday_One")
                    : string.Format(_resolve("Tip_DueToday_Many"), dueToday),
                Severity = TipSeverity.Warning,
                Action = new TipAction { Label = _resolve("Tip_Action_ViewPlanned"), Type = TipActionType.ViewPlanned },
                DismissAfterMs = 0,
                IsMeaningful = true
            };

        var openMyDay = tasks.Count(t => t.IsInMyDay && !t.IsCompleted);
        var hasOpenTasks = tasks.Any(t => !t.IsCompleted);
        var hasUsedMyDay = tasks.Any(t => t.MyDayDate.HasValue || t.IsInMyDay);

        if (openMyDay == 0 && current.Hour < EveningHour && hasUsedMyDay && hasOpenTasks)
            return new Tip
            {
                Message = _resolve("Tip_MyDayEmpty"),
                Severity = TipSeverity.Warning,
                Action = new TipAction { Label = _resolve("Tip_Action_PlanMyDay"), Type = TipActionType.ViewMyDay },
                DismissAfterMs = 0,
                IsMeaningful = true
            };

        var completedToday = tasks.Count(t =>
            t.IsCompleted && t.CompletedAt.HasValue &&
            t.CompletedAt.Value.ToLocalTime().Date == today);

        if (current.Hour >= EveningHour && openMyDay == 0 && completedToday >= 1)
            return new Tip
            {
                Message = _resolve("Tip_EveningWrapUp"),
                Severity = TipSeverity.Info,
                Action = new TipAction { Label = _resolve("Tip_Action_PlanTomorrow"), Type = TipActionType.ViewMyDay },
                DismissAfterMs = 0,
                IsMeaningful = true
            };

        if (completedToday >= CompletedTodayCelebrationThreshold)
            return new Tip
            {
                Message = string.Format(_resolve("Tip_CompletedToday"), completedToday),
                Severity = TipSeverity.Info,
                Action = null,
                DismissAfterMs = 3000,
                IsMeaningful = true
            };

        var staleTask = tasks
            .Where(t => !t.IsCompleted && t.DueDate == null &&
                        (today - t.CreatedAt.Date).TotalDays >= StaleTaskThresholdDays)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefault();

        if (staleTask != null)
            return new Tip
            {
                Message = string.Format(_resolve("Tip_StaleTask"),
                    staleTask.Title, (today - staleTask.CreatedAt.Date).Days),
                Severity = TipSeverity.Info,
                Action = new TipAction { Label = _resolve("Tip_Action_TakeALook"), Type = TipActionType.OpenMainWindow },
                DismissAfterMs = 0,
                IsMeaningful = true
            };

        if (hasOpenTasks)
        {
            var fallbackTip = new Tip
            {
                Message = _resolve(GetTimeBasedGreetingKey(current)),
                Severity = TipSeverity.Warning,
                Action = null,
                DismissAfterMs = 5000,
                IsMeaningful = false
            };
            return ShouldSuppressFallback(current, lastMeaningfulTip, lastActivity) ? null : fallbackTip;
        }

        if (tasks.Count == 0)
        {
            var fallbackTip = new Tip
            {
                Message = _resolve("Tip_EmptyList"),
                Severity = TipSeverity.Warning,
                Action = new TipAction { Label = _resolve("Tip_Action_AddSample"), Type = TipActionType.AddSampleTask },
                DismissAfterMs = 0,
                IsMeaningful = false
            };
            return ShouldSuppressFallback(current, lastMeaningfulTip, lastActivity) ? null : fallbackTip;
        }

        var anytimeTip = new Tip
        {
            Message = _resolve(AnytimeGreetingKeys[_greetingIndex++ % AnytimeGreetingKeys.Length]),
            Severity = TipSeverity.Info,
            Action = null,
            DismissAfterMs = 3000,
            IsMeaningful = false
        };
        return ShouldSuppressFallback(current, lastMeaningfulTip, lastActivity) ? null : anytimeTip;
    }

    private static bool ShouldSuppressFallback(DateTime now, DateTime? lastMeaningfulTip, DateTime? lastActivity)
    {
        var inactiveDuration = now - (lastActivity ?? now);
        var timeSinceMeaningful = now - (lastMeaningfulTip ?? DateTime.MinValue);

        // Silence is better than filler: suppress when the user is active and has
        // recently seen a meaningful tip.
        return inactiveDuration.TotalMinutes < InactivityThresholdMinutes &&
               timeSinceMeaningful.TotalHours < MeaningfulTipThresholdHours;
    }

    private static string GetTimeBasedGreetingKey(DateTime now)
    {
        var index = _greetingIndex++;

        return now.Hour < 12
            ? MorningGreetingKeys[index % MorningGreetingKeys.Length]
            : now.Hour < EveningHour
                ? AfternoonGreetingKeys[index % AfternoonGreetingKeys.Length]
                : EveningGreetingKeys[index % EveningGreetingKeys.Length];
    }
}
