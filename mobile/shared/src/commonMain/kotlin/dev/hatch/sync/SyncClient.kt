package dev.hatch.sync

import io.github.jan.supabase.SupabaseClient
import io.github.jan.supabase.auth.Auth
import io.github.jan.supabase.auth.FlowType
import io.github.jan.supabase.auth.auth
import io.github.jan.supabase.auth.providers.Github
import io.github.jan.supabase.auth.providers.builtin.Email
import io.github.jan.supabase.auth.status.SessionStatus
import io.github.jan.supabase.createSupabaseClient
import io.github.jan.supabase.postgrest.Postgrest
import io.github.jan.supabase.postgrest.from
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlin.time.Clock

@Serializable
data class UserDataRow(
    @SerialName("user_id") val userId: String,
    @SerialName("tasks_json") val tasksJson: String,
    @SerialName("updated_at") val updatedAt: String,
)

// Mirrors the outcomes of ReadServerTasks in windows/Services/SyncService.cs. Unreadable is
// deliberately distinct from Empty: docs/sync-protocol.md §2 requires a row that will not
// decrypt to be treated as "data I cannot read", never as an empty account.
sealed interface SignUpResult {
    data object SignedIn : SignUpResult
    data object ConfirmEmail : SignUpResult
    data class Failed(val message: String) : SignUpResult
}

// The derived key plus the salt it was derived against. Persisted by the platform
// (ADR-0005) so the passphrase is entered once per device rather than once per launch.
class SyncKey(val key: ByteArray, val salt: ByteArray)

sealed interface PushResult {
    data class Success(val merged: TasksFile, val updatedAt: String) : PushResult
    data object NeedsPassphrase : PushResult
    data object Unreadable : PushResult
    data class Failed(val message: String) : PushResult
}

sealed interface PullResult {
    data class Success(val data: TasksFile, val updatedAt: String) : PullResult
    data object Empty : PullResult
    data object NeedsPassphrase : PullResult
    data object Unreadable : PullResult
    data class Failed(val message: String) : PullResult
}

class SyncClient(supabaseUrl: String, supabaseKey: String) {

    private val client = createSupabaseClient(
        supabaseUrl = normalizeUrl(supabaseUrl),
        supabaseKey = supabaseKey,
    ) {
        install(Auth) {
            // Matches the redirect registered for the Windows client (docs/sync-protocol.md §1).
            scheme = "hatch"
            host = "auth-callback"
            // PKCE, not implicit: a custom scheme is not exclusive to one app, so an
            // implicit redirect would hand a hijacker live access and refresh tokens.
            flowType = FlowType.PKCE
        }
        install(Postgrest)
    }

    // internal, not public: supabase-kt stays an implementation detail of this module.
    // The Android deeplink glue lives in androidMain (SyncClientAndroid.kt) so consumers
    // never need supabase types on their classpath.
    internal val supabase: SupabaseClient get() = client

    val signedInEmail: String? get() = client.auth.currentUserOrNull()?.email

    val signedIn: Flow<Boolean>
        get() = client.auth.sessionStatus.map { it is SessionStatus.Authenticated }

    // Opens a Custom Tab; the result arrives as a deeplink, not as a return value.
    suspend fun signInWithGithub(): String? = try {
        client.auth.signInWith(Github)
        null
    } catch (t: Throwable) {
        t.message ?: "GitHub sign-in failed"
    }

    suspend fun signIn(email: String, password: String): String? = try {
        client.auth.signInWith(Email) {
            this.email = email
            this.password = password
        }
        null
    } catch (t: Throwable) {
        t.message ?: "Sign-in failed"
    }

    suspend fun signUp(email: String, password: String): SignUpResult = try {
        client.auth.signUpWith(Email) {
            this.email = email
            this.password = password
        }
        // With email confirmation enabled, sign-up succeeds but returns no session —
        // the account is not usable until the link is clicked.
        if (client.auth.currentSessionOrNull() != null) SignUpResult.SignedIn
        else SignUpResult.ConfirmEmail
    } catch (t: Throwable) {
        SignUpResult.Failed(t.message ?: "Sign-up failed")
    }

