namespace Hatch.Helpers;

internal static class CalendarCacheFile
{
    private const int SnapshotCount = 3;

    internal static IEnumerable<string> ReadCandidates(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var prefix = Path.GetFileName(path) + ".snapshot-";
        var snapshots = Enumerable.Range(0, SnapshotCount)
            .Select(index => Path.Combine(directory, $"{Path.GetFileName(path)}.snapshot-{index}.json"))
            .OrderByDescending(GetLastWriteTimeUtc)
            .ToList();

        // Keep reading snapshots written by older app versions, but never require
        // directory enumeration to find the fixed snapshots written by this version.
        try
        {
            snapshots.AddRange(Directory.EnumerateFiles(directory, prefix + "*.json", SearchOption.TopDirectoryOnly)
                .Where(candidate => !IsFixedSnapshot(candidate, path))
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        snapshots.Add(path);
        return snapshots;
    }

    internal static Task<string> WriteSnapshotAsync(string temporary, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(path)!;
        var slots = Enumerable.Range(0, SnapshotCount)
            .Select(index => Path.Combine(directory, $"{Path.GetFileName(path)}.snapshot-{index}.json"))
            .ToArray();
        var existing = slots.Where(File.Exists).ToArray();
        var latestWrite = existing.Select(GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        var targets = slots.Where(slot => !File.Exists(slot))
            .Concat(existing.OrderBy(GetLastWriteTimeUtc))
            .ToArray();

        var writeTime = DateTime.UtcNow;
        if (writeTime <= latestWrite && latestWrite < DateTime.MaxValue)
            writeTime = latestWrite.AddTicks(1);
        try { File.SetLastWriteTimeUtc(temporary, writeTime); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        Exception? lastError = null;
        foreach (var target in targets)
        {
            try
            {
                File.Move(temporary, target, overwrite: true);
                RemoveLegacySnapshots(directory, prefix: Path.GetFileName(path) + ".snapshot-");
                return Task.FromResult(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new IOException("No calendar cache snapshot slot is available.");
    }

    private static DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    private static bool IsFixedSnapshot(string candidate, string path) =>
        Enumerable.Range(0, SnapshotCount).Any(index => string.Equals(candidate,
            path + $".snapshot-{index}.json", StringComparison.OrdinalIgnoreCase));

    private static void RemoveLegacySnapshots(string directory, string prefix)
    {
        try
        {
            foreach (var candidate in Directory.EnumerateFiles(directory, prefix + "*.json", SearchOption.TopDirectoryOnly))
            {
                if (Enumerable.Range(0, SnapshotCount).Any(index => candidate.EndsWith(
                        $".snapshot-{index}.json", StringComparison.OrdinalIgnoreCase)))
                    continue;
                try { File.Delete(candidate); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
