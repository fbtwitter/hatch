using System.Security.Cryptography;
using System.Text;
using Windows.Security.Credentials;

namespace Hatch.Services;

// Keep the passphrase and salt together in one DPAPI-protected file. Credential Locker
// remains a migration source, but is not required for new saves because it may be
// uninitialized or unavailable for the current Windows user profile.
internal static class SyncPassphraseStore
{
    private const string Resource = "Hatch.SyncCrypto";
    private const string PassphraseName = "sync_passphrase";
    private const string SaltName = "sync_salt";
    private static readonly string Folder = Path.Combine(Hatch.Helpers.AppDataPath.Folder, "sync-credentials");

    public static (string? Passphrase, byte[]? Salt) Load()
    {
        var saved = ProtectedSecretFile.Load(Folder, Resource, "passphrase-and-salt");
        if (saved != null) return Decode(saved);

        var passphrase = Retrieve(PassphraseName);
        var salt = Retrieve(SaltName);
        if (passphrase == null || salt == null) return (null, null);

        var encodedSalt = Convert.FromBase64String(salt);
        ProtectedSecretFile.Save(Folder, Resource, "passphrase-and-salt", Encode(passphrase, encodedSalt));
        RemoveLegacyCredentials();
        return (passphrase, encodedSalt);
    }

    public static void Save(string passphrase)
    {
        var salt = SyncCrypto.CreateSalt();
        ProtectedSecretFile.Save(Folder, Resource, "passphrase-and-salt", Encode(passphrase, salt));
        RemoveLegacyCredentials();
    }

    public static void Clear()
    {
        ProtectedSecretFile.Delete(Folder, Resource, "passphrase-and-salt");
        RemoveLegacyCredentials();
    }

    private static string Encode(string passphrase, byte[] salt) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(passphrase)) + "." + Convert.ToBase64String(salt);

    private static (string? Passphrase, byte[]? Salt) Decode(string value)
    {
        var parts = value.Split('.', 2);
        if (parts.Length != 2) return (null, null);
        try { return (Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])), Convert.FromBase64String(parts[1])); }
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
