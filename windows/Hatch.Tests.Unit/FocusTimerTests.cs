using Hatch.Helpers;

namespace Hatch.Tests.Unit;

// Mirrors hatch-mobile shared/commonTest/kotlin/dev/hatch/sync/FocusTimerTest.kt. FocusTimer is
// the whole of focus mode's testable logic — the platform side is just a timer tick, a settings
// blob and a popup. These pin the elapsed-time arithmetic across a pause/resume cycle, which is
// the one place it could drift from the Kotlin copy.
[TestClass]
public sealed class FocusTimerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private static FocusSession Session(TimeSpan? accumulated = null, bool paused = false) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Write the report",
            Start, accumulated ?? TimeSpan.Zero, paused);

    [TestMethod]
    public void Elapsed_WhileRunning_IsWallClockSinceStartPlusBanked()
    {
        var s = Session(TimeSpan.FromSeconds(5));
        Assert.AreEqual(TimeSpan.FromSeconds(30), FocusTimer.Elapsed(s, Start.AddSeconds(25)));
    }

    [TestMethod]
    public void Elapsed_WhilePaused_IsJustTheBankedTime()
    {
        var s = Session(TimeSpan.FromSeconds(42), paused: true);
        Assert.AreEqual(TimeSpan.FromSeconds(42), FocusTimer.Elapsed(s, Start.AddHours(3)));
    }

    [TestMethod]
    public void Elapsed_AClockThatWentBackwards_NeverReportsNegative()
    {
        Assert.AreEqual(TimeSpan.Zero, FocusTimer.Elapsed(Session(), Start.AddSeconds(-60)));
    }

    [TestMethod]
    public void PauseResumePause_SumsBothRunningSegments()
    {
        var afterFirstPause = FocusTimer.Pause(Session(), Start.AddSeconds(10));
        Assert.IsTrue(afterFirstPause.Paused);
        Assert.AreEqual(TimeSpan.FromSeconds(10), afterFirstPause.Accumulated);

        var resumed = FocusTimer.Resume(afterFirstPause, Start.AddSeconds(100));
        Assert.IsFalse(resumed.Paused);
        Assert.AreEqual(Start.AddSeconds(100), resumed.StartedAtUtc);

        var afterSecondPause = FocusTimer.Pause(resumed, Start.AddSeconds(105));
        Assert.AreEqual(TimeSpan.FromSeconds(15), afterSecondPause.Accumulated);
        Assert.AreEqual(TimeSpan.FromSeconds(15), FocusTimer.Elapsed(afterSecondPause, Start.AddHours(1)));
    }

    [TestMethod]
    public void PauseAndResume_AreEachIdempotent()
    {
        var paused = FocusTimer.Pause(Session(), Start.AddSeconds(10));
        Assert.AreEqual(paused, FocusTimer.Pause(paused, Start.AddSeconds(50)));

        var resumed = FocusTimer.Resume(paused, Start.AddSeconds(60));
        Assert.AreEqual(resumed, FocusTimer.Resume(resumed, Start.AddSeconds(70)));
    }

    [TestMethod]
    public void Format_UsesMSsUnderAnHourAndHMmSsFromAnHour()
    {
        Assert.AreEqual("0:00", FocusTimer.Format(TimeSpan.Zero));
        Assert.AreEqual("0:09", FocusTimer.Format(TimeSpan.FromSeconds(9)));
        Assert.AreEqual("0:59", FocusTimer.Format(TimeSpan.FromSeconds(59)));
        Assert.AreEqual("1:00", FocusTimer.Format(TimeSpan.FromMinutes(1)));
        Assert.AreEqual("25:00", FocusTimer.Format(TimeSpan.FromMinutes(25)));
        Assert.AreEqual("1:00:00", FocusTimer.Format(TimeSpan.FromHours(1)));
        Assert.AreEqual("2:05:07", FocusTimer.Format(new TimeSpan(2, 5, 7)));
    }

    [TestMethod]
    public void Format_ClampsANegativeDurationToZero()
    {
        Assert.AreEqual("0:00", FocusTimer.Format(TimeSpan.FromSeconds(-5)));
    }

    [TestMethod]
    public void MinuteFraction_LapsOnceAMinute()
    {
        Assert.AreEqual(0d, FocusTimer.MinuteFraction(TimeSpan.Zero), 1e-9);
        Assert.AreEqual(0.5d, FocusTimer.MinuteFraction(TimeSpan.FromSeconds(30)), 1e-9);
        // The lap boundary: a full minute is the start of the next sweep, not the end of one.
        Assert.AreEqual(0d, FocusTimer.MinuteFraction(TimeSpan.FromSeconds(60)), 1e-9);
        Assert.AreEqual(0.25d, FocusTimer.MinuteFraction(TimeSpan.FromSeconds(75)), 1e-9);
        Assert.AreEqual(0d, FocusTimer.MinuteFraction(TimeSpan.FromSeconds(-5)), 1e-9);
    }
}
