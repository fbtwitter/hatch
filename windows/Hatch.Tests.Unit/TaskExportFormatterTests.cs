using Hatch.Helpers;
using Hatch.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class TaskExportFormatterTests
{
    private static TasksFile SampleData()
    {
        var listId = Guid.NewGuid();
        return new TasksFile
        {
            Lists = [new TaskList { Id = listId, Name = "Work Project" }],
            Tasks =
            [
                new TodoItem { Title = "Default list task", ListId = Guid.Empty },
                new TodoItem
                {
                    Title = "Custom list task",
                    ListId = listId,
                    Priority = TaskPriority.High,
                    Tags = ["urgent", "client"],
                    DueDate = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero),
                    IsCompleted = true
                }
            ]
        };
    }

    [TestMethod]
    public void ToJson_RoundTripsTaskCount()
    {
        var json = TaskExportFormatter.ToJson(SampleData());
        Assert.IsTrue(json.Contains("Default list task"));
        Assert.IsTrue(json.Contains("Custom list task"));
    }

    [TestMethod]
    public void ToCsv_IncludesHeaderAndBothTasks()
    {
        var csv = TaskExportFormatter.ToCsv(SampleData());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual("Title,List,Due Date,Priority,Completed,Tags,Notes", lines[0].TrimEnd('\r'));
        Assert.AreEqual(3, lines.Length); // header + 2 tasks
        Assert.IsTrue(csv.Contains("Work Project"));
        Assert.IsTrue(csv.Contains("High"));
    }

    [TestMethod]
    public void ToCsv_QuotesFieldsContainingCommas()
    {
        var data = new TasksFile { Tasks = [new TodoItem { Title = "Buy milk, eggs, bread" }] };
        var csv = TaskExportFormatter.ToCsv(data);

        Assert.IsTrue(csv.Contains("\"Buy milk, eggs, bread\""));
    }

    [TestMethod]
    public void ToMarkdown_GroupsByListAndUsesCheckboxes()
    {
        var md = TaskExportFormatter.ToMarkdown(SampleData());

        Assert.IsTrue(md.Contains("## Task"));
        Assert.IsTrue(md.Contains("## Work Project"));
        Assert.IsTrue(md.Contains("- [ ] Default list task"));
        Assert.IsTrue(md.Contains("- [x] Custom list task"));
        Assert.IsTrue(md.Contains("#urgent"));
    }

    [TestMethod]
    public void ToMarkdown_EmptyData_StillProducesHeader()
    {
        var md = TaskExportFormatter.ToMarkdown(new TasksFile());
        Assert.IsTrue(md.StartsWith("# Hatch Tasks"));
    }
}
