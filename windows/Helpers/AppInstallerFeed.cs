using System.Xml;
using System.Xml.Linq;

namespace Hatch.Helpers;

internal static class AppInstallerFeed
{
    public static Version ReadVersion(Stream stream, string packageName, string publisher)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var feed = XDocument.Load(reader);
        XNamespace ns = "http://schemas.microsoft.com/appx/appinstaller/2021";
        var bundle = feed.Root?.Element(ns + "MainBundle");

        if (feed.Root?.Name != ns + "AppInstaller" ||
            bundle?.Attribute("Name")?.Value != packageName ||
            bundle.Attribute("Publisher")?.Value != publisher ||
            !Version.TryParse(bundle.Attribute("Version")?.Value, out var version) ||
            version.Revision < 0)
            throw new InvalidDataException("The App Installer feed does not describe this Hatch package.");

        return version;
    }
}