    suspend fun signOut() {
        runCatching { client.auth.signOut() }
    }

    // RLS scopes the table to auth.uid(), so this returns at most one row.
    suspend fun pull(syncKey: SyncKey?): PullResult = try {
        val row = client.from(TABLE).select().decodeSingleOrNull<UserDataRow>()
        when {
            row == null || row.tasksJson.isEmpty() -> PullResult.Empty
            SyncCrypto.isEncrypted(row.tasksJson) -> decrypt(row, syncKey)
            // Predates E2E encryption: parse as-is (§2 legacy plaintext).
            else -> parse(row.tasksJson, row.updatedAt)
        }
    } catch (t: Throwable) {
        PullResult.Failed(t.message ?: "Pull failed")
    }

    // Merge-before-push, mirroring windows/Services/SyncService.cs. The row is whole-state,
    // not a delta, so pushing local state blind would replace anything this device has not
    // seen. Always: read → merge (§5) → upload the union.
    suspend fun pushMerged(local: TasksFile, syncKey: SyncKey?): PushResult {
        // §2: clients MUST refuse to push when no key is available.
        if (syncKey == null) return PushResult.NeedsPassphrase
        return try {
            val userId = client.auth.currentUserOrNull()?.id
                ?: return PushResult.Failed("Not signed in")

            val row = client.from(TABLE).select().decodeSingleOrNull<UserDataRow>()
            val server = when {
                row == null || row.tasksJson.isEmpty() -> TasksFile()
                SyncCrypto.isEncrypted(row.tasksJson) ->
                    SyncCrypto.tryDecryptWithKey(row.tasksJson, syncKey.key)
                        ?.let { runCatching { SyncWire.deserialize(it) }.getOrNull() }
                        ?: return PushResult.Unreadable
                else -> runCatching { SyncWire.deserialize(row.tasksJson) }.getOrNull()
                    ?: return PushResult.Unreadable
            }

            val merged = SyncMerge.merge(local, server)
            val envelope = SyncCrypto.encryptWithKey(
                SyncWire.serialize(merged), syncKey.key, syncKey.salt,
            )
            val updatedAt = Clock.System.now().toString()

            client.from(TABLE).upsert(
                UserDataRow(userId = userId, tasksJson = envelope, updatedAt = updatedAt)
            )
            PushResult.Success(merged, updatedAt)
        } catch (t: Throwable) {
            PushResult.Failed(t.message ?: "Push failed")
        }
    }

    // The salt already on the server, so a new device derives its key against the account's
    // salt rather than minting a second one (§3, per-account salt).
    suspend fun serverSalt(): ByteArray? = runCatching {
        client.from(TABLE).select().decodeSingleOrNull<UserDataRow>()
            ?.tasksJson?.let { SyncCrypto.saltOf(it) }
    }.getOrNull()

    private fun decrypt(row: UserDataRow, syncKey: SyncKey?): PullResult {
        if (syncKey == null) return PullResult.NeedsPassphrase
        val plain = SyncCrypto.tryDecryptWithKey(row.tasksJson, syncKey.key)
            ?: return PullResult.Unreadable
        return parse(plain, row.updatedAt)
    }

    private fun parse(json: String, updatedAt: String): PullResult = try {
        PullResult.Success(SyncWire.deserialize(json), updatedAt)
    } catch (_: Throwable) {
        PullResult.Unreadable
    }

    companion object {
        private const val TABLE = "user_data"

        // The Windows Secrets.cs value ends in /rest/v1/, which supabase-kt appends itself.
        internal fun normalizeUrl(url: String): String =
            url.trim().removeSuffix("/").removeSuffix("/rest/v1").removeSuffix("/")
    }
}
