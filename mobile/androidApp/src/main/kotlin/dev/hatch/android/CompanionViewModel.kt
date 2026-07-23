package dev.hatch.android

import android.app.Application
import android.content.Intent
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import dev.hatch.sync.PullResult
import dev.hatch.sync.PushResult
import dev.hatch.sync.SignUpResult
import dev.hatch.sync.SyncClient
import dev.hatch.sync.SyncCrypto
import dev.hatch.sync.SyncKey
import dev.hatch.sync.SyncMerge
import dev.hatch.sync.handleDeeplink
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import dev.hatch.sync.TaskList
import dev.hatch.sync.TasksFile
import dev.hatch.sync.TodoItem
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.UUID

sealed interface SyncState {
    // The credentials form keeps its own text; `error` and `notice` are shown inline on it
    // so a failure never discards what was typed.
    data class Off(val error: String? = null, val notice: String? = null) : SyncState
    data object NotConfigured : SyncState
    data object Working : SyncState
    data object NeedsPassphrase : SyncState
    data object WrongPassphrase : SyncState
    // Signed in, but the account has a verified authenticator this session has not met.
    // Every sync path is refused until it is (docs/mfa-spec.md); local tasks are unaffected.
    // `redeeming` swaps the prompt to the recovery-code form rather than being a separate
    // state, so backing out cannot strand the user away from the challenge.
    data class NeedsMfaCode(val error: String? = null, val redeeming: Boolean = false) : SyncState
    data class On(
        val email: String?,
        val serverUpdatedAt: String,
        // Pushing is refused without one (docs/sync-protocol.md §2), so the UI has to be
        // able to ask for it even when the current row is readable legacy plaintext.
        val hasPassphrase: Boolean,
    ) : SyncState
    data class Failed(val message: String) : SyncState
}

data class AppState(
    val tasks: List<TodoItem> = emptyList(),
    val lists: List<TaskList> = emptyList(),
    val sync: SyncState = SyncState.Off(),
    // False until the on-disk list has been read. Gates the empty state (which would
    // otherwise flash "Nothing yet" before the tasks arrive) and, more importantly, gates
    // every mutation: persisting before the load lands would write a near-empty file over
    // the real one.
    val loaded: Boolean = false,
    val themeMode: ThemeMode = ThemeMode.System,
)

// State lives here, on the platform — the shared module stays pure (ADR-0006).
class CompanionViewModel(app: Application) : AndroidViewModel(app) {

    private val store = LocalTaskStore(app)

    private val configured =
        BuildConfig.SUPABASE_URL.isNotEmpty() && BuildConfig.SUPABASE_KEY.isNotEmpty()

    private val client by lazy { SyncClient(BuildConfig.SUPABASE_URL, BuildConfig.SUPABASE_KEY) }

    private val keyStore = SyncKeyStore(app)

    private val prefs = AppPrefs(app)

    // Keystore-backed (ADR-0005), so the passphrase is entered once per device rather than
    // once per launch, and the expensive PBKDF2 derivation happens exactly once.
    // Populated by the init block off the main thread — the unwrap is a hardware-backed
    // AES-GCM operation and does not belong in a constructor that runs during onCreate.
    private var syncKey: SyncKey? = null

    // Theme is seeded synchronously so the very first frame paints in the chosen theme.
    private val _state = MutableStateFlow(AppState(themeMode = prefs.themeMode))
    val state: StateFlow<AppState> = _state.asStateFlow()

    // One-shot, not state: "a manual pull just finished" is an event. Kept out of AppState
    // so reopening the Sync screen cannot replay a stale "Pulled — 17 tasks".
    private val _pullCompleted = MutableSharedFlow<Int>(extraBufferCapacity = 1)
    val pullCompleted: SharedFlow<Int> = _pullCompleted.asSharedFlow()

    private val _pushCompleted = MutableSharedFlow<Int>(extraBufferCapacity = 1)
    val pushCompleted: SharedFlow<Int> = _pushCompleted.asSharedFlow()

    private var pushJob: Job? = null

    init {
        // Both of these used to run in the constructor, i.e. on the main thread during
        // onCreate: a file read plus JSON parse, and a hardware-backed Keystore unwrap.
        // Together they cost seconds on a mid-range device and logcat reported hundreds of
        // skipped frames before the first paint.
        viewModelScope.launch {
            val local = withContext(Dispatchers.IO) { store.load() }
            syncKey = withContext(Dispatchers.IO) { keyStore.load() }

            _state.value = _state.value.copy(
                tasks = local.tasks,
                lists = local.lists,
                sync = if (configured) SyncState.Off() else SyncState.NotConfigured,
                loaded = true,
            )
            if (configured) observeSession()
        }
    }

    // --- Local, works with no account and no network ------------------------------------

    fun addTask(title: String) {
        // Guarded, not merely disabled in the UI: persisting before the load lands would
        // write a one-task file over the real one.
        if (!_state.value.loaded) return
        val trimmed = title.trim()
        if (trimmed.isEmpty()) return
        val now = isoNow()
        val task = TodoItem(
            id = UUID.randomUUID().toString(),
            title = trimmed,
            listId = DEFAULT_LIST_ID,
            createdAt = now,
            updatedAt = now,
        )
        // Newest-first, matching the Windows app's insert-at-zero ordering.
        persist(_state.value.tasks.toMutableList().apply { add(0, task) })
    }

