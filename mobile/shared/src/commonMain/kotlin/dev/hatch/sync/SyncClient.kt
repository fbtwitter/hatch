package dev.hatch.sync

import io.github.jan.supabase.SupabaseClient
import io.github.jan.supabase.auth.Auth
import io.github.jan.supabase.auth.FlowType
import io.github.jan.supabase.auth.auth
import io.github.jan.supabase.auth.providers.Github
import io.github.jan.supabase.auth.providers.builtin.Email
import io.github.jan.supabase.auth.status.SessionStatus
import io.github.jan.supabase.auth.user.UserMfaFactor
import io.github.jan.supabase.createSupabaseClient
import io.github.jan.supabase.postgrest.Postgrest
import io.github.jan.supabase.postgrest.from
import io.github.jan.supabase.postgrest.postgrest
import io.github.jan.supabase.postgrest.rpc
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import kotlin.time.Clock

@Serializable
data class UserDataRow(
    @SerialName("user_id") val userId: String,
    @SerialName("tasks_json") val tasksJson: String,
    @SerialName("updated_at") val updatedAt: String,
)

// Mirrors ReadServerTasks in windows/Services/SyncService.cs. Unreadable is distinct from
// Empty by contract (§2): a row that will not decrypt is never an empty account.
sealed interface SignUpResult {
    data object SignedIn : SignUpResult
    data object ConfirmEmail : SignUpResult
    data class Failed(val message: String) : SignUpResult
}

// Persisted by the platform (ADR-0005), so the passphrase is entered once per device.
class SyncKey(val key: ByteArray, val salt: ByteArray)

sealed interface PushResult {
    data class Success(val merged: TasksFile, val updatedAt: String) : PushResult
    data object NeedsPassphrase : PushResult
    data object NeedsMfa : PushResult
    data object Unreadable : PushResult
    data class Failed(val message: String) : PushResult
}

sealed interface PullResult {
    data class Success(val data: TasksFile, val updatedAt: String) : PullResult
    data object Empty : PullResult
    data object NeedsPassphrase : PullResult
    data object NeedsMfa : PullResult
    data object Unreadable : PullResult
    data class Failed(val message: String) : PullResult
}

class SyncClient(supabaseUrl: String, supabaseKey: String) {

    private val client = createSupabaseClient(
        supabaseUrl = normalizeUrl(supabaseUrl),
        supabaseKey = supabaseKey,
    ) {
        install(Auth) {
            // Matches the redirect registered for the Windows client (§1).
            scheme = "hatch"
            host = "auth-callback"
            // PKCE: a custom scheme is not exclusive to one app, so an implicit redirect
            // would hand a hijacker live tokens.
            flowType = FlowType.PKCE
        }
        install(Postgrest)
    }

    // internal: supabase-kt stays an implementation detail, so consumers never need its
    // types on their classpath. Deeplink glue lives in androidMain.
    internal val supabase: SupabaseClient get() = client

    val signedInEmail: String? get() = client.auth.currentUserOrNull()?.email

    val signedIn: Flow<Boolean>
        get() = client.auth.sessionStatus.map { it is SessionStatus.Authenticated }

    // Session restore is async; a background worker would otherwise ask before it lands.
    suspend fun awaitSession(): Boolean {
        client.auth.awaitInitialization()
        return client.auth.currentSessionOrNull() != null
    }

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

    // Mirrors IsMfaChallengePending in windows/Services/SyncService.cs.
    //
    // runCatching is load-bearing: supabase-kt throws IllegalStateException when the JWT
    // carries no `aal` claim, which would escape pull() ahead of its try block.
    //
    // Fails open, like the Windows client — aal2 in RLS is what makes the server authority.
    val mfaChallengePending: Boolean
        get() = runCatching {
            client.auth.currentSessionOrNull() != null &&
                client.auth.mfa.status.let { it.enabled && !it.active }
        }.getOrDefault(false)

    // Turns two-factor OFF rather than granting a one-time pass: nothing outside GoTrue can
    // mint an aal2 token, so removing the factor is the only way out of an aal1 session.
    suspend fun redeemRecoveryCode(code: String): String? = try {
        val accepted = client.postgrest.rpc(
            "redeem_mfa_recovery_code",
            buildJsonObject { put("code", code.trim()) },
        ).decodeAs<Boolean>()
        if (accepted) null
        else "That recovery code was not recognised. Check for typos, or try another from your list."
    } catch (t: Throwable) {
        t.message ?: "Could not use that recovery code"
    }

    // Null on success, message on failure. Promotes this session to aal2.
    suspend fun submitMfaChallenge(code: String): String? = try {
        val factor = client.auth.mfa.retrieveFactorsForCurrentUser()
            .firstOrNull(UserMfaFactor::isVerified)
        if (factor == null) "No authenticator is set up for this account."
        else {
            client.auth.mfa.createChallengeAndVerify(factor.id, code.trim())
            null
        }
    } catch (t: Throwable) {
        t.message ?: "That code was not accepted."
    }

    // RLS scopes the table to auth.uid(), so this returns at most one row.
    suspend fun pull(syncKey: SyncKey?): PullResult {
        // Before the request: an unchallenged session must not read the row at all.
        if (mfaChallengePending) return PullResult.NeedsMfa
        return try {
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
    }

    // Read → merge (§5) → upload, mirroring windows/Services/SyncService.cs. The row is
    // whole-state, so a blind push would replace whatever this device has not seen.
    suspend fun pushMerged(local: TasksFile, syncKey: SyncKey?): PushResult {
        // §2: clients MUST refuse to push when no key is available.
        if (syncKey == null) return PushResult.NeedsPassphrase
        if (mfaChallengePending) return PushResult.NeedsMfa
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

    // So a new device derives against the account's salt rather than minting a second (§3).
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

        // Secrets.cs ends in /rest/v1/, which supabase-kt appends itself.
        internal fun normalizeUrl(url: String): String =
            url.trim().removeSuffix("/").removeSuffix("/rest/v1").removeSuffix("/")
    }
}
