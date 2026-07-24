using Windows.Security.Credentials;

namespace Hatch.Services;

// Sync tokens live in the Windows Credential Locker instead of plaintext settings.json.
// Tokens persisted by older versions in settings.json are migrated on first load.
internal static class SyncTokenStore
{
    private const string Resource    = "Hatch.Sync";
    private const string AccessName  = "access_token";
    private const string RefreshName = "refresh_token";

    public static (string? Access, string? Refresh) Load()
    {
        var settings = App.Settings;
        if (!string.IsNullOrEmpty(settings.SyncAccessToken))
        {
            Save(settings.SyncAccessToken, settings.SyncRefreshToken);
            settings.SyncAccessToken  = null;
            settings.SyncRefreshToken = null;
            _ = App.SettingsService.SaveAsync();
        }
        return (Retrieve(AccessName), Retrieve(RefreshName));
    }

    public static void Save(string? access, string? refresh)
    {
        Clear();
        var vault = new PasswordVault();
        // PasswordCredential rejects empty passwords — absent tokens are simply not stored.
        if (!string.IsNullOrEmpty(access))
            vault.Add(new PasswordCredential(Resource, AccessName, access));
        if (!string.IsNullOrEmpty(refresh))
            vault.Add(new PasswordCredential(Resource, RefreshName, refresh));
    }

    public static void Clear()
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var cred in vault.FindAllByResource(Resource))
                vault.Remove(cred);
        }
        catch
        {
            // FindAllByResource throws when the resource has no entries — nothing to clear.
        }
    }

    private static string? Retrieve(string name)
    {
        try
        {
            var cred = new PasswordVault().Retrieve(Resource, name);
            cred.RetrievePassword();
            return cred.Password;
        }
        catch { return null; }
    }
}
