using System.Drawing;
using Hatch.Helpers;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class MascotPopupPlacementTests
{
    [TestMethod]
    public void EdgesAndScales_KeepContentInWorkAreaAndAwayFromMascot()
    {
        foreach (var work in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1280, -720, 1280, 680) })
        foreach (double scale in new[] { 1d, 1.25, 1.5, 2, 3 })
        foreach (bool above in new[] { false, true })
        foreach (int x in new[] { work.Left, work.Left + work.Width / 2, work.Right - 120 })
        foreach (int y in new[] { work.Top, work.Top + work.Height / 2, work.Bottom - 120 })
        {
            var mascot = new Rectangle(x, y, 120, 120);
            var result = MascotPopupPlacement.Place(work, mascot,
                new Size((int)(340 * scale), (int)(600 * scale)), (int)(12 * scale), above);
            Assert.IsTrue(work.Contains(result), $"Outside work area: {result}");
            Assert.IsFalse(mascot.IntersectsWith(result), $"Overlaps mascot: {result}");
            Assert.IsTrue(result.Width > 0 && result.Height > 0);
        }
    }

    [TestMethod]
    public void TopEdge_FocusFallsBelowWithoutShrinking()
    {
        var result = MascotPopupPlacement.Place(new Rectangle(0, 0, 800, 600),
            new Rectangle(340, 0, 120, 120), new Size(280, 100), 12, preferAbove: true);
        Assert.AreEqual(new Size(280, 100), result.Size);
        Assert.AreEqual(132, result.Top);
    }

    [TestMethod]
    public void NeitherSideFits_QuickAddUsesSpaceAbove()
    {
        var result = MascotPopupPlacement.Place(new Rectangle(0, 0, 500, 900),
            new Rectangle(190, 700, 120, 120), new Size(340, 500), 12);
        Assert.AreEqual(new Size(340, 500), result.Size);
        Assert.AreEqual(688, result.Bottom);
    }

    [TestMethod]
    public void TinyWorkArea_OversizedContentDoesNotThrow()
    {
        var work = new Rectangle(-20, 10, 40, 30);
        var result = MascotPopupPlacement.Place(work, work, new Size(1000, 1000), 12);
        Assert.IsTrue(work.Contains(result));
        Assert.IsTrue(result.Width > 0 && result.Height > 0);
    }
}
