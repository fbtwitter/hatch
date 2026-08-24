package dev.hatch.android

import android.app.Application
import android.content.Intent
import android.net.Uri
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import dev.hatch.sync.PullResult
import dev.hatch.sync.PushResult
import dev.hatch.sync.Recurrence
import dev.hatch.sync.RecurrenceHelper
import dev.hatch.sync.SignUpResult
import dev.hatch.sync.SyncClient
import dev.hatch.sync.SyncCrypto
import dev.hatch.sync.SyncKey
import dev.hatch.sync.SyncMerge
import dev.hatch.sync.TaskExportFormatter
import dev.hatch.sync.handleDeeplink
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
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
import kotlinx.datetime.toKotlinLocalDate
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.UUID

// The three formats windows/Helpers/TaskExportFormatter.cs writes, and the MIME type the
// system document picker needs for each.
enum class ExportFormat(val label: String, val mimeType: String, val extension: String) {
    Json("JSON", "application/json", "json"),
    Csv("CSV", "text/csv", "csv"),
    Markdown("Markdown", "text/markdown", "md"),
}

sealed interface SyncState {
    // Shown inline on the credentials form, so a failure never discards what was typed.
    data class Off(val error: String? = null, val notice: String? = null) : SyncState
    data object NotConfigured : SyncState
    data object Working : SyncState
    data object NeedsPassphrase : SyncState
    data object WrongPassphrase : SyncState
    // Every sync path is refused until the challenge is met (docs/mfa-spec.md). `redeeming`
    // is a flag rather than its own state so backing out cannot strand the user.
    data class NeedsMfaCode(val error: String? = null, val redeeming: Boolean = false) : SyncState
    data class On(
        val email: String?,
        val serverUpdatedAt: String,
        // Pushing is refused without one (§2), even when the row is readable plaintext.
        val hasPassphrase: Boolean,
    ) : SyncState
    data class Failed(val message: String) : SyncState
}

data class AppState(
    val tasks: List<TodoItem> = emptyList(),
    val lists: List<TaskList> = emptyList(),
    val sync: SyncState = SyncState.Off(),
    // Gates the empty state, and every mutation: persisting before the load lands would
    // write a near-empty file over the real one.
    val loaded: Boolean = false,
    val themeMode: ThemeMode = ThemeMode.System,
    val useDynamicColor: Boolean = false,
)

const val NAV_MY_DAY = "myday"
const val NAV_IMPORTANT = "important"
const val NAV_PLANNED = "planned"
const val NAV_ALL_TASKS = "alltasks"

// First entry matches MainViewModel.AddList; the rest exist only here, because the WinUI
// app has no recolour code at all despite project-overview.md claiming 8 hues.
val ListPalette = listOf(
    "#0078D4", "#107C10", "#C239B3", "#D13438",
    "#CA5010", "#8764B8", "#00838C", "#7A7574",
)

// State lives here, on the platform — the shared module stays pure (ADR-0006).
class CompanionViewModel(app: Application) : AndroidViewModel(app) {

    private val appContext = app.applicationContext

    private val store = LocalTaskStore(app)

    private val configured =
        BuildConfig.SUPABASE_URL.isNotEmpty() && BuildConfig.SUPABASE_KEY.isNotEmpty()

    private val client by lazy { SyncClient(BuildConfig.SUPABASE_URL, BuildConfig.SUPABASE_KEY) }

    private val keyStore = SyncKeyStore(app)

    private val prefs = AppPrefs(app)

    // Keystore-backed (ADR-0005): entered once per device, derived once. Loaded in init
    // rather than here — the unwrap is too slow for onCreate.
    private var syncKey: SyncKey? = null

    // Seeded synchronously so the first frame paints in the chosen theme. The disk read this
    // implies is deliberate and is declared inside AppPrefs itself, so nothing is needed here.
    private val _state = MutableStateFlow(
        AppState(themeMode = prefs.themeMode, useDynamicColor = prefs.useDynamicColor)
    )
    val state: StateFlow<AppState> = _state.asStateFlow()

