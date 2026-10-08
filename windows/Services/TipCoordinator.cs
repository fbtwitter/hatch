using Hatch.Models;

namespace Hatch.Services;

// Owns topic cooldowns, pending tips, and daily display slots for both mascot surfaces.
public sealed class TipCoordinator
{
    private readonly TipEngine _engine = new(Helpers.Strings.Get);
    private readonly SettingsService _settings;
    public event Action<bool>? QuickTipsAvailabilityChanged;
    public event Action? PendingTipChanged;
    public event Action? TopicsChanged;
    private Tip? _pendingTip;
    private DateTime _pendingTipDate;
    private string? _resumedTopic;

    public TipCoordinator(SettingsService settings)
    {
        _settings = settings;
    }

    private AppSettings S => _settings.Current;
    private bool QuickTipsActiveToday => S.ShowQuickTips &&
        (!S.QuickTipsPausedUntil.HasValue || DateTime.Today >= S.QuickTipsPausedUntil.Value.Date);
    public bool CanSpeak => QuickTipsActiveToday;

    private Dictionary<string, TipTopicState> TopicStates => S.TipTopics ??= [];
    public IReadOnlyDictionary<string, TipTopicState> Topics => TopicStates;
    private bool IsQuiet(string topic, DateTime now) =>
        TopicStates.TryGetValue(topic, out var state) && state.QuietUntil > now;

    public Tip? PeekPendingTip(IReadOnlyList<TodoItem> tasks)
    {
        if (!QuickTipsActiveToday) return null;
        if (_pendingTip == null && S.PendingTipTopic != null && S.PendingTipDate?.Date != DateTime.Today)
        {
            ClearPendingTip();
            return null;
        }
        if (_pendingTip == null && S.PendingTipDate?.Date == DateTime.Today)
        {
            _pendingTip = S.PendingTipTopic switch
            {
                "plan-my-day" or "plan-tomorrow" => _engine.GetPlanningTip(tasks, DateTime.Now),
                "inspiration" => _engine.GetInspirationTip(DateTime.Now, customTips: null),
                "my-day-complete" => new Tip
                {
                    Topic = "my-day-complete", Message = Helpers.Strings.Get("Tip_MyDayComplete"),
                    Category = TipCategory.Encouragement, DismissAfterMs = 9000
                },
                _ => null
            };
            _pendingTipDate = DateTime.Today;
            if (_pendingTip?.Topic != S.PendingTipTopic) ClearPendingTip();
        }
        if (_pendingTip == null) return null;
        if (_pendingTipDate != DateTime.Today)
        {
            ClearPendingTip();
            return null;
        }
        var tip = _pendingTip;
        if (IsQuiet(tip.Topic, DateTime.Now) ||
            (tip.Category == TipCategory.Planning &&
             _engine.GetPlanningTip(tasks, DateTime.Now)?.Topic != tip.Topic) ||
            (tip.IsInspiration && S.LastInspirationDate?.Date != DateTime.Today))
        {
            ClearPendingTip();
            return null;
        }
        return tip;
    }

    public void SetPendingTip(Tip tip)
    {
        _pendingTip = tip;
        _pendingTipDate = DateTime.Today;
        S.PendingTipTopic = tip.Topic;
        S.PendingTipDate = DateTime.Today;
        _settings.SaveDebounced();
        PendingTipChanged?.Invoke();
    }

    public void ClearPendingTip()
    {
        if (_pendingTip == null && S.PendingTipTopic == null) return;
        _pendingTip = null;
        S.PendingTipTopic = null;
        S.PendingTipDate = null;
        _settings.SaveDebounced();
        PendingTipChanged?.Invoke();
    }

    public void PauseForToday()
    {
        S.QuickTipsPausedUntil = DateTime.Today.AddDays(1);
        ClearPendingTip();
        _settings.SaveDebounced();
        QuickTipsAvailabilityChanged?.Invoke(false);
    }

    public void SetQuickTipsEnabled(bool enabled)
    {
        if (S.ShowQuickTips == enabled) return;

        S.ShowQuickTips = enabled;
        if (enabled) S.QuickTipsPausedUntil = null;
        else ClearPendingTip();
        _settings.SaveDebounced();
        QuickTipsAvailabilityChanged?.Invoke(QuickTipsActiveToday);
    }

