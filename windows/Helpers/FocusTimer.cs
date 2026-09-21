namespace Hatch.Helpers;

// A per-task count-up stopwatch — "focus mode". Purely local and ephemeral: a FocusSession is
// never written to TodoItem, never added to the wire model, never synced. Transcribed from
// hatch-mobile shared/commonMain/kotlin/dev/hatch/sync/FocusTimer.kt; kept pure so it links
// into Hatch.Tests.Unit without a WinUI reference, the same reasoning as SummaryStats.
//
// TaskTitle is a display-only snapshot so a restored session has something to show before the
// task list has loaded; the UI refreshes it when the task is renamed mid-session.
public sealed record FocusSession(
    Guid TaskId,
    string TaskTitle,
    // Wall clock at the last start or resume. Elapsed is measured against the same clock the
    // caller passes to Elapsed(), so a machine sleep or reboot can't corrupt it.
    DateTimeOffset StartedAtUtc,
    // Time banked from earlier running segments, before the current one.
    TimeSpan Accumulated,
    bool Paused);

public static class FocusTimer
{
    public static TimeSpan Elapsed(FocusSession session, DateTimeOffset now)
    {
        if (session.Paused) return session.Accumulated;
        var segment = now - session.StartedAtUtc;
        return session.Accumulated + (segment < TimeSpan.Zero ? TimeSpan.Zero : segment);
    }

    // Banks the running segment into Accumulated and stops the clock.
    public static FocusSession Pause(FocusSession session, DateTimeOffset now)
        => session.Paused
            ? session
            : session with { Accumulated = Elapsed(session, now), Paused = true };

    // Restarts the clock from now; Accumulated already holds everything before this segment.
    public static FocusSession Resume(FocusSession session, DateTimeOffset now)
        => session.Paused
            ? session with { StartedAtUtc = now, Paused = false }
            : session;

    // Where the current minute stands, 0..1. A count-up stopwatch has no total to fill, so the
    // readout's ring borrows a clock's second hand instead — one lap a minute. It is honest
    // about what it measures (the seconds you can already read beside it) and still says
    // "running" at a glance, which a bar with no end never could.
    public static double MinuteFraction(TimeSpan elapsed)
        => elapsed <= TimeSpan.Zero ? 0 : elapsed.TotalMilliseconds % 60_000d / 60_000d;

    // "M:SS" under an hour, "H:MM:SS" from an hour on. Negative clamps to zero.
    public static string Format(TimeSpan elapsed)
    {
        var totalSeconds = (long)elapsed.TotalSeconds;
        if (totalSeconds < 0) totalSeconds = 0;
        var hours   = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours}:{minutes:D2}:{seconds:D2}"
            : $"{minutes}:{seconds:D2}";
    }
}
