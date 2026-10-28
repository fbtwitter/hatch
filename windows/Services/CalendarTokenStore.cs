using Hatch.Helpers;
using Hatch.Models;
using Windows.Security.Credentials;

namespace Hatch.Services;

internal static class CalendarTokenStore
{
    private static string Resource(CalendarAccount account) => account.Provider switch
    {
        CalendarProvider.Microsoft => "Hatch.Calendar.Microsoft",
        CalendarProvider.Google => "Hatch.Calendar.Google",
        _ => throw new InvalidOperationException("Unknown calendar provider.")
    };

    internal static string? Load(CalendarAccount account)
    {
        if (AppDataPath.IsUiTest || account.Provider == CalendarProvider.Ics) return null;
        var credential = Find(account);
        if (credential == null) return null;
        credential.RetrievePassword();
        return credential.Password;
    }

    internal static void Save(CalendarAccount account, string refreshToken)
    {
        if (AppDataPath.IsUiTest) throw new InvalidOperationException("Calendar credentials are disabled in UI tests.");
        var vault = new PasswordVault();
        var previous = Find(account);
        previous?.RetrievePassword();
        if (previous != null) vault.Remove(previous);
        try { vault.Add(new PasswordCredential(Resource(account), account.Id, refreshToken)); }
        catch
        {
            if (previous != null) vault.Add(previous);
            throw;
        }
    }

    internal static void Clear(CalendarAccount account)
    {
        if (AppDataPath.IsUiTest || account.Provider == CalendarProvider.Ics) return;
        if (Find(account) is { } credential) new PasswordVault().Remove(credential);
    }

    private static PasswordCredential? Find(CalendarAccount account)
    {
        try { return new PasswordVault().Retrieve(Resource(account), account.Id); }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070490)) { return null; }
    }
}
