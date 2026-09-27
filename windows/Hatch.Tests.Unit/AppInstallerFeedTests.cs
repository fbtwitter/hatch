using System.Text;
using Hatch.Helpers;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class AppInstallerFeedTests
{
    private const string Feed = """
        <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2021" Version="0.25.1.0">
          <MainBundle Name="Hatch" Publisher="CN=Hatch" Version="0.25.1.0" Uri="https://example.com/Hatch.msixbundle" />
        </AppInstaller>
        """;

    [TestMethod]
    public void ReadVersion_AcceptsMatchingPackage()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Feed));

        Assert.AreEqual(new Version(0, 25, 1, 0), AppInstallerFeed.ReadVersion(stream, "Hatch", "CN=Hatch"));
    }

    [TestMethod]
    public void ReadVersion_RejectsDifferentPackage()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Feed));

        Assert.ThrowsExactly<InvalidDataException>(() => AppInstallerFeed.ReadVersion(stream, "Other", "CN=Hatch"));
    }

    [TestMethod]
    public void ReadVersion_RejectsMalformedXml()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<AppInstaller>"));

        Assert.ThrowsExactly<System.Xml.XmlException>(() => AppInstallerFeed.ReadVersion(stream, "Hatch", "CN=Hatch"));
    }
}
