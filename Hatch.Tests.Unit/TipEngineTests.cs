using Hatch.Models;
using Hatch.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class TipEngineTests
{
    private static TodoItem Task(bool completed = false, DateTimeOffset? dueDate = null, bool inMyDay = false) => new()
    {
        Title = "t",
        IsCompleted = completed,
        DueDate = dueDate,
        IsInMyDay = inMyDay
    };

    [TestMethod]
    public void OverdueTask_TakesPriorityOverEverythingElse()
    {
        var engine = new TipEngine();
        var tasks = new List<TodoItem>
        {
            Task(dueDate: DateTimeOffset.Now.AddDays(-2), inMyDay: true) // overdue AND My Day non-empty
        };

        var tip = engine.GetTip(tasks, lastMeaningfulTip: null, lastActivity: null);

        Assert.IsNotNull(tip);
        Assert.AreEqual(TipSeverity.Critical, tip!.Severity);
        Assert.AreEqual(TipActionType.ViewOverdue, tip.Action?.Type);
        Assert.IsTrue(tip.IsMeaningful);
    }

    [TestMethod]
    public void EmptyMyDay_PromptsPlanning_WhenNoOverdueTasks()
    {
        var engine = new TipEngine();
        var tasks = new List<TodoItem> { Task(dueDate: DateTimeOffset.Now.AddDays(3)) }; // not overdue, not in My Day

        var tip = engine.GetTip(tasks, lastMeaningfulTip: null, lastActivity: null);

        Assert.IsNotNull(tip);
        Assert.AreEqual(TipActionType.ViewMyDay, tip!.Action?.Type);
    }

    // The empty-My-Day check (tasks.Count(IsInMyDay && !IsCompleted) == 0) runs before the
    // empty-task-list check, and an empty list trivially has zero My-Day tasks — so the
    // "add sample task" fallback tip is unreachable whenever there are no tasks at all.
    // Documenting actual behavior here, not fixing it (out of scope for this pass).
    [TestMethod]
    public void EmptyTaskList_ReturnsMyDayEmptyTip_NotAddSampleFallback()
    {
        var engine = new TipEngine();
        var tip = engine.GetTip([], lastMeaningfulTip: null, lastActivity: null);

        Assert.IsNotNull(tip);
        Assert.AreEqual(TipActionType.ViewMyDay, tip!.Action?.Type);
    }

    [TestMethod]
    public void FiveOrMoreCompleted_ShowsProgressCelebration_WhenMyDayNotEmpty()
    {
        var engine = new TipEngine();
        var tasks = new List<TodoItem> { Task(inMyDay: true) };
        for (int i = 0; i < 5; i++)
            tasks.Add(Task(completed: true));

        var tip = engine.GetTip(tasks, lastMeaningfulTip: null, lastActivity: null);

        Assert.IsNotNull(tip);
        Assert.IsTrue(tip!.Message.Contains("5 tasks completed"));
        Assert.IsTrue(tip.IsMeaningful);
    }

    // To reach the fallback-greeting branch at all, overdue/My-Day-empty/5-completed must
    // all be false: My Day must be non-empty, and at least one task must be open.
    private static List<TodoItem> FallbackReachableTasks() => [Task(inMyDay: true)];

    [TestMethod]
    public void FallbackGreeting_SuppressedWhenUserRecentlyActiveAndMeaningfulTipRecent()
    {
        var engine = new TipEngine();

        var tip = engine.GetTip(
            FallbackReachableTasks(),
            lastMeaningfulTip: DateTime.Now.AddMinutes(-10),
            lastActivity: DateTime.Now.AddMinutes(-1));

        Assert.IsNull(tip);
    }

    [TestMethod]
    public void FallbackGreeting_ShownWhenUserInactiveLongEnough()
    {
        var engine = new TipEngine();

        var tip = engine.GetTip(
            FallbackReachableTasks(),
            lastMeaningfulTip: DateTime.Now.AddHours(-10),
            lastActivity: DateTime.Now.AddMinutes(-10));

        Assert.IsNotNull(tip);
        Assert.IsFalse(tip!.IsMeaningful);
    }
}
