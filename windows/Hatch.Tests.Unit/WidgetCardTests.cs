using System.Text.Json.Nodes;
using Hatch.Models;
using Hatch.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class WidgetCardTests
{
    [TestMethod]
    public void EmptyMyDay_HasValidCardAndCaptureActions()
    {
        var snapshot = MyDayWidgetCardBuilder.CreateSnapshot(new TasksFile());
        var card = JsonNode.Parse(MyDayWidgetCardBuilder.Build(snapshot, "medium"))!;
        Assert.AreEqual("AdaptiveCard", card["type"]!.GetValue<string>());
        Assert.AreEqual("1.5", card["version"]!.GetValue<string>());
        Assert.IsTrue(Nodes(card).Any(node => node["text"]?.GetValue<string>() == "Nothing planned yet"));
        Assert.IsTrue(Nodes(card).Any(node => node["verb"]?.GetValue<string>() == "add"));
        Assert.AreEqual("myday", card["actions"]![0]!["verb"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow("small", 0)]
    [DataRow("medium", 3)]
    [DataRow("large", 6)]
    public void CardSizes_PreserveTaskPreviewLimits(string size, int expected)
    {
        var snapshot = new MyDayWidgetSnapshot(Enumerable.Range(0, 8)
            .Select(index => new MyDayWidgetTask(Guid.NewGuid(), "Fixture task " + index, null, "default", false)).ToList(),
            0, 8, DateOnly.FromDateTime(DateTime.Today));
        var card = JsonNode.Parse(MyDayWidgetCardBuilder.Build(snapshot, size))!;
        Assert.AreEqual(expected, Nodes(card).Count(node => node["text"]?.GetValue<string>()?.StartsWith("Fixture task ") == true));
    }

    [TestMethod]
    public void Snapshot_FiltersDeletedAndOldTasksAndCountsCompletion()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var snapshot = MyDayWidgetCardBuilder.CreateSnapshot(new TasksFile
        {
            Tasks =
            [
                new TodoItem { Title = "Current", IsInMyDay = true, MyDayDate = today },
                new TodoItem { Title = "Legacy current", IsInMyDay = true },
                new TodoItem { Title = "Done", IsInMyDay = true, MyDayDate = today, IsCompleted = true },
                new TodoItem { Title = "Old", IsInMyDay = true, MyDayDate = today.AddDays(-1) },
                new TodoItem { Title = "Deleted", IsInMyDay = true, MyDayDate = today, IsDeleted = true },
                new TodoItem { Title = "Outside My Day" }
            ]
        });
        Assert.AreEqual(3, snapshot.Total);
        Assert.AreEqual(1, snapshot.Done);
        CollectionAssert.AreEquivalent(new[] { "Current", "Legacy current" }, snapshot.OpenTasks.Select(task => task.Title).ToArray());
    }

    [TestMethod]
    public void TaskActions_RetainIdentityAndJsonEscaping()
    {
        var id = Guid.NewGuid();
        var title = "Review \"quoted\" task\nsecond line";
        var snapshot = new MyDayWidgetSnapshot([new MyDayWidgetTask(id, title, "Today · Work", "attention", true)],
            0, 1, DateOnly.FromDateTime(DateTime.Today));
        var card = JsonNode.Parse(MyDayWidgetCardBuilder.Build(snapshot, "large"))!;
        var nodes = Nodes(card).ToArray();
        Assert.IsTrue(nodes.Any(node => node["text"]?.GetValue<string>() == title));
        Assert.IsTrue(nodes.Any(node => node["verb"]?.GetValue<string>() == $"complete:{id}"));
        Assert.IsTrue(nodes.Any(node => node["verb"]?.GetValue<string>() == $"open:{id}"));
        Assert.IsTrue(nodes.Where(node => node["type"]?.GetValue<string>() == "Action.Execute")
            .All(node => node["associatedInputs"]?.GetValue<string>() == "none"));
    }

    private static IEnumerable<JsonObject> Nodes(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var child in obj.Select(pair => pair.Value).OfType<JsonNode>())
                foreach (var descendant in Nodes(child)) yield return descendant;
        }
        else if (node is JsonArray array)
            foreach (var child in array.OfType<JsonNode>())
                foreach (var descendant in Nodes(child)) yield return descendant;
    }
}
