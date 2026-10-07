namespace Hatch.Helpers;

internal static class CrashLog
{
    private static readonly object Sync = new();
    private const long MaxLength = 1_000_000;

    internal static string Path => System.IO.Path.Combine(AppDataPath.Folder, "crash.log");

    internal static void Write(string source, string details)
    {
        try
        {
            var entry = $"[{DateTimeOffset.Now:O}] {source} (process {Environment.ProcessId}){Environment.NewLine}"
                + details + Environment.NewLine + Environment.NewLine;
            lock (Sync)
            {
                // A crash may terminate the process immediately after this handler returns.
                Directory.CreateDirectory(AppDataPath.Folder);
                if (File.Exists(Path) && new FileInfo(Path).Length >= MaxLength)
                    File.WriteAllText(Path, entry);
                else
                    File.AppendAllText(Path, entry);
            }
        }
        catch
        {
            // Last-chance logging must not replace the original crash.
        }
    }
}
