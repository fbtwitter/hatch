using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Hatch.Services;

// End-to-end encryption for the Supabase payload: the server only ever stores this
// envelope, so nobody with database access (including the project admin) can read tasks.
// Compiled into the WinUI-free test project — BCL only.
public static class SyncCrypto
{
    private const string Prefix = "HATCHE2E.v1.";
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Pbkdf2Iterations = 600_000;

    // PBKDF2 at 600k iterations costs ~hundreds of ms; the salt is fixed per passphrase
    // (see SyncPassphraseStore) precisely so this cache makes derivation a one-time cost.
    // Nonce uniqueness per message is what GCM requires — salt reuse is fine for PBKDF2.
    private static readonly ConcurrentDictionary<(string Passphrase, string Salt), byte[]> KeyCache = new();

    public static byte[] CreateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static bool IsEncrypted(string payload)
        => payload.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Encrypt(string plaintext, string passphrase, byte[] salt)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(GetKey(passphrase, salt), TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return Prefix +
               Convert.ToBase64String(salt) + "." +
               Convert.ToBase64String(nonce) + "." +
               Convert.ToBase64String(cipher) + "." +
               Convert.ToBase64String(tag);
    }

    // Null on wrong passphrase, tampered ciphertext, or malformed envelope.
    public static string? TryDecrypt(string envelope, string passphrase)
    {
        if (!IsEncrypted(envelope)) return null;
        try
        {
            var parts = envelope[Prefix.Length..].Split('.');
            if (parts.Length != 4) return null;

            var salt = Convert.FromBase64String(parts[0]);
            var nonce = Convert.FromBase64String(parts[1]);
            var cipher = Convert.FromBase64String(parts[2]);
            var tag = Convert.FromBase64String(parts[3]);

            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(GetKey(passphrase, salt), TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] GetKey(string passphrase, byte[] salt)
        => KeyCache.GetOrAdd((passphrase, Convert.ToBase64String(salt)),
            k => Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Pbkdf2Iterations,
                                           HashAlgorithmName.SHA256, KeySize));
}