    // Which folder the Lists tab is showing. Held here rather than as a route argument so a
    // jump from a Summary tile does not push a second `lists` entry onto the back stack and
    // fight the bottom bar's saveState/restoreState, and rather than in a savedStateHandle so
    // there is no collector that can be missing at emit time (b00761a). Outside AppState on
    // purpose: switching folders must not wake every collector of the task list. Not
    // persisted — a cold start opens on All Tasks.
    private val _selectedFolder = MutableStateFlow(NAV_ALL_TASKS)
    val selectedFolder: StateFlow<String> = _selectedFolder.asStateFlow()

    fun selectFolder(nav: String) {
        _selectedFolder.value = nav
    }

    // An event, not state: in AppState, reopening Sync would replay a stale "Pulled — 17".
    private val _pullCompleted = MutableSharedFlow<Int>(extraBufferCapacity = 1)
    val pullCompleted: SharedFlow<Int> = _pullCompleted.asSharedFlow()

    private val _pushCompleted = MutableSharedFlow<Int>(extraBufferCapacity = 1)
    val pushCompleted: SharedFlow<Int> = _pushCompleted.asSharedFlow()

    private var pushJob: Job? = null

    // Tombstones — outside AppState, but rejoin the file on every save and push (§5).
    private var deletedTasks: List<TodoItem> = emptyList()
    private var deletedLists: List<TaskList> = emptyList()

    private fun wholeState(
        tasks: List<TodoItem> = _state.value.tasks,
        lists: List<TaskList> = _state.value.lists,
    ) = TasksFile(tasks + deletedTasks, lists + deletedLists)

    private fun applyMerged(merged: TasksFile) {
        // A task deleted on another device arrives here as a freshly-pulled tombstone —
        // same reminder-leak reason as deleteTask/deleteList: persist() only reschedules
        // over the live list it's given, so anything that drops out of that list between
        // one merge and the next needs its own alarm cancelled explicitly, or a task
        // deleted elsewhere keeps reminding on this phone.
        val previouslyLive = _state.value.tasks.map { it.id }.toSet()
        deletedTasks = merged.tasks.filter { it.isDeleted }
        deletedLists = merged.lists.filter { it.isDeleted }
        val liveTasks = merged.tasks.filterNot { it.isDeleted }
        persist(
            tasks = liveTasks,
            lists = merged.lists.filterNot { it.isDeleted },
        )
        val newlyGone = previouslyLive - liveTasks.map { it.id }.toSet()
        newlyGone.forEach { cancelReminder(appContext, it) }
    }

    init {
        // Off the main thread: a file read plus JSON parse and a Keystore unwrap cost
        // seconds on a mid-range device, and hundreds of skipped frames before first paint.
        //
        // The two loads are independent — keyStore.load() reads its own SharedPreferences file
        // and its own Keystore-backed key, using nothing store.load() produces — but they used
        // to run one after the other on the exact path that gates the splash screen
        // (setKeepOnScreenCondition { !loaded }). The Keystore unwrap is the slower of the two,
        // so a sequential await made every cold start pay for it on top of the JSON read
        // instead of alongside it. async/awaitAll runs them concurrently on Dispatchers.IO;
        // measured with StartupBenchmark, not asserted.
        viewModelScope.launch {
            val localDeferred = async(Dispatchers.IO) { store.load() }
            val syncKeyDeferred = async(Dispatchers.IO) { keyStore.load() }
            val local = localDeferred.await()
            syncKey = syncKeyDeferred.await()

            deletedTasks = local.tasks.filter { it.isDeleted }
            deletedLists = local.lists.filter { it.isDeleted }

            // "IsInMyDay: cleared client-side each new day" (sync-protocol.md §4). Mirrors
            // TaskStorageService.LoadAsync on Windows: silent and local-only — myDayDate is
            // left untouched and updatedAt is not stamped, so this day-boundary reset can
            // never shadow a genuinely newer edit from another device on the next merge.
            // Without it a task starred into My Day on the phone stayed there forever; this
            // ViewModel had no equivalent of Windows's reset at all.
            val today = today()
            var myDayReset = false
            val liveTasks = local.tasks.filterNot { it.isDeleted }.map { t ->
                val myDayDate = t.myDayDate
                if (t.isInMyDay && myDayDate != null && myDayDate < today) {
                    myDayReset = true
                    t.copy(isInMyDay = false)
                } else t
            }

            _state.value = _state.value.copy(
                tasks = liveTasks,
                lists = local.lists.filterNot { it.isDeleted },
                sync = if (configured) SyncState.Off() else SyncState.NotConfigured,
                loaded = true,
            )
            // Persisted so a second load the same day is a no-op, but off the push path —
            // this reset is not a sync-worthy edit.
            if (myDayReset) withContext(Dispatchers.IO) { store.save(wholeState()) }
            if (configured) observeSession()
        }
    }

