using Hatch.Helpers;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class CalendarCacheFileTests
{
    [TestMethod]
    public async Task ReplaceRetriesUntilWindowsSharingLockClears()
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
            using var releaseLock = new Timer(_ => cacheLock.Dispose(), null, 250, Timeout.Infinite);

            await CalendarCacheFile.ReplaceAsync(temporary, path, CancellationToken.None);

            Assert.AreEqual("updated", await File.ReadAllTextAsync(path));
            Assert.IsFalse(File.Exists(temporary));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task PersistentWindowsSharingLockLeavesExistingCacheUntouched()
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

            var error = await Assert.ThrowsAsync<Exception>(() => CalendarCacheFile.ReplaceAsync(temporary, path, CancellationToken.None));

            Assert.IsTrue(error is IOException or UnauthorizedAccessException);
            Assert.AreEqual("previous", await File.ReadAllTextAsync(path));
            Assert.IsTrue(File.Exists(temporary));
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
