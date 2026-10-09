// Deliberate host-boundary doubles for the linked production SyncService.
// Authentication, Credential Locker and localization are NOT covered by these tests.
namespace Hatch.Services
{
    public sealed class SyncAccountService
    {
        internal Supabase.Client? Client { get; set; }
        public bool IsSignedIn { get; set; } = true;
        public bool IsMfaChallengePending { get; set; }
        public bool HasPassphrase => SyncPassphraseStore.Load().Passphrase != null;
        public event Action? SigningOut;
        public void SignOut() { IsSignedIn = false; SigningOut?.Invoke(); }
    }

    internal static class SyncPassphraseStore
    {
        private static (string? Passphrase, byte[]? Salt) _value;
        public static (string? Passphrase, byte[]? Salt) Load() => _value;
        public static void Save(string passphrase) => _value = (passphrase, SyncCrypto.CreateSalt());
        public static void Clear() => _value = (null, null);
    }
}

namespace Hatch.Helpers
{
    internal static class Strings
    {
        public const string Sync_Error_NotReady = "NotReady";
        public const string Sync_Error_NotSignedIn = "NotSignedIn";
        public const string Sync_Error_MfaRequired = "MfaRequired";
        public const string Sync_Error_OldPassphraseWrong = "OldPassphraseWrong";
        public const string Sync_Error_NoUserId = "NoUserId";
        public const string Sync_Error_NoPassphrase = "NoPassphrase";
        public const string Sync_Error_WrongPassphrase = "WrongPassphrase";
    }
}