    // --- Local, works with no account and no network ------------------------------------

    // Returns the new id so the list can scroll to it; null when nothing was added.
    //
    // `nav` is passed in rather than read from state: the screen showing the field is the
    // only thing that knows which list the field belongs to, and while the two were tracked
    // separately a task could be created against a list that was no longer on screen.
    fun addTask(title: String, nav: String): String? {
        if (!_state.value.loaded) return null
        val trimmed = title.trim()
        if (trimmed.isEmpty()) return null
        val now = isoNow()
        // Picks up the property the open smart list is defined by, or it vanishes on create.
        val task = TodoItem(
            id = UUID.randomUUID().toString(),
            title = trimmed,
            listId = listIdForNav(nav),
            isInMyDay = nav == NAV_MY_DAY,
            myDayDate = if (nav == NAV_MY_DAY) today() else null,
            isStarred = nav == NAV_IMPORTANT,
            dueDate = if (nav == NAV_PLANNED) dueDateIsoOf(LocalDate.now()) else null,
            createdAt = now,
            updatedAt = now,
        )
        persist(_state.value.tasks.toMutableList().apply { add(0, task) })
        return task.id
    }

    fun toggleComplete(task: TodoItem) {
        if (!_state.value.loaded) return
        val now = isoNow()
        val completing = !task.isCompleted

        val updated = _state.value.tasks.map {
            if (it.id != task.id) it
            else it.copy(
                isCompleted = completing,
                completedAt = if (completing) now else null,
                updatedAt = now,
            )
        }

        // Mirrors MainViewModel.TrySpawnNextRecurrence.
        val spawned = if (completing) spawnNextRecurrence(task, now) else null
        persist(if (spawned == null) updated else listOf(spawned) + updated)
    }

    private fun spawnNextRecurrence(task: TodoItem, now: String): TodoItem? {
        if (task.recurrence == Recurrence.NONE) return null
        val due = task.dueDate ?: return null
        // Unparseable: skip rather than invent an occurrence on a date nobody chose.
        val nextDue = RecurrenceHelper.advanceDueDate(due, task.recurrence) ?: return null

        return task.copy(
            id = UUID.randomUUID().toString(),
            isCompleted = false,
            completedAt = null,
            isInMyDay = false,
            myDayDate = null,
            dueDate = nextDue,
            createdAt = now,
            updatedAt = now,
        )
    }

    // Stamped here, not per call site: an edit without a fresh UpdatedAt loses the merge.
    fun saveTask(updated: TodoItem) {
        if (!_state.value.loaded) return
        val stamped = updated.copy(updatedAt = isoNow())
        persist(_state.value.tasks.map { if (it.id == stamped.id) stamped else it })
    }

    fun toggleStar(task: TodoItem) = saveTask(task.copy(isStarred = !task.isStarred))

    // Mirrors TodoItem.SetMyDay: membership and date are one rule, never set independently.
    fun setMyDay(task: TodoItem, on: Boolean) = saveTask(
        task.copy(isInMyDay = on, myDayDate = if (on) today() else null)
    )

