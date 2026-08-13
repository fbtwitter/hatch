using Hatch.Helpers;
using Hatch.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class MascotOpenPageHelperTests
{
    [TestMethod]
    public void NullTag_MeansRememberLast_PassesThrough()
    {
        var result = MascotOpenPageHelper.Resolve(null, []);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void EmptyTag_PassesThroughUnchanged()
    {
        var result = MascotOpenPageHelper.Resolve("", []);

        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void FixedTag_PassesThroughUnchanged()
    {
        var result = MascotOpenPageHelper.Resolve("myday", []);

        Assert.AreEqual("myday", result);
    }

    [TestMethod]
    public void SummaryTag_PassesThroughUnchanged()
    {
        var result = MascotOpenPageHelper.Resolve("summary", []);

        Assert.AreEqual("summary", result);
    }

    [TestMethod]
    public void CustomListTag_StillExists_PassesThroughUnchanged()
    {
        var listId = Guid.NewGuid();
        var lists = new[] { new TaskList { Id = listId, Name = "Groceries" } };

        var result = MascotOpenPageHelper.Resolve(listId.ToString(), lists);

        Assert.AreEqual(listId.ToString(), result);
    }

    [TestMethod]
    public void CustomListTag_NoLongerExists_FallsBackToSummary()
    {
        var deletedListId = Guid.NewGuid();

        var result = MascotOpenPageHelper.Resolve(deletedListId.ToString(), []);

        Assert.AreEqual(MascotOpenPageHelper.SummaryFallbackTag, result);
    }

    [TestMethod]
    public void CustomListTag_OnlyOtherListsExist_FallsBackToSummary()
    {
        var deletedListId = Guid.NewGuid();
        var otherLists = new[] { new TaskList { Id = Guid.NewGuid(), Name = "Work" } };

        var result = MascotOpenPageHelper.Resolve(deletedListId.ToString(), otherLists);

        Assert.AreEqual(MascotOpenPageHelper.SummaryFallbackTag, result);
    }
}