    fun toggleComplete(task: TodoItem) {
        if (!_state.value.loaded) return
        val now = isoNow()
        persist(
            _state.value.tasks.map {
                if (it.id != task.id) it
                else it.copy(
                    isCompleted = !it.isCompleted,
                    completedAt = if (!it.isCompleted) now else null,
                    updatedAt = now,
                )
            }
        )
    }

    private val saveMutex = Mutex()

    private fun persist(tasks: List<TodoItem>, lists: List<TaskList> = _state.value.lists) {
        // State first so the row redraws on this frame; the write follows off-thread.
        // Ticking a checkbox previously blocked the main thread on a serialize + file write.
        _state.value = _state.value.copy(tasks = tasks, lists = lists)

        viewModelScope.launch(Dispatchers.IO) {
            // Serialized, and each writer re-reads current state under the lock: a write
            // that arrives late then re-writes the newest snapshot rather than restoring a
            // stale one, so ordering between launches cannot lose an edit.
            saveMutex.withLock {
                val snapshot = _state.value
                store.save(TasksFile(snapshot.tasks, snapshot.lists))
            }
        }
        schedulePush()
    }

    // --- Sync, entirely opt-in ----------------------------------------------------------

    fun signIn(email: String, password: String) {
        if (!configured) return
        setSync(SyncState.Working)
        viewModelScope.launch {
            val error = client.signIn(email.trim(), password)
            // Back to the form, not a dead-end screen: the text is still there to correct.
            if (error != null) setSync(SyncState.Off(error = humanize(error))) else pull()
        }
    }

    fun signInWithGithub() {
        if (!configured) return
        setSync(SyncState.Working)
        viewModelScope.launch {
            val error = client.signInWithGithub()
            // Success is not signalled here — the browser redirects back into the activity
            // and the session arrives through handleDeeplink/observeSession.
            if (error != null) setSync(SyncState.Off(error = humanize(error)))
        }
    }

    // Called from the activity for the hatch://auth-callback redirect.
    fun handleDeeplink(intent: Intent) {
        if (!configured) return
        client.handleDeeplink(intent)
    }

    private fun observeSession() {
        viewModelScope.launch {
            // Touching `client` here is what first constructs it — createSupabaseClient plus
            // an OkHttp engine — so the lazy is forced off the main thread rather than
            // during the first composition.
            val signedInFlow = withContext(Dispatchers.IO) { client.signedIn }
            signedInFlow.collect { signedIn ->
                // Only react to a sign-in that happened outside the email/password path;
                // that path drives its own state transitions.
                if (signedIn && _state.value.sync !is SyncState.On) pull()
            }
        }
    }

    fun signUp(email: String, password: String) {
        if (!configured) return
        setSync(SyncState.Working)
        viewModelScope.launch {
            when (val result = client.signUp(email.trim(), password)) {
                SignUpResult.SignedIn -> pull()
                SignUpResult.ConfirmEmail -> setSync(
                    SyncState.Off(notice = "Account created. Check $email for a confirmation link, then sign in.")
                )
                is SignUpResult.Failed -> setSync(SyncState.Off(error = humanize(result.message)))
            }
        }
    }

    // Supabase deliberately does not distinguish "no such user" from "wrong password", so
    // the message has to cover both without implying which.
    private fun humanize(raw: String): String {
        val text = raw.lowercase()
        return when {
            "invalid login credentials" in text || "invalid_credentials" in text ->
                "That email and password don't match an account. Check the email — your sync account may differ from the one on this phone."
            "email not confirmed" in text ->
                "This account still needs confirming. Open the link in your email, then sign in."
            "user already registered" in text || "already been registered" in text ->
                "That email already has an account. Sign in instead."
            "totp" in text || "invalid_code" in text || "mfa" in text ->
                "That code wasn't accepted. Codes expire every 30 seconds — wait for the next one and try again."
            "password should be" in text || "weak" in text ->
                "That password is too short. Use at least 6 characters."
            "network" in text || "unable to resolve host" in text || "timeout" in text ->
                "Can't reach the server. Check your connection — your tasks on this phone are unaffected."
            else -> raw
        }
    }

    fun submitMfaCode(code: String) {
        setSync(SyncState.Working)
        viewModelScope.launch {
            val error = client.submitMfaChallenge(code)
            // Back to the prompt with the reason, not a dead end — the code rotates every
            // 30 seconds, so a rejection usually just means "try the next one".
            if (error != null) setSync(SyncState.NeedsMfaCode(error = humanize(error)))
            else pull()
        }
    }

    fun setThemeMode(mode: ThemeMode) {
        prefs.themeMode = mode
        _state.value = _state.value.copy(themeMode = mode)
    }

    fun showRecoveryCodeEntry(show: Boolean) {
        val current = _state.value.sync
        if (current is SyncState.NeedsMfaCode) setSync(current.copy(error = null, redeeming = show))
    }

