using Hatch.Helpers;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class CalendarCacheFileTests
{
    [TestMethod]
    [SupportedOSPlatform("windows")]
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
            var candidates = CalendarCacheFile.ReadCandidates(path).ToArray();
            Assert.AreEqual(snapshot, candidates[0]);
            Assert.AreEqual(path, candidates[^1]);
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
            var candidates = CalendarCacheFile.ReadCandidates(path).ToArray();
            CollectionAssert.AreEqual(new[] { secondSnapshot, firstSnapshot }, candidates.Take(2).ToArray());
            Assert.AreEqual(path, candidates[^1]);
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

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task SnapshotLoadFindsKnownSnapshotWhenDirectoryCannotBeEnumerated()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("This check exercises Windows directory ACL behavior.");

        var folder = CreateTempFolder();
        var directory = new DirectoryInfo(folder);
        var path = Path.Combine(folder, "calendar.json");
        var snapshot = path + ".snapshot-0.json";
        FileSystemAccessRule? denyListRule = null;
        try
        {
            await File.WriteAllTextAsync(path, "legacy");
            await File.WriteAllTextAsync(snapshot, "updated");
            var restrictedAcl = directory.GetAccessControl();
            denyListRule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ListDirectory, AccessControlType.Deny);
            restrictedAcl.AddAccessRule(denyListRule);
            directory.SetAccessControl(restrictedAcl);

            var candidates = CalendarCacheFile.ReadCandidates(path).ToArray();

            Assert.AreEqual(snapshot, candidates[0]);
        }
        finally
        {
            if (denyListRule != null)
            {
                var cleanupAcl = directory.GetAccessControl();
                cleanupAcl.RemoveAccessRuleSpecific(denyListRule);
                directory.SetAccessControl(cleanupAcl);
            }
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task SnapshotSaveSucceedsWhenDirectoryCannotBeEnumerated()
    {
        var folder = CreateTempFolder();
        var directory = new DirectoryInfo(folder);
        var path = Path.Combine(folder, "calendar.json");
        var temporary = path + ".new";
        FileSystemAccessRule? denyListRule = null;
        try
        {
            await File.WriteAllTextAsync(temporary, "updated");
            var restrictedAcl = directory.GetAccessControl();
            denyListRule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ListDirectory, AccessControlType.Deny);
            restrictedAcl.AddAccessRule(denyListRule);
            directory.SetAccessControl(restrictedAcl);

            var snapshot = await CalendarCacheFile.WriteSnapshotAsync(temporary, path, CancellationToken.None);

            Assert.AreEqual("updated", await File.ReadAllTextAsync(snapshot));
            Assert.IsFalse(File.Exists(temporary));
        }
        finally
        {
            if (denyListRule != null)
            {
                var cleanupAcl = directory.GetAccessControl();
                cleanupAcl.RemoveAccessRuleSpecific(denyListRule);
                directory.SetAccessControl(cleanupAcl);
            }
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task SnapshotWriteUsesAnotherSlotWhenOldestSnapshotIsLocked()
    {
        var folder = CreateTempFolder();
        var path = Path.Combine(folder, "calendar.json");
        var temporary = path + ".new";
        try
        {
            string oldestSnapshot = "";
            for (var index = 0; index < 3; index++)
            {
                await File.WriteAllTextAsync(temporary, $"snapshot-{index}");
                var snapshot = await CalendarCacheFile.WriteSnapshotAsync(temporary, path, CancellationToken.None);
                if (index == 0) oldestSnapshot = snapshot;
                await Task.Delay(2);
            }

            await File.WriteAllTextAsync(temporary, "latest");
            using var snapshotLock = new FileStream(oldestSnapshot, FileMode.Open, FileAccess.Read, FileShare.Read);
            var latestSnapshot = await CalendarCacheFile.WriteSnapshotAsync(temporary, path, CancellationToken.None);

            Assert.AreNotEqual(oldestSnapshot, latestSnapshot);
            Assert.AreEqual("snapshot-0", await File.ReadAllTextAsync(oldestSnapshot));
            Assert.AreEqual("latest", await File.ReadAllTextAsync(latestSnapshot));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task LegacySnapshotRemainsReadableAlongsideFixedSlots()
    {
        var folder = CreateTempFolder();
        var path = Path.Combine(folder, "calendar.json");
        var legacySnapshot = path + ".snapshot-063867481234567890-0123456789abcdef0123456789abcdef.json";
        try
        {
            await File.WriteAllTextAsync(legacySnapshot, "legacy snapshot");

            var candidates = CalendarCacheFile.ReadCandidates(path).ToArray();

            CollectionAssert.Contains(candidates, legacySnapshot);
            Assert.AreEqual(path, candidates[^1]);
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
