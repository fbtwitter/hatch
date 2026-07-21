using Hatch.Models;

namespace Hatch.Services;

// Single owner of the tip bookkeeping that the two tip surfaces (quick-add bubble's
// contextual tip, mascot's proactive TeachingTip) previously duplicated: adaptive-silence
// cooldown, activity stamping, meaningful-tip timestamp, daily-indicator date, and the
// 3-strike dismissal counter. TipEngine itself stays a pure function.
public sealed class TipCoordinator
{
    private readonly TipEngine _engine = new(Helpers.Strings.Get);
    private readonly SettingsService _settings;

    public TipCoordinator(SettingsService settings)
    {
        _settings = settings;
    }

    private AppSettings S => _settings.Current;

    public Tip? TryGetContextualTip(IReadOnlyList<TodoItem> tasks, out bool isNewDailyTip)
    {
        isNewDailyTip = false;
        var today = DateTime.Today;

        if (S.TipAutoOpenCooldownUntil.HasValue && today < S.TipAutoOpenCooldownUntil.Value)
            return null;

        S.LastUserActivityTime = DateTime.Now;
        var tip = _engine.GetTip(tasks, S.LastMeaningfulTipTime, S.LastUserActivityTime);
        if (tip == null)
        {
            _ = _settings.SaveAsync();
            return null;
        }

        if (tip.IsMeaningful)
            S.LastMeaningfulTipTime = DateTime.Now;

        if (S.LastTipShowDate?.Date != today)
        {
            S.LastTipShowDate = today;
            isNewDailyTip = true;
        }

        _ = _settings.SaveAsync();
        return tip;
    }

    public Tip? TryGetProactiveTip(IReadOnlyList<TodoItem> tasks, out bool isNewDailyTip)
    {
        isNewDailyTip = false;

        if (!S.ShowTipsAutomatically) return null;
        if (S.LastProactiveTipCheckDate?.Date == DateTime.Today) return null;

        // Outside the preferred window the check date is NOT stamped, so the tip can
        // still fire later the same day once the window opens.
        if (!Helpers.TipSchedule.IsInPreferredWindow(DateTime.Now, S.ProactiveTipTime)) return null;

        S.LastProactiveTipCheckDate = DateTime.Today;
        _ = _settings.SaveAsync();

        return TryGetContextualTip(tasks, out isNewDailyTip);
    }

    public void RecordEngagement()
    {
        if (S.ConsecutiveTipDismissals > 0)
        {
            S.ConsecutiveTipDismissals = 0;
            _ = _settings.SaveAsync();
        }
    }

    public void RecordDismissal()
    {
        S.ConsecutiveTipDismissals++;
        if (S.ConsecutiveTipDismissals >= 3)
        {
            S.TipAutoOpenCooldownUntil = DateTime.Today.AddDays(3);
            S.ConsecutiveTipDismissals = 0;
        }
        _ = _settings.SaveAsync();
    }
}
