using Hatch.Helpers;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class CalendarCacheFileTests
{
    [TestMethod]
    public async Task SnapshotWritesWithoutReplacingLockedLegacyCache()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("This check exercises Windows file sharing semantics.");

        var folder = CreateTempFolder();
        var path = Path.Combine(folder, "calendar.json");
        var temporary = path + ".new";
        try
        {
            await File.WriteAllTextAsync(path, "previous");
            await File.WriteAllTextAsync(temporary, "updated");
            using var cacheLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            var snapshot = await CalendarCacheFile.WriteSnapshotAsync(temporary, path, CancellationToken.None);

            Assert.AreEqual("previous", await File.ReadAllTextAsync(path));
            Assert.AreEqual("updated", await File.ReadAllTextAsync(snapshot));
            Assert.IsFalse(File.Exists(temporary));
            CollectionAssert.AreEqual(new[] { snapshot, path }, CalendarCacheFile.ReadCandidates(path).ToArray());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task NewSnapshotPreservesThePreviousValidCache()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("This check exercises Windows file sharing semantics.");

        var folder = CreateTempFolder();
        var path = Path.Combine(folder, "calendar.json");
        var temporary = path + ".new";
        try
        {
            await File.WriteAllTextAsync(path, "previous");
            await File.WriteAllTextAsync(temporary, "updated");
            var firstSnapshot = await CalendarCacheFile.WriteSnapshotAsync(temporary, path, CancellationToken.None);
            await File.WriteAllTextAsync(temporary, "latest");
            var secondSnapshot = await CalendarCacheFile.WriteSnapshotAsync(temporary, path, CancellationToken.None);

            Assert.AreEqual("previous", await File.ReadAllTextAsync(path));
            Assert.AreEqual("latest", await File.ReadAllTextAsync(secondSnapshot));
            CollectionAssert.AreEqual(new[] { secondSnapshot, firstSnapshot, path }, CalendarCacheFile.ReadCandidates(path).ToArray());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task SnapshotRotationKeepsLegacyCacheAndOnlyThreeSnapshots()
    {
        var folder = CreateTempFolder();
        var path = Path.Combine(folder, "calendar.json");
        var temporary = path + ".new";
        try
        {
            await File.WriteAllTextAsync(path, "legacy");
            for (var index = 0; index < 4; index++)
            {
                await File.WriteAllTextAsync(temporary, $"snapshot-{index}");
                await CalendarCacheFile.WriteSnapshotAsync(temporary, path, CancellationToken.None);
                await Task.Delay(2);
            }

            Assert.AreEqual("legacy", await File.ReadAllTextAsync(path));
            Assert.HasCount(4, CalendarCacheFile.ReadCandidates(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Hatch.CalendarCacheTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