    fun deleteTask(task: TodoItem) {
        if (!_state.value.loaded) return
        // Fields kept, not blanked, so restoreTask puts back exactly what was there.
        deletedTasks = deletedTasks + task.copy(isDeleted = true, updatedAt = isoNow())
        persist(_state.value.tasks.filterNot { it.id == task.id })
        // persist() reschedules only over the list just passed to it, which no longer
        // contains this task — its own alarm needs cancelling explicitly, or it fires
        // a reminder for a task that's gone. restoreTask needs no undo of this: the
        // restored task re-enters the live list, so the next persist() reschedules it.
        cancelReminder(appContext, task.id)
    }

    // The fresh UpdatedAt is what beats a tombstone another device may already hold.
    fun restoreTask(task: TodoItem) {
        if (!_state.value.loaded) return
        deletedTasks = deletedTasks.filterNot { it.id == task.id }
        val restored = task.copy(isDeleted = false, updatedAt = isoNow())
        persist(_state.value.tasks.toMutableList().apply { add(0, restored) })
    }

    // --- Lists ---------------------------------------------------------------------------

    fun createList(name: String) {
        if (!_state.value.loaded) return
        val trimmed = name.trim()
        if (trimmed.isEmpty()) return
        val now = isoNow()
        val list = TaskList(
            id = UUID.randomUUID().toString(),
            name = trimmed,
            accentColor = ListPalette.first(),
            sortOrder = _state.value.lists.size,
            updatedAt = now,
        )
        persist(_state.value.tasks, _state.value.lists + list)
    }

    fun renameList(list: TaskList, name: String) {
        val trimmed = name.trim()
        if (trimmed.isEmpty()) return
        saveList(list.copy(name = trimmed))
    }

    fun togglePinList(list: TaskList) = saveList(list.copy(isPinned = !list.isPinned))

    private fun saveList(updated: TaskList) {
        if (!_state.value.loaded) return
        val stamped = updated.copy(updatedAt = isoNow())
        persist(_state.value.tasks, _state.value.lists.map { if (it.id == stamped.id) stamped else it })
    }

    // Tombstones the list and every task in it, mirroring MainViewModel.DeleteList.
    fun deleteList(list: TaskList) {
        if (!_state.value.loaded) return
        val now = isoNow()
        val orphaned = _state.value.tasks.filter { it.listId == list.id }

        deletedTasks = deletedTasks + orphaned.map { it.copy(isDeleted = true, updatedAt = now) }
        deletedLists = deletedLists + list.copy(isDeleted = true, updatedAt = now)

        // No "if this list was open, fall back to All Tasks" here any more: which list is
        // open is the navigation back stack's business now, and the list route pops itself
        // when its list stops existing — which also covers a delete arriving from a pull,
        // something this fallback never did.
        persist(
            tasks = _state.value.tasks.filterNot { it.listId == list.id },
            lists = _state.value.lists.filterNot { it.id == list.id },
        )
        // Same reason as deleteTask: every orphaned task just left the list persist()
        // reschedules over, so each one needs its own alarm cancelled explicitly.
        orphaned.forEach { cancelReminder(appContext, it.id) }
    }

    private val saveMutex = Mutex()

    private var reminderJob: Job? = null

    // The write itself is deliberately NOT debounced, unlike Windows, whose coding standards
    // mandate a 500ms idle delay. That rule does not port: a debounce window is a data-loss
    // window, and Android kills backgrounded processes routinely where Windows almost never
    // does. The write is off the main thread and mutex-guarded, so its only cost is I/O.
    private fun persist(tasks: List<TodoItem>, lists: List<TaskList> = _state.value.lists) {
        // State first so the row redraws this frame; the write follows off-thread.
        _state.value = _state.value.copy(tasks = tasks, lists = lists)

        viewModelScope.launch(Dispatchers.IO) {
            // Each writer re-reads state under the lock, so a late write cannot restore a
            // stale snapshot over a newer one.
            saveMutex.withLock {
                store.save(wholeState())
            }
        }
        scheduleReminderRebuild()
        schedulePush()
    }

