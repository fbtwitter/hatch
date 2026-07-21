package dev.hatch.sync

import dev.whyoleg.cryptography.BinarySize.Companion.bits
import dev.whyoleg.cryptography.CryptographyProvider
import dev.whyoleg.cryptography.DelicateCryptographyApi
import dev.whyoleg.cryptography.algorithms.AES
import dev.whyoleg.cryptography.algorithms.PBKDF2
import dev.whyoleg.cryptography.algorithms.SHA256
import kotlin.io.encoding.Base64
import kotlin.io.encoding.ExperimentalEncodingApi

// Kotlin transcription of Services/SyncCrypto.cs. The C# side is the reference
// implementation; this must produce byte-identical envelopes. See docs/sync-protocol.md §3.
@OptIn(ExperimentalEncodingApi::class, DelicateCryptographyApi::class)
object SyncCrypto {
    private const val PREFIX = "HATCHE2E.v1."
    private const val TAG_SIZE = 16
    private const val ITERATIONS = 600_000

    private val keyCache = mutableMapOf<Pair<String, String>, ByteArray>()

    fun isEncrypted(payload: String): Boolean = payload.startsWith(PREFIX)

    // Nonce is a parameter rather than generated here so the protocol test vector is
    // reproducible; production callers must pass freshly random bytes every time.
    fun encrypt(plaintext: String, passphrase: String, salt: ByteArray, nonce: ByteArray): String {
        // .NET's AesGcm.Encrypt writes ciphertext and tag to separate buffers, which is why
        // the envelope carries them as separate fields. cryptography-kotlin returns them
        // concatenated as [ciphertext | tag], so the trailing 16 bytes are split back out.
        val combined = cipher(passphrase, salt)
            .encryptWithIvBlocking(nonce, plaintext.encodeToByteArray())
        val tagStart = combined.size - TAG_SIZE

        return PREFIX +
            Base64.encode(salt) + "." +
            Base64.encode(nonce) + "." +
            Base64.encode(combined.copyOfRange(0, tagStart)) + "." +
            Base64.encode(combined.copyOfRange(tagStart, combined.size))
    }

    // Null on wrong passphrase, tampered ciphertext, or malformed envelope.
    fun tryDecrypt(envelope: String, passphrase: String): String? {
        if (!isEncrypted(envelope)) return null
        return try {
            val parts = envelope.removePrefix(PREFIX).split(".")
            if (parts.size != 4) return null

            val salt = Base64.decode(parts[0])
            val nonce = Base64.decode(parts[1])
            val ciphertext = Base64.decode(parts[2])
            val tag = Base64.decode(parts[3])

            cipher(passphrase, salt)
                .decryptWithIvBlocking(nonce, ciphertext + tag)
                .decodeToString()
        } catch (_: Throwable) {
            null
        }
    }

    private fun cipher(passphrase: String, salt: ByteArray) =
        CryptographyProvider.Default
            .get(AES.GCM)
            .keyDecoder()
            .decodeFromByteArrayBlocking(AES.Key.Format.RAW, derivedKey(passphrase, salt))
            .cipher()

    private fun derivedKey(passphrase: String, salt: ByteArray): ByteArray =
        keyCache.getOrPut(passphrase to Base64.encode(salt)) {
            CryptographyProvider.Default
                .get(PBKDF2)
                .secretDerivation(
                    digest = SHA256,
                    iterations = ITERATIONS,
                    outputSize = 256.bits,
                    salt = salt,
                )
                .deriveSecretToByteArrayBlocking(passphrase.encodeToByteArray())
        }
}
