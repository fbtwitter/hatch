using Hatch.Helpers;
using Hatch.Models;
using Hatch.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class TipEngineTests
{
    // Identity resolver: Tip.Message carries the resource key (format args are ignored
    // because the key itself contains no {0} placeholders), so tests assert on keys
    // without touching the WinUI resource pipeline.
    private static TipEngine Engine() => new(key => key);

    // Fixed 10:00 local time — morning, outside the evening rules.
    private static readonly DateTime Morning = DateTime.Today.AddHours(10);
    private static readonly DateTime Evening = DateTime.Today.AddHours(19);

    private static TodoItem Task(bool completed = false, DateTimeOffset? dueDate = null,
        bool inMyDay = false, DateTimeOffset? completedAt = null, DateTime? createdAt = null)
    {
        var t = new TodoItem
        {
            Title = "t",
            IsCompleted = completed,
            DueDate = dueDate,
            IsInMyDay = inMyDay
        };
        if (completedAt.HasValue) t.CompletedAt = completedAt;
        if (createdAt.HasValue) t.CreatedAt = createdAt.Value;
        return t;
    }

    [TestMethod]
    public void OverdueTask_TakesPriorityOverEverythingElse()
    {
        var tasks = new List<TodoItem>
        {
            Task(dueDate: DateTimeOffset.Now.AddDays(-2), inMyDay: true)
        };

        var tip = Engine().GetTip(tasks, now: Morning);

        Assert.IsNotNull(tip);
        Assert.AreEqual("Tip_Overdue_One", tip!.Message);
        Assert.AreEqual(TipSeverity.Critical, tip.Severity);
        Assert.AreEqual(TipActionType.ViewOverdue, tip.Action?.Type);
        Assert.IsTrue(tip.IsMeaningful);
    }

    [TestMethod]
    public void MultipleOverdueTasks_UsePluralKey()
    {
        var tasks = new List<TodoItem>
        {
            Task(dueDate: DateTimeOffset.Now.AddDays(-2)),
            Task(dueDate: DateTimeOffset.Now.AddDays(-1))
        };

        var tip = Engine().GetTip(tasks, now: Morning);

        Assert.AreEqual("Tip_Overdue_Many", tip!.Message);
    }

    [TestMethod]
    public void DueToday_BeatsMyDayEmpty_ButNotOverdue()
    {
        var tasks = new List<TodoItem>
        {
            Task(dueDate: new DateTimeOffset(DateTime.Today.AddHours(12)))
        };

        var tip = Engine().GetTip(tasks, now: Morning);

        Assert.AreEqual("Tip_DueToday_One", tip!.Message);
        Assert.AreEqual(TipActionType.ViewPlanned, tip.Action?.Type);
        Assert.IsTrue(tip.IsMeaningful);
    }

    [TestMethod]
    public void EmptyMyDay_PromptsPlanning_OnlyWhenMyDayWasUsedBefore()
    {
        var neverUsedMyDay = new List<TodoItem> { Task(createdAt: DateTime.Now) };
        var usedMyDayBefore = new List<TodoItem>
        {
            Task(createdAt: DateTime.Now),
            Task(completed: true, completedAt: DateTimeOffset.Now.AddDays(-3))
        };
        usedMyDayBefore[1].MyDayDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-3));

        var tipWithoutHistory = Engine().GetTip(neverUsedMyDay, now: Morning);
        var tipWithHistory = Engine().GetTip(usedMyDayBefore, now: Morning);

        Assert.AreNotEqual("Tip_MyDayEmpty", tipWithoutHistory?.Message);
        Assert.AreEqual("Tip_MyDayEmpty", tipWithHistory!.Message);
        Assert.AreEqual(TipSeverity.Warning, tipWithHistory.Severity);
        Assert.AreEqual(TipActionType.ViewMyDay, tipWithHistory.Action?.Type);
    }

    [TestMethod]
    public void EmptyMyDay_NotPrompted_InTheEvening()
    {
        var tasks = new List<TodoItem>
        {
            Task(createdAt: DateTime.Now),
            Task(completed: true, completedAt: DateTimeOffset.Now.AddDays(-3))
        };
        tasks[1].MyDayDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-3));

        var tip = Engine().GetTip(tasks, now: Evening);

        Assert.AreNotEqual("Tip_MyDayEmpty", tip?.Message);
    }

    [TestMethod]
    public void EmptyTaskList_ReturnsAddSampleTip()
    {
        var tip = Engine().GetTip([], now: Morning);

        Assert.IsNotNull(tip);
        Assert.AreEqual("Tip_EmptyList", tip!.Message);
        Assert.AreEqual(TipActionType.AddSampleTask, tip.Action?.Type);
    }

    [TestMethod]
    public void EveningWithClearMyDayAndCompletions_SuggestsPlanningTomorrow()
    {
        var tasks = new List<TodoItem>
        {
            Task(createdAt: DateTime.Now),
            Task(completed: true, completedAt: DateTimeOffset.Now)
        };

        var tip = Engine().GetTip(tasks, now: Evening);

        Assert.AreEqual("Tip_EveningWrapUp", tip!.Message);
        Assert.AreEqual(TipActionType.ViewMyDay, tip.Action?.Type);
        Assert.IsTrue(tip.IsMeaningful);
    }

    [TestMethod]
    public void CompletionCelebration_CountsOnlyToday()
    {
        var completedLongAgo = new List<TodoItem> { Task(inMyDay: true) };
        for (int i = 0; i < 5; i++)
            completedLongAgo.Add(Task(completed: true, completedAt: DateTimeOffset.Now.AddDays(-30)));

        var completedToday = new List<TodoItem> { Task(inMyDay: true) };
        for (int i = 0; i < 5; i++)
            completedToday.Add(Task(completed: true, completedAt: DateTimeOffset.Now));

        var tipOld = Engine().GetTip(completedLongAgo, now: Morning);
        var tipToday = Engine().GetTip(completedToday, now: Morning);

        Assert.AreNotEqual("Tip_CompletedToday", tipOld?.Message);
        Assert.AreEqual("Tip_CompletedToday", tipToday!.Message);
        Assert.IsTrue(tipToday.IsMeaningful);
    }

    [TestMethod]
    public void StaleTask_Nudged_WhenOldUndatedAndNothingElseApplies()
    {
        var tasks = new List<TodoItem>
        {
            Task(inMyDay: true),
            Task(createdAt: DateTime.Now.AddDays(-20))
        };

        var tip = Engine().GetTip(tasks, now: Morning);

        Assert.AreEqual("Tip_StaleTask", tip!.Message);
        Assert.AreEqual(TipActionType.OpenMainWindow, tip.Action?.Type);
    }

    [TestMethod]
    public void RecentTask_NotNudgedAsStale()
    {
        var tasks = new List<TodoItem>
        {
            Task(inMyDay: true),
            Task(createdAt: DateTime.Now.AddDays(-5))
        };

        var tip = Engine().GetTip(tasks, now: Morning);

        Assert.AreNotEqual("Tip_StaleTask", tip?.Message);
    }

    // To reach the fallback-greeting branch, every meaningful rule must be false:
    // My Day non-empty, nothing due/overdue/stale, no completions today.
    private static List<TodoItem> FallbackReachableTasks() => [Task(inMyDay: true)];

    // Offsets are anchored to Morning, not DateTime.Now: these rules compare against the
    // injected "now", so wall-clock-relative values silently change meaning with the time
    // of day the suite runs.
    [TestMethod]
    public void FallbackGreeting_SuppressedWhenUserRecentlyActiveAndMeaningfulTipRecent()
    {
        var tip = Engine().GetTip(
            FallbackReachableTasks(),
            lastMeaningfulTip: Morning.AddMinutes(-10),
            lastActivity: Morning.AddMinutes(-1),
            now: Morning);

        Assert.IsNull(tip);
    }

    [TestMethod]
    public void FallbackGreeting_ShownWhenUserInactiveLongEnough()
    {
        var tip = Engine().GetTip(
            FallbackReachableTasks(),
            lastMeaningfulTip: Morning.AddHours(-10),
            lastActivity: Morning.AddMinutes(-10),
            now: Morning);

        Assert.IsNotNull(tip);
        Assert.IsFalse(tip!.IsMeaningful);
        StringAssert.StartsWith(tip.Message, "Tip_Greeting_Morning_");
    }

    [TestMethod]
    public void PreferredWindow_GatesProactiveTipsByHour()
    {
        var nineAm = DateTime.Today.AddHours(9);
        var threePm = DateTime.Today.AddHours(15);
        var eightPm = DateTime.Today.AddHours(20);

        Assert.IsTrue(TipSchedule.IsInPreferredWindow(nineAm, TipTimePreference.Anytime));
        Assert.IsTrue(TipSchedule.IsInPreferredWindow(nineAm, TipTimePreference.Morning));
        Assert.IsFalse(TipSchedule.IsInPreferredWindow(nineAm, TipTimePreference.Evening));
        Assert.IsTrue(TipSchedule.IsInPreferredWindow(threePm, TipTimePreference.Afternoon));
        Assert.IsFalse(TipSchedule.IsInPreferredWindow(threePm, TipTimePreference.Morning));
        Assert.IsTrue(TipSchedule.IsInPreferredWindow(eightPm, TipTimePreference.Evening));
        Assert.IsFalse(TipSchedule.IsInPreferredWindow(eightPm, TipTimePreference.Afternoon));
    }
}