    // Rescheduling walks every live task and issues one WorkManager cancel-or-enqueue each,
    // and every one of those is a write into WorkManager's own database — so ticking a single
    // checkbox in a 200-task list cost ~200 database operations, almost all of them rewriting
    // an alarm to exactly what it already was. Coalesced rather than made incremental: the
    // whole-snapshot rebuild is what makes drift impossible (see Reminders.kt), and tracking
    // per-edit deltas would trade that guarantee away to save work that is already off the
    // main thread. Reminders fire at 09:00, so arriving a second late means nothing.
    private fun scheduleReminderRebuild() {
        reminderJob?.cancel()
        reminderJob = viewModelScope.launch(Dispatchers.IO) {
            delay(REMINDER_REBUILD_DEBOUNCE_MS)
            rescheduleReminders(appContext, _state.value.tasks)
        }
    }

    // --- Export -------------------------------------------------------------------------

    private val _exportFinished = MutableSharedFlow<String>(extraBufferCapacity = 1)
    val exportFinished: SharedFlow<String> = _exportFinished.asSharedFlow()

    // The uri comes from the system document picker, so this writes only where the user
    // pointed it, and nothing leaves the device unless they choose a cloud folder themselves.
    fun exportTo(uri: Uri, format: ExportFormat) {
        viewModelScope.launch {
            // Live tasks only — deliberately not wholeState(). Tombstones belong to the sync
            // protocol; an export listing tasks the user deleted would be a bug wearing a
            // feature's clothes. (Windows exports the raw file and does include them.)
            val data = TasksFile(_state.value.tasks, _state.value.lists)
            val today = LocalDate.now().toKotlinLocalDate()

            val result = withContext(Dispatchers.IO) {
                runCatching {
                    val text = when (format) {
                        ExportFormat.Json -> TaskExportFormatter.toJson(data)
                        ExportFormat.Csv -> TaskExportFormatter.toCsv(data)
                        ExportFormat.Markdown -> TaskExportFormatter.toMarkdown(data, today)
                    }
                    appContext.contentResolver.openOutputStream(uri)?.use { out ->
                        out.write(text.encodeToByteArray())
                    } ?: error("That location could not be opened for writing.")
                }
            }

            _exportFinished.emit(
                result.fold(
                    onSuccess = {
                        val count = data.tasks.size
                        "Exported $count task${if (count == 1) "" else "s"}"
                    },
                    onFailure = { "Export failed — ${it.message ?: "unknown error"}" },
                )
            )
        }
    }

    // --- Sync, entirely opt-in ----------------------------------------------------------

    fun signIn(email: String, password: String) {
        if (!configured) return
        setSync(SyncState.Working)
        viewModelScope.launch {
            val error = client.signIn(email.trim(), password)
            // Back to the form, not a dead end: the text is still there to correct.
            if (error != null) setSync(SyncState.Off(error = humanize(error))) else pull()
        }
    }

    fun signInWithGithub() {
        if (!configured) return
        setSync(SyncState.Working)
        viewModelScope.launch {
            val error = client.signInWithGithub()
            // Success arrives via handleDeeplink/observeSession, not from here.
            if (error != null) setSync(SyncState.Off(error = humanize(error)))
        }
    }

    // Called from the activity for the hatch://auth-callback redirect.
    fun handleDeeplink(intent: Intent) {
        if (!configured) return
        // Off the main thread for the same reason observeSession spells out below: touching
        // `client` constructs it, and constructing it builds Ktor, OkHttp and supabase-kt,
        // which read from disk. This is called straight from MainActivity.onCreate, so doing
        // it inline put that whole stack on the critical path of every cold start — StrictMode
        // reported it as a main-thread disk read on launch. Nothing here needs to be ordered
        // against the rest of onCreate: the callback that matters is the session change
        // observeSession is already collecting.
        viewModelScope.launch(Dispatchers.IO) { client.handleDeeplink(intent) }
    }

