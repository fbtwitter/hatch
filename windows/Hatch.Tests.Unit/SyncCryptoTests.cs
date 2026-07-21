using Hatch.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public class SyncCryptoTests
{
    // Shared salt so the PBKDF2 derivation (600k iterations) runs once per passphrase
    // for the whole test class.
    private static readonly byte[] Salt = SyncCrypto.CreateSalt();
    private const string Passphrase = "correct horse battery staple";

    [TestMethod]
    public void RoundTrip_ReturnsOriginalPlaintext()
    {
        var plaintext = """{"Tasks":[{"Title":"secret task","Notes":"private"}]}""";

        var envelope = SyncCrypto.Encrypt(plaintext, Passphrase, Salt);
        var decrypted = SyncCrypto.TryDecrypt(envelope, Passphrase);

        Assert.AreEqual(plaintext, decrypted);
    }

    [TestMethod]
    public void Envelope_ContainsNoPlaintext()
    {
        var envelope = SyncCrypto.Encrypt("very secret task title", Passphrase, Salt);

        Assert.IsTrue(SyncCrypto.IsEncrypted(envelope));
        Assert.IsFalse(envelope.Contains("secret"));
        Assert.IsFalse(envelope.Contains("task"));
    }

    [TestMethod]
    public void WrongPassphrase_ReturnsNull()
    {
        var envelope = SyncCrypto.Encrypt("payload", Passphrase, Salt);

        Assert.IsNull(SyncCrypto.TryDecrypt(envelope, "not the passphrase"));
    }

    [TestMethod]
    public void TamperedCiphertext_ReturnsNull()
    {
        var envelope = SyncCrypto.Encrypt("payload to protect", Passphrase, Salt);

        // Envelope: HATCHE2E.v1.<salt>.<nonce>.<cipher>.<tag> — index 4 is the ciphertext.
        var parts = envelope.Split('.');
        var cipher = Convert.FromBase64String(parts[4]);
        cipher[0] ^= 0xFF;
        parts[4] = Convert.ToBase64String(cipher);
        var tampered = string.Join('.', parts);

        Assert.IsNull(SyncCrypto.TryDecrypt(tampered, Passphrase));
    }

    [TestMethod]
    public void SameMessageTwice_ProducesDifferentEnvelopes()
    {
        var a = SyncCrypto.Encrypt("same content", Passphrase, Salt);
        var b = SyncCrypto.Encrypt("same content", Passphrase, Salt);

        Assert.AreNotEqual(a, b);
    }

    // Test vector from docs/sync-protocol.md §3 — pins KDF (PBKDF2-SHA256, 600k, 32-byte
    // key), envelope layout, and cipher. The Kotlin core must decrypt this same string.
    [TestMethod]
    public void GoldenEnvelope_FromProtocolSpec_Decrypts()
    {
        const string envelope =
            "HATCHE2E.v1.AQIDBAUGBwgJCgsMDQ4PEA==.ZWZnaGlqa2xtbm9w." +
            "8MBXWg99OnKuOx9fdeAkXP6/l9/nYdI=.zG17LsgSGvCuUByNjY0Eog==";

        var plain = SyncCrypto.TryDecrypt(envelope, "hatch-protocol-fixture");

        Assert.AreEqual("""{"Tasks":[],"Lists":[]}""", plain);
    }

    [TestMethod]
    public void PlaintextJson_IsNotDetectedAsEncrypted_AndDecryptReturnsNull()
    {
        var legacyRow = """{"Tasks":[],"Lists":[]}""";

        Assert.IsFalse(SyncCrypto.IsEncrypted(legacyRow));
        Assert.IsNull(SyncCrypto.TryDecrypt(legacyRow, Passphrase));
    }
}
