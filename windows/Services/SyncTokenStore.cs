using System.Text;
using Windows.Security.Credentials;

namespace Hatch.Services;

// Sync tokens are stored in a DPAPI-protected file. Credential Locker and the legacy
// settings fields are read only for migration, so a missing Credential Locker path does
// not prevent sign-in or token refresh from being persisted.
internal static class SyncTokenStore
{
    private const string Resource = "Hatch.Sync";
    private const string AccessName = "access_token";
    private const string RefreshName = "refresh_token";
    private static readonly string Folder = Path.Combine(Hatch.Helpers.AppDataPath.Folder, "sync-credentials");

    public static (string? Access, string? Refresh) Load(SettingsService settingsService)
    {
        var settings = settingsService.Current;
        if (!string.IsNullOrEmpty(settings.SyncAccessToken))
        {
            var access = settings.SyncAccessToken;
            var refresh = settings.SyncRefreshToken;
            Save(access, refresh);
            settings.SyncAccessToken = null;
            settings.SyncRefreshToken = null;
            settingsService.SaveDebounced();
            return (access, refresh);
        }

        if (ProtectedSecretFile.Load(Folder, Resource, "session") is { } saved)
            return Decode(saved);

        var legacy = (Retrieve(AccessName), Retrieve(RefreshName));
        if (legacy.Item1 != null || legacy.Item2 != null)
        {
            Save(legacy.Item1, legacy.Item2);
            return legacy;
        }
        return legacy;
    }

    public static void Save(string? access, string? refresh)
    {
        if (string.IsNullOrEmpty(access) && string.IsNullOrEmpty(refresh))
        {
            Clear();
            return;
        }

        ProtectedSecretFile.Save(Folder, Resource, "session", Encode(access, refresh));
        RemoveLegacyCredentials();
    }

    public static void Clear()
    {
        ProtectedSecretFile.Delete(Folder, Resource, "session");
        RemoveLegacyCredentials();
    }

    private static string Encode(string? access, string? refresh) =>
        (string.IsNullOrEmpty(access) ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(access))) + "." +
        (string.IsNullOrEmpty(refresh) ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(refresh)));

    private static (string? Access, string? Refresh) Decode(string value)
    {
        var parts = value.Split('.', 2);
        if (parts.Length != 2) return (null, null);
        try
        {
            return (parts[0] == "-" ? null : Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])),
                parts[1] == "-" ? null : Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])));
        }
        catch (FormatException) { return (null, null); }
    }

    private static string? Retrieve(string name)
    {
        try
        {
            var credential = new PasswordVault().Retrieve(Resource, name);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch { return null; }
    }

    private static void RemoveLegacyCredentials()
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var credential in vault.FindAllByResource(Resource)) vault.Remove(credential);
        }
        catch { }
    }
}
