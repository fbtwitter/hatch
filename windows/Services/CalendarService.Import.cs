using System.Text;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.Services;

public sealed partial class CalendarService
{
    public async Task ImportAsync(string path, string? replaceId = null, CancellationToken cancellationToken = default)
    {
        await InitializeAsync();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(Path.GetExtension(path), ".ics", StringComparison.OrdinalIgnoreCase))
                throw new ProviderException("Calendar_ImportInvalid");
            var previous = replaceId == null ? null : _accounts.FirstOrDefault(a => a.Id == replaceId && a.Provider == CalendarProvider.Ics);
            if (replaceId != null && previous == null) throw new ProviderException("Calendar_ImportMissing");
            var fileName = Path.GetFileName(path);
            if (_accounts.Any(a => a.Provider == CalendarProvider.Ics && a.Id != replaceId &&
                string.Equals(a.Email, fileName, StringComparison.OrdinalIgnoreCase)))
                throw new ProviderException("Calendar_ImportExists");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > IcsCalendarImport.MaxFileBytes) throw new ProviderException("Calendar_ImportTooLarge");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            CalendarAccount snapshot;
            try { snapshot = IcsCalendarImport.Parse(text, fileName, previous?.Id ?? "ics-" + Guid.NewGuid().ToString("N"), DateTimeOffset.Now); }
            catch (FormatException ex) when (ex.Message is "Calendar_ImportTooLarge" or "Calendar_ImportTooMany")
            { throw new ProviderException(ex.Message); }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            { throw new ProviderException("Calendar_ImportInvalid"); }
            var accounts = previous == null ? _accounts.Append(snapshot).ToList()
                : _accounts.Select(a => a.Id == previous.Id ? snapshot : a).ToList();
            await SaveAsync(accounts, cancellationToken);
            _accounts = accounts;
            Error = null;
            ReconcileReminders();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        { throw new ProviderException("Calendar_ImportRead"); }
        finally { _gate.Release(); StateChanged?.Invoke(); }
    }
}