    private fun observeSession() {
        viewModelScope.launch {
            // Touching `client` constructs it, so force the lazy off the main thread.
            val signedInFlow = withContext(Dispatchers.IO) { client.signedIn }
            signedInFlow.collect { signedIn ->
                // Only for sign-ins outside the email/password path, which drives its own.
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

    // Supabase does not distinguish "no such user" from "wrong password", so the message
    // has to cover both without implying which.
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
            // Back to the prompt: codes rotate, so a rejection usually means "try the next".
            if (error != null) setSync(SyncState.NeedsMfaCode(error = humanize(error)))
            else pull()
        }
    }

    fun setThemeMode(mode: ThemeMode) {
        prefs.themeMode = mode
        _state.value = _state.value.copy(themeMode = mode)
    }

    fun setDynamicColor(on: Boolean) {
        prefs.useDynamicColor = on
        _state.value = _state.value.copy(useDynamicColor = on)
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
                // The factor is gone, so the existing aal1 session is now sufficient.
                pull()
            }
        }
    }

    fun submitPassphrase(value: String) {
        setSync(SyncState.Working)
        viewModelScope.launch {
            // The account's existing salt, so every device converges on one (§3).
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

    // Mirrors SchedulePush in windows/Services/SyncService.cs.
    private fun schedulePush() {
        if (_state.value.sync !is SyncState.On || syncKey == null) return
        pushJob?.cancel()
        pushJob = viewModelScope.launch {
            delay(PUSH_DEBOUNCE_MS)
            pushNow(manual = false)
        }
    }

    private suspend fun pushNow(manual: Boolean) {
        when (val result = client.pushMerged(wholeState(), syncKey)) {
            is PushResult.Success -> {
                applyMerged(result.merged)
                setSync(SyncState.On(client.signedInEmail, result.updatedAt, true))
                if (manual) _pushCompleted.emit(result.merged.tasks.count { !it.isDeleted })
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

    private var autoPullJob: Job? = null

    // Matches StartAutoSync's PeriodicTimer on Windows, scoped to the foreground.
    fun startAutoPull() {
        autoPullJob?.cancel()
        autoPullJob = viewModelScope.launch {
            while (true) {
                pullQuietly()
                delay(AUTO_PULL_INTERVAL_MS)
            }
        }
    }

    fun stopAutoPull() {
        autoPullJob?.cancel()
        autoPullJob = null
    }

    // Silent by design: an automatic pull must never take the screen with a spinner or a
    // passphrase prompt. Anything needing an answer waits for the manual path.
    private suspend fun pullQuietly() {
        if (_state.value.sync !is SyncState.On) return
        when (val result = client.pull(syncKey)) {
            is PullResult.Success -> {
                applyServer(result.data)
                setSync(SyncState.On(client.signedInEmail, result.updatedAt, syncKey != null))
            }
            else -> Unit
        }
    }


    fun signOut() {
        // Matches SyncPassphraseStore.Clear() on Windows.
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
                // Never "no tasks", and local data is untouched (§2 unreadable-row rule).
                // The stored key cannot open this row, so discard it.
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
    // wholeState(), not the live half: an unpushed local delete must take part in the merge.
    private fun applyServer(server: TasksFile) {
        applyMerged(SyncMerge.merge(wholeState(), server))
    }

    private fun setSync(sync: SyncState) {
        // Background sync follows the opt-in: never enqueued until sync actually works.
        if (sync is SyncState.On) schedulePeriodicSync(appContext)
        else if (sync is SyncState.Off) cancelPeriodicSync(appContext)

        _state.value = _state.value.copy(sync = sync)
    }

    private companion object {
        const val PUSH_DEBOUNCE_MS = 3_000L
        // Far shorter than the push debounce: this one only coalesces a burst of edits, and
        // an alarm that is a second late is indistinguishable from one that is not.
        const val REMINDER_REBUILD_DEBOUNCE_MS = 1_000L
        const val AUTO_PULL_INTERVAL_MS = 5 * 60 * 1_000L
        fun isoNow(): String =
            OffsetDateTime.now(ZoneOffset.UTC).format(DateTimeFormatter.ISO_OFFSET_DATE_TIME)

        // Local day deliberately: "added to My Day today" means today where the user is.
        fun today(): String = LocalDate.now().toString()
    }
}
