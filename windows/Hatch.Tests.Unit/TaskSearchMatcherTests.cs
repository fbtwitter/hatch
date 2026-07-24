using Hatch.Helpers;
using Hatch.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class TaskSearchMatcherTests
{
    [TestMethod]
    public void Matches_TitleSubstring_CaseInsensitive()
    {
        var task = new TodoItem { Title = "Buy Groceries" };
        Assert.IsTrue(TaskSearchMatcher.Matches(task, "groceries"));
        Assert.IsTrue(TaskSearchMatcher.Matches(task, "GROCERIES"));
    }

    [TestMethod]
    public void Matches_NotesSubstring()
    {
        var task = new TodoItem { Title = "Task", Notes = "remember the milk" };
        Assert.IsTrue(TaskSearchMatcher.Matches(task, "milk"));
    }

    [TestMethod]
    public void Matches_TagSubstring()
    {
        var task = new TodoItem { Title = "Task", Tags = ["work", "urgent"] };
        Assert.IsTrue(TaskSearchMatcher.Matches(task, "urg"));
    }

    [TestMethod]
    public void DoesNotMatch_WhenQueryAbsentEverywhere()
    {
        var task = new TodoItem { Title = "Task", Notes = "notes", Tags = ["tag"] };
        Assert.IsFalse(TaskSearchMatcher.Matches(task, "nonexistent"));
    }

    [TestMethod]
    public void Matches_HandlesNullNotesGracefully()
    {
        var task = new TodoItem { Title = "Task", Notes = null };
        Assert.IsFalse(TaskSearchMatcher.Matches(task, "anything"));
    }
}
