namespace Hatch.Helpers;

internal static class CalendarCacheFile
{
    internal static async Task ReplaceAsync(string temporary, string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && IsTransientLock(ex) && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 << attempt), cancellationToken);
            }
        }
    }

    private static bool IsTransientLock(Exception error) => (error.HResult & 0xFFFF) is 5 or 32 or 33;
}
