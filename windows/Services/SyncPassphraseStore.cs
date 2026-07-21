using Windows.Security.Credentials;

namespace Hatch.Services;

// Separate vault resource from SyncTokenStore: token refresh clears Hatch.Sync wholesale,
// which must never take the passphrase with it. Salt is generated once when the passphrase
// is set and reused for every push so the PBKDF2 key derivation is a one-time cost.
internal static class SyncPassphraseStore
{
    private const string Resource       = "Hatch.SyncCrypto";
    private const string PassphraseName = "sync_passphrase";
    private const string SaltName       = "sync_salt";

    public static (string? Passphrase, byte[]? Salt) Load()
    {
        var passphrase = Retrieve(PassphraseName);
        var saltB64 = Retrieve(SaltName);
        if (passphrase == null || saltB64 == null) return (null, null);
        try { return (passphrase, Convert.FromBase64String(saltB64)); }
        catch { return (null, null); }
    }

    public static void Save(string passphrase)
    {
        Clear();
        var vault = new PasswordVault();
        vault.Add(new PasswordCredential(Resource, PassphraseName, passphrase));
        vault.Add(new PasswordCredential(Resource, SaltName,
            Convert.ToBase64String(SyncCrypto.CreateSalt())));
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
