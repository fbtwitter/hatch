using Hatch.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class RecoveryKitTests
{
    private static readonly DateTime Created = new(2026, 7, 22, 9, 5, 0);

    private static string Kit() => RecoveryKit.Build("correct horse battery", "user@example.com", Created);

    [TestMethod]
    public void Carries_the_passphrase_and_account()
    {
        var kit = Kit();

        Assert.IsTrue(kit.Contains("correct horse battery"), "the passphrase is the point of the kit");
        Assert.IsTrue(kit.Contains("user@example.com"));
    }

    [TestMethod]
    public void States_that_nobody_can_recover_it()
    {
        // A kit that undersells this gets filed somewhere lossy.
        var kit = Kit();

        Assert.IsTrue(kit.Contains("reset it or recover it for you"));
        Assert.IsTrue(kit.Contains("permanently unreadable"));
    }

    [TestMethod]
    public void Distinguishes_itself_from_the_other_two_secrets()
    {
        // Three secrets now exist. Saving the wrong one and feeling covered is the failure
        // this paragraph exists to prevent.
        var kit = Kit();

        Assert.IsTrue(kit.Contains("not your account password"));
        Assert.IsTrue(kit.Contains("not a two-factor recovery code"));
    }

    [TestMethod]
    public void Says_to_keep_it_off_this_machine()
        => Assert.IsTrue(Kit().Contains("after this computer is gone"));

    [TestMethod]
    public void Is_stamped_with_its_creation_time()
        => Assert.IsTrue(Kit().Contains("2026-07-22 09:05"));

    [TestMethod]
    public void File_name_is_dated_and_has_no_path_hostile_characters()
    {
        var name = RecoveryKit.FileName(Created);

        Assert.AreEqual("hatch-recovery-kit-2026-07-22", name);
        Assert.AreEqual(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
    }
}
