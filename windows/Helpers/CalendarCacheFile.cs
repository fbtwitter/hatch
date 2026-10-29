namespace Hatch.Helpers;

internal static class CalendarCacheFile
{
    private const int SnapshotCount = 3;

    internal static IEnumerable<string> ReadCandidates(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var prefix = Path.GetFileName(path) + ".snapshot-";
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, prefix + "*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Append(path)
            : [path];
    }

    internal static Task<string> WriteSnapshotAsync(string temporary, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(path)!;
        var snapshot = Path.Combine(directory,
            $"{Path.GetFileName(path)}.snapshot-{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}.json");
        File.Move(temporary, snapshot);
        PruneSnapshots(path, snapshot);
        return Task.FromResult(snapshot);
    }

    private static void PruneSnapshots(string path, string current)
    {
        var candidates = ReadCandidates(path).ToArray();
        var keep = candidates.Take(SnapshotCount).ToHashSet(StringComparer.OrdinalIgnoreCase);
        keep.Add(current);
        keep.Add(path);
        foreach (var candidate in candidates.Where(candidate => !keep.Contains(candidate)))
        {
            try { File.Delete(candidate); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