    public Tip? TryGetContextualTip(IReadOnlyList<TodoItem> tasks)
    {
        var today = DateTime.Today;

        if (!QuickTipsActiveToday) return null;
        S.LastUserActivityTime = DateTime.Now;
        if (_resumedTopic != null)
        {
            var resumedTopic = _resumedTopic;
            var resumed = resumedTopic == "inspiration"
                ? _engine.GetInspirationTip(DateTime.Now, S.CustomTips)
                : _engine.GetPlanningTip(tasks, DateTime.Now);
            _resumedTopic = null;
            if (resumed?.Topic == resumedTopic &&
                !IsQuiet(resumed.Topic, DateTime.Now)) return resumed;
        }
        var pending = PeekPendingTip(tasks);
        if (pending != null) return pending;

        var tip = _engine.GetTip(tasks, S.LastMeaningfulTipTime, S.LastUserActivityTime,
                                 now: null,
                                 chattiness: S.MascotChattiness,
                                 customTips: S.CustomTips,
                                 lastInspiration: S.LastInspirationDate,
                                 quietedTopics: TopicStates.Where(pair => pair.Value.QuietUntil > DateTime.Now)
                                     .Select(pair => pair.Key).ToHashSet());
        if (tip != null && !IsQuiet(tip.Topic, DateTime.Now)) return tip;
        if (S.MascotChattiness != MascotChattiness.Quiet &&
            S.LastInspirationDate?.Date != today && !IsQuiet("inspiration", DateTime.Now))
            return _engine.GetInspirationTip(DateTime.Now, S.CustomTips);
        return null;
    }

    public Tip? TryGetProactiveTip(IReadOnlyList<TodoItem> tasks)
    {
        if (!QuickTipsActiveToday) return null;
        if (!S.ShowTipsAutomatically) return null;
        var now = DateTime.Now;
        if (!Helpers.TipSchedule.IsInPreferredWindow(now, S.ProactiveTipTime)) return null;
        if (tasks.Any(task => !task.IsCompleted && task.DueDate is { } due &&
            IsNearDueNotification(due, now))) return null;
        if (S.LastAutomaticBubbleAt.HasValue && now - S.LastAutomaticBubbleAt.Value < TimeSpan.FromHours(3)) return null;
        if (S.LastPlanningBubbleDate?.Date != now.Date)
        {
            var planning = _engine.GetPlanningTip(tasks, now);
            if (planning != null && !IsQuiet(planning.Topic, now)) return planning;
        }
        if (S.MascotChattiness != MascotChattiness.Quiet &&
            S.LastInspirationDate?.Date != now.Date && !IsQuiet("inspiration", now))
            return _engine.GetInspirationTip(now, customTips: null);
        return null;
    }

    private static bool IsNearDueNotification(DateTimeOffset due, DateTime now)
    {
        var dueLocal = due.TimeOfDay == TimeSpan.Zero
            ? due.Date.AddHours(9)
            : due.ToLocalTime().DateTime;
        return Math.Abs((dueLocal - now).TotalMinutes) <= 10 ||
               Math.Abs((dueLocal.AddMinutes(-30) - now).TotalMinutes) <= 10;
    }

    public void RecordShown(Tip tip, bool automatic)
    {
        if (!automatic && _pendingTip?.Topic == tip.Topic) ClearPendingTip();
        if (TopicStates.TryGetValue(tip.Topic, out var retry) &&
            retry.QuietUntil.HasValue && retry.QuietUntil <= DateTime.Now)
        {
            retry.QuietUntil = DateTime.Now.AddDays(30);
            retry.Dismissals = 0;
            TopicsChanged?.Invoke();
        }
        if (tip.IsInspiration) S.LastInspirationDate = DateTime.Today;
        if (tip.IsMeaningful) S.LastMeaningfulTipTime = DateTime.Now;
        if (automatic)
        {
            S.LastAutomaticBubbleAt = DateTime.Now;
            if (tip.Category == TipCategory.Planning) S.LastPlanningBubbleDate = DateTime.Today;
        }
        _settings.SaveDebounced();
    }

    public void RecordEngagement(Tip? tip)
    {
        if (tip == null) return;
        if (TopicStates.TryGetValue(tip.Topic, out var state))
        {
            if (state.QuietUntil.HasValue) TopicStates.Remove(tip.Topic);
            else state.Dismissals = 0;
            TopicsChanged?.Invoke();
        }
        ClearPendingTip();
        _settings.SaveDebounced();
    }

    public void RecordDismissal(Tip tip)
    {
        var state = TopicStates.GetValueOrDefault(tip.Topic);
        if (state == null) TopicStates[tip.Topic] = state = new TipTopicState();
        if (state.QuietUntil > DateTime.Now)
        {
            ClearPendingTip();
            return;
        }
        state.Dismissals++;
        if (state.Dismissals >= 3)
        {
            state.QuietUntil = DateTime.Now.AddDays(30);
            state.Dismissals = 0;
        }
        ClearPendingTip();
        _settings.SaveDebounced();
        TopicsChanged?.Invoke();
    }

    public void ResumeTopic(string topic)
    {
        if (!TopicStates.Remove(topic)) return;
        _resumedTopic = topic;
        _settings.SaveDebounced();
        PendingTipChanged?.Invoke();
        TopicsChanged?.Invoke();
    }

    public void NotifyPresentationSettingsChanged() => PendingTipChanged?.Invoke();
}
