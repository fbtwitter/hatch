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

// ADR-0005: persists the 32-byte derived key, never the passphrase. The passphrase is
// user-chosen and probably reused elsewhere; the derived key unlocks exactly one account's
// task payload and nothing else, so holding it is a strict reduction in blast radius.
//
// AndroidKeyStore keys are non-exportable and SyncCrypto needs raw bytes, so this uses the
// standard wrap pattern: a hardware-backed AES key inside the Keystore encrypts our derived
// key, and only the wrapped blob is written to disk.
//
// Not EncryptedSharedPreferences: Jetpack Security Crypto is deprecated and unmaintained.
class SyncKeyStore(context: Context) {

    // Lazy so the backing XML is read on whichever thread first calls load()/save() — both
    // of which are already off the main thread — rather than during construction, which
    // happens in the ViewModel's field initializers during onCreate.
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
                    // setUserAuthenticationRequired(true) would add a biometric gate here.
                    // Left off for now: it needs a BiometricPrompt flow, and locking the key
                    // behind one before background sync exists would strand the key.
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
