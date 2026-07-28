package dev.hatch.android

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import dev.hatch.sync.SyncKey
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

// ADR-0005: persists the derived key, never the passphrase. Keystore keys are
// non-exportable and SyncCrypto needs raw bytes, hence the wrap pattern. Not
// EncryptedSharedPreferences — Jetpack Security Crypto is deprecated.
class SyncKeyStore(context: Context) {

    // Lazy: keeps the XML read off onCreate, where this is constructed.
    private val prefs by lazy { context.getSharedPreferences(PREFS, Context.MODE_PRIVATE) }

    fun load(): SyncKey? {
        val wrapped = prefs.getString(WRAPPED_KEY, null) ?: return null
        val salt = prefs.getString(SALT, null) ?: return null
        return runCatching {
            val blob = Base64.decode(wrapped, Base64.NO_WRAP)
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(
                Cipher.DECRYPT_MODE,
                wrappingKey(),
                GCMParameterSpec(TAG_BITS, blob.copyOfRange(0, IV_BYTES)),
            )
            SyncKey(
                key = cipher.doFinal(blob.copyOfRange(IV_BYTES, blob.size)),
                salt = Base64.decode(salt, Base64.NO_WRAP),
            )
        }.getOrNull()
    }

    fun save(syncKey: SyncKey) {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, wrappingKey())
        val blob = cipher.iv + cipher.doFinal(syncKey.key)
        prefs.edit()
            .putString(WRAPPED_KEY, Base64.encodeToString(blob, Base64.NO_WRAP))
            .putString(SALT, Base64.encodeToString(syncKey.salt, Base64.NO_WRAP))
            .apply()
    }

    // Called on sign-out and whenever the stored key turns out not to decrypt the row.
    fun clear() {
        prefs.edit().clear().apply()
        runCatching { keyStore().deleteEntry(ALIAS) }
    }

    private fun wrappingKey(): SecretKey {
        (keyStore().getEntry(ALIAS, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }

        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE).apply {
            init(
                KeyGenParameterSpec.Builder(
                    ALIAS,
                    KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
                )
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setKeySize(256)
                    // No setUserAuthenticationRequired: a biometric gate would strand the
                    // key in background sync.
                    .build()
            )
        }.generateKey()
    }

    private fun keyStore() = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }

    private companion object {
        const val ANDROID_KEYSTORE = "AndroidKeyStore"
        const val ALIAS = "hatch_sync_kek"
        const val PREFS = "hatch_sync_key"
        const val WRAPPED_KEY = "wrapped_key"
        const val SALT = "salt"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val IV_BYTES = 12
        const val TAG_BITS = 128
    }
}
