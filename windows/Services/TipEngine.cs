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

    // Original lines in the mascot's own voice. Deliberately unattributed — see
    // context/current-feature.md: this app does not put words in a real person's mouth.
    // Users add their own (attributed or not) via Settings; those are merged in.
    private static readonly string[] InspirationKeys =
    [
        "Tip_Inspiration_0", "Tip_Inspiration_1", "Tip_Inspiration_2",
        "Tip_Inspiration_3", "Tip_Inspiration_4", "Tip_Inspiration_5",
        "Tip_Inspiration_6", "Tip_Inspiration_7", "Tip_Inspiration_8",
        "Tip_Inspiration_9", "Tip_Inspiration_10", "Tip_Inspiration_11"
    ];

    private static readonly string[] CaptureInviteKeys =
    [
        "Tip_Capture_0", "Tip_Capture_1", "Tip_Capture_2", "Tip_Capture_3"
    ];

    private static int _greetingIndex = 0;

    private const int InactivityThresholdMinutes = 5;
    private const int MeaningfulTipThresholdHours = 4;
    private const int CompletedTodayCelebrationThreshold = 5;
    private const int StaleTaskThresholdDays = 14;
    private const int EveningHour = 18;
    private const int UndatedBacklogThreshold = 5;

    private readonly Func<string, string> _resolve;

    // This file is compiled into the WinUI-free test project, so it must not touch the
    // resource pipeline itself — TipCoordinator injects Strings.Get; the identity default
    // means Message carries raw resource keys, which is exactly what the tests assert on.
    public TipEngine(Func<string, string>? resolve = null)
    {
        _resolve = resolve ?? (key => key);
    }

    // chattiness/customTips/lastInspiration are optional so the existing call sites and
    // the pre-existing test suite keep compiling unchanged; TipCoordinator supplies them.
    public Tip? GetTip(IReadOnlyList<TodoItem> tasks, DateTime? lastMeaningfulTip = null,
                       DateTime? lastActivity = null, DateTime? now = null,
                       MascotChattiness chattiness = MascotChattiness.Balanced,
                       IReadOnlyList<string>? customTips = null,
                       DateTime? lastInspiration = null)
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

        // Undated backlog — a real, actionable observation about the list, so it ranks
        // above the fallback tier and counts as meaningful. Only fires once the pile is
        // big enough to be worth mentioning; a couple of undated tasks is normal.
        var undated = tasks.Count(t => !t.IsCompleted && t.DueDate == null);
        if (undated >= UndatedBacklogThreshold)
            return new Tip
            {
                Message = string.Format(_resolve("Tip_UndatedBacklog"), undated),
                Severity = TipSeverity.Info,
                Action = new TipAction { Label = _resolve("Tip_Action_ScheduleOne"), Type = TipActionType.OpenMainWindow },
                DismissAfterMs = 0,
                IsMeaningful = true
            };

        // ── Fallback tier: nothing actionable is pending ────────────────────────────
        // Quiet means exactly that — no greetings, no inspiration, no invitations.
        if (chattiness == MascotChattiness.Quiet) return null;

        // Onboarding outranks inspiration: someone with no tasks at all needs the prompt
        // that gets them started, not a quote. Deliberately above the daily slot.
        if (tasks.Count == 0)
        {
            var emptyTip = new Tip
            {
                Message = _resolve("Tip_EmptyList"),
                Severity = TipSeverity.Warning,
                Action = new TipAction { Label = _resolve("Tip_Action_AddSample"), Type = TipActionType.AddSampleTask },
                DismissAfterMs = 0,
                IsMeaningful = false
            };
            return chattiness != MascotChattiness.Chatty &&
                   ShouldSuppressFallback(current, lastMeaningfulTip, lastActivity)
                ? null : emptyTip;
        }

        // The one guaranteed slot: first showing of the day gets an inspiration line even
        // when the silence rule would otherwise suppress it. Date-seeded rather than
        // random so the same line holds all day instead of re-rolling per bubble open.
        bool inspirationDueToday = chattiness != MascotChattiness.Quiet &&
                                   lastInspiration?.Date != today;
        if (inspirationDueToday)
        {
            var pool = BuildInspirationPool(customTips);
            if (pool.Count > 0)
            {
                var dayNumber = (int)(current.Date.Ticks / TimeSpan.TicksPerDay);
                return new Tip
                {
                    Message = pool[dayNumber % pool.Count],
                    Severity = TipSeverity.Info,
                    Action = null,
                    DismissAfterMs = 6000,
                    IsMeaningful = false,
                    IsInspiration = true
                };
            }
        }

        bool suppress = chattiness != MascotChattiness.Chatty &&
                        ShouldSuppressFallback(current, lastMeaningfulTip, lastActivity);

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
            return suppress ? null : fallbackTip;
        }

        // Everything is done. Rather than another bare greeting, invite the next thought
        // in — this is the moment the user has capacity to add something.
        var captureTip = new Tip
        {
            Message = _resolve(CaptureInviteKeys[_greetingIndex++ % CaptureInviteKeys.Length]),
            Severity = TipSeverity.Info,
            Action = new TipAction { Label = _resolve("Tip_Action_WriteItDown"), Type = TipActionType.CaptureTask },
            DismissAfterMs = 5000,
            IsMeaningful = false
        };
        return suppress ? null : captureTip;
    }

    // Built-in lines and the user's own, merged. A user with many custom lines therefore
    // sees them more often than the built-ins — intentional, so disliked built-ins can be
    // drowned out without needing a replace-vs-append toggle.
    private List<string> BuildInspirationPool(IReadOnlyList<string>? customTips)
    {
        var pool = new List<string>(InspirationKeys.Length + (customTips?.Count ?? 0));
        foreach (var key in InspirationKeys)
            pool.Add(_resolve(key));
        if (customTips != null)
            foreach (var line in customTips)
                if (!string.IsNullOrWhiteSpace(line))
                    pool.Add(line.Trim());
        return pool;
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