    // Turns two-factor OFF rather than granting a one-time pass — see SyncClient.
    fun redeemRecoveryCode(code: String) {
        setSync(SyncState.Working)
        viewModelScope.launch {
            val error = client.redeemRecoveryCode(code)
            if (error != null) {
                setSync(SyncState.NeedsMfaCode(error = error, redeeming = true))
            } else {
                // The factor is gone, so the aal1 session this app already holds is now
                // sufficient — no re-authentication needed.
                pull()
            }
        }
    }

    fun submitPassphrase(value: String) {
        setSync(SyncState.Working)
        viewModelScope.launch {
            // Derive against the account's existing salt when there is one, so every device
            // converges on a single salt (§3) and derives its key exactly once.
            val salt = client.serverSalt() ?: SyncCrypto.createSalt()
            // 600k PBKDF2 iterations — seconds of CPU. Never on the main thread.
            val derived = withContext(Dispatchers.Default) { SyncCrypto.deriveKey(value, salt) }

            val key = SyncKey(derived, salt)
            syncKey = key
            keyStore.save(key)
            pull()
        }
    }

    fun push() {
        if (_state.value.sync is SyncState.Working) return
        setSync(SyncState.Working)
        viewModelScope.launch { pushNow(manual = true) }
    }

    // Mirrors SchedulePush in windows/Services/SyncService.cs: local edits go up on a short
    // debounce rather than one request per keystroke-level change.
    private fun schedulePush() {
        if (_state.value.sync !is SyncState.On || syncKey == null) return
        pushJob?.cancel()
        pushJob = viewModelScope.launch {
            delay(PUSH_DEBOUNCE_MS)
            pushNow(manual = false)
        }
    }

    private suspend fun pushNow(manual: Boolean) {
        val local = TasksFile(_state.value.tasks, _state.value.lists)
        when (val result = client.pushMerged(local, syncKey)) {
            is PushResult.Success -> {
                persist(result.merged.tasks, result.merged.lists)
                setSync(SyncState.On(client.signedInEmail, result.updatedAt, true))
                if (manual) _pushCompleted.emit(result.merged.tasks.size)
            }
            PushResult.NeedsPassphrase ->
                setSync(SyncState.On(client.signedInEmail, "", hasPassphrase = false))
            PushResult.NeedsMfa -> setSync(SyncState.NeedsMfaCode())
            PushResult.Unreadable -> {
                syncKey = null
                keyStore.clear()
                setSync(SyncState.WrongPassphrase)
            }
            is PushResult.Failed -> setSync(SyncState.Failed(humanize(result.message)))
        }
    }

    fun refresh() {
        if (_state.value.sync is SyncState.Working) return
        setSync(SyncState.Working)
        viewModelScope.launch { pull(manual = true) }
    }

    fun signOut() {
        // Cleared on explicit sign-out, matching SyncPassphraseStore.Clear() on Windows.
        syncKey = null
        keyStore.clear()
        viewModelScope.launch {
            client.signOut()
            setSync(SyncState.Off())
        }
    }

    private suspend fun pull(manual: Boolean = false) {
        when (val result = client.pull(syncKey)) {
            is PullResult.Success -> {
                applyServer(result.data)
                setSync(SyncState.On(client.signedInEmail, result.updatedAt, syncKey != null))
                if (manual) _pullCompleted.emit(_state.value.tasks.size)
            }
            PullResult.Empty -> {
                setSync(SyncState.On(client.signedInEmail, "", syncKey != null))
                if (manual) _pullCompleted.emit(_state.value.tasks.size)
            }
            PullResult.NeedsPassphrase -> setSync(SyncState.NeedsPassphrase)
            PullResult.NeedsMfa -> setSync(SyncState.NeedsMfaCode())
            PullResult.Unreadable -> {
                // Never treated as "no tasks", and local data is never touched
                // (docs/sync-protocol.md §2 unreadable-row rule). The stored key cannot
                // open this row, so discard it rather than failing on every launch.
                syncKey = null
                keyStore.clear()
                setSync(SyncState.WrongPassphrase)
            }
            is PullResult.Failed -> setSync(SyncState.Failed(result.message))
        }
    }

    // Proper §5 merge now that SyncMerge is ported: last-write-wins by UpdatedAt, local
    // wins ties, nothing dropped. This replaces the interim "server always wins" rule, so
    // a task completed on this phone no longer reverts on the next pull.
    private fun applyServer(server: TasksFile) {
        val merged = SyncMerge.merge(TasksFile(_state.value.tasks, _state.value.lists), server)
        persist(tasks = merged.tasks, lists = merged.lists)
    }

    private fun setSync(sync: SyncState) {
        _state.value = _state.value.copy(sync = sync)
    }

    private companion object {
        const val DEFAULT_LIST_ID = "00000000-0000-0000-0000-000000000000"
        const val PUSH_DEBOUNCE_MS = 3_000L
        fun isoNow(): String =
            OffsetDateTime.now(ZoneOffset.UTC).format(DateTimeFormatter.ISO_OFFSET_DATE_TIME)
    }
}
