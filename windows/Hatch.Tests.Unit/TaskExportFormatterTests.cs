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
}
