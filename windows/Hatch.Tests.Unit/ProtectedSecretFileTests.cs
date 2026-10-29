using System.Runtime.Versioning;
using System.Text;
using Hatch.Services;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class ProtectedSecretFileTests
{
    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void SecretRoundTripsWithoutWritingPlaintext()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("DPAPI is available only on Windows.");

        var folder = CreateTempFolder();
        const string secret = "synthetic sync credential";
        try
        {
            ProtectedSecretFile.Save(folder, "test.resource", "test.name", secret);
            var file = Directory.GetFiles(folder).Single();

            Assert.AreEqual(secret, ProtectedSecretFile.Load(folder, "test.resource", "test.name"));
            Assert.IsFalse(Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains(secret, StringComparison.Ordinal));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void SecretCanBeOverwrittenAndDeleted()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("DPAPI is available only on Windows.");

        var folder = CreateTempFolder();
        try
        {
            ProtectedSecretFile.Save(folder, "test.resource", "test.name", "first");
            ProtectedSecretFile.Save(folder, "test.resource", "test.name", "second");

            Assert.AreEqual("second", ProtectedSecretFile.Load(folder, "test.resource", "test.name"));
            ProtectedSecretFile.Delete(folder, "test.resource", "test.name");
            Assert.IsNull(ProtectedSecretFile.Load(folder, "test.resource", "test.name"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static string CreateTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Hatch.ProtectedSecretTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
