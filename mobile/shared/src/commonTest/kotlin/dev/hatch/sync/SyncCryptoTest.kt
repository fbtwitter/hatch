package dev.hatch.sync

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

// Mirrors Hatch.Tests.Unit/SyncCryptoTests.cs. The test vector is the executable half of
// docs/sync-protocol.md §3 — if this passes, the Kotlin envelope framing matches .NET's.
class SyncCryptoTest {

    private val passphrase = "hatch-protocol-fixture"
    private val salt = byteArrayOf(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16)
    private val nonce = "efghijklmnop".encodeToByteArray() // bytes 0x65..0x70
    private val plaintext = """{"Tasks":[],"Lists":[]}"""

    private val expectedEnvelope =
        "HATCHE2E.v1.AQIDBAUGBwgJCgsMDQ4PEA==.ZWZnaGlqa2xtbm9w." +
            "8MBXWg99OnKuOx9fdeAkXP6/l9/nYdI=.zG17LsgSGvCuUByNjY0Eog=="

    @Test
    fun reproduces_the_protocol_test_vector() {
        assertEquals(expectedEnvelope, SyncCrypto.encrypt(plaintext, passphrase, salt, nonce))
    }

    @Test
    fun decrypts_the_protocol_test_vector() {
        assertEquals(plaintext, SyncCrypto.tryDecrypt(expectedEnvelope, passphrase))
    }

    // Deliberately a different nonce from the test vector: the JDK's GCM implementation
    // throws "Cannot reuse iv for GCM encryption" if the same key+IV pair is used to encrypt
    // twice in one process. .NET's AesGcm has no such guard, so this is a real behavioural
    // difference between the two implementations, not a test artefact. §3 requires a fresh
    // random nonce per envelope regardless.
    @Test
    fun round_trips() {
        val freshNonce = "ROUNDTRIP-01".encodeToByteArray()
        val envelope = SyncCrypto.encrypt(plaintext, passphrase, salt, freshNonce)
        assertEquals(plaintext, SyncCrypto.tryDecrypt(envelope, passphrase))
    }

    @Test
    fun wrong_passphrase_returns_null() {
        assertNull(SyncCrypto.tryDecrypt(expectedEnvelope, "not-the-passphrase"))
    }

    @Test
    fun tampered_ciphertext_returns_null() {
        val parts = expectedEnvelope.removePrefix("HATCHE2E.v1.").split(".")
        val flipped = parts[2].let { if (it[0] == 'A') "B" + it.substring(1) else "A" + it.substring(1) }
        val tampered = "HATCHE2E.v1." + listOf(parts[0], parts[1], flipped, parts[3]).joinToString(".")
        assertNull(SyncCrypto.tryDecrypt(tampered, passphrase))
    }

    @Test
    fun malformed_envelope_returns_null() {
        assertNull(SyncCrypto.tryDecrypt("HATCHE2E.v1.only.three.fields", passphrase))
    }

    @Test
    fun detects_envelopes_and_ignores_legacy_plaintext() {
        assertTrue(SyncCrypto.isEncrypted(expectedEnvelope))
        assertFalse(SyncCrypto.isEncrypted(plaintext))
        assertNull(SyncCrypto.tryDecrypt(plaintext, passphrase))
    }
}
