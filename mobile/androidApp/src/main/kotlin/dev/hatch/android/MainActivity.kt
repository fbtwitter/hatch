package dev.hatch.android

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem

// Mirrors the Windows minimum (SettingsViewModel.SetSyncPassphraseAsync).
private const val MIN_PASSPHRASE = 8

// TOTP is always 6 digits (docs/mfa-spec.md).
private const val MFA_CODE_LENGTH = 6

class MainActivity : ComponentActivity() {

    // Same instance the composables get from viewModel(), since both resolve against this
    // activity's ViewModelStore.
    private val vm: CompanionViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        vm.handleDeeplink(intent)
        setContent {
            MaterialTheme(
                colorScheme = if (isSystemInDarkTheme()) darkColorScheme() else lightColorScheme(),
            ) {
                Surface(Modifier.fillMaxSize()) { HatchApp() }
            }
        }
    }

    // The OAuth redirect arrives here because the activity is singleTop.
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        vm.handleDeeplink(intent)
    }
}

@Composable
private fun HatchApp(vm: CompanionViewModel = viewModel()) {
    val state by vm.state.collectAsState()
    // The app opens on the task list and never asks who you are. Sync is reached only from
    // the overflow menu — see the HARD STOP quoted in context/current-feature.md.
    var showSync by rememberSaveable { mutableStateOf(false) }
    val snackbar = remember { SnackbarHostState() }

    // "Pull now" is a fetch, not a setting: on success, get out of the way and show the
    // tasks it fetched. Failures and passphrase prompts deliberately keep you on the Sync
    // screen, because those need an answer.
    LaunchedEffect(Unit) {
        vm.pullCompleted.collect { count ->
            showSync = false
            snackbar.showSnackbar("Pulled — $count task${if (count == 1) "" else "s"}")
        }
    }

    // Push deliberately does NOT navigate away: nothing on the task list changes as a
    // result, and you are usually mid-flow on the Sync screen when you press it.
    LaunchedEffect(Unit) {
        vm.pushCompleted.collect { count ->
            snackbar.showSnackbar("Pushed — $count task${if (count == 1) "" else "s"} encrypted and sent")
        }
    }

    if (showSync) {
        SyncScreen(
            sync = state.sync,
            onSignIn = vm::signIn,
            onSignUp = vm::signUp,
            onGithub = vm::signInWithGithub,
            onPassphrase = vm::submitPassphrase,
            onMfaCode = vm::submitMfaCode,
            onRefresh = vm::refresh,
            onPush = vm::push,
            onSignOut = vm::signOut,
            onBack = { showSync = false },
            snackbar = snackbar,
        )
    } else {
        TaskScreen(
            tasks = state.tasks,
            lists = state.lists,
            snackbar = snackbar,
            onAdd = vm::addTask,
            onToggle = vm::toggleComplete,
            onOpenSync = { showSync = true },
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TaskScreen(
    tasks: List<TodoItem>,
    lists: List<TaskList>,
    snackbar: SnackbarHostState,
    onAdd: (String) -> Unit,
    onToggle: (TodoItem) -> Unit,
    onOpenSync: () -> Unit,
) {
    var draft by remember { mutableStateOf("") }
    val open = tasks.filter { !it.isCompleted }
    val done = tasks.filter { it.isCompleted }
    val listNames = lists.associate { it.id to it.name }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        topBar = {
            TopAppBar(
                title = { Text("Hatch") },
                actions = { TextButton(onClick = onOpenSync) { Text("Sync") } },
            )
        },
        bottomBar = {
            Surface(tonalElevation = 3.dp) {
                Row(
                    Modifier.fillMaxWidth().padding(12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    OutlinedTextField(
                        value = draft,
                        onValueChange = { draft = it },
                        placeholder = { Text("Add a task") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                        modifier = Modifier.weight(1f),
                    )
                    Spacer(Modifier.width(8.dp))
                    Button(
                        onClick = { onAdd(draft); draft = "" },
                        enabled = draft.isNotBlank(),
                    ) { Text("Add") }
                }
            }
        },
    ) { padding ->
        if (tasks.isEmpty()) {
            Column(
                Modifier.fillMaxSize().padding(padding).padding(32.dp),
                verticalArrangement = Arrangement.Center,
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Text("Nothing yet", style = MaterialTheme.typography.titleMedium)
                Spacer(Modifier.height(6.dp))
                Text(
                    "Add a task below. No account needed — Sync is optional.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            return@Scaffold
        }

        LazyColumn(Modifier.fillMaxSize().padding(padding)) {
            items(open, key = { it.id }) { TaskRow(it, listNames, onToggle) }
            if (done.isNotEmpty()) {
                item {
                    Text(
                        "Completed",
                        style = MaterialTheme.typography.labelLarge,
                        modifier = Modifier.padding(start = 16.dp, top = 20.dp, bottom = 4.dp),
                    )
                }
                items(done, key = { it.id }) { TaskRow(it, listNames, onToggle) }
            }
        }
    }
}

@Composable
private fun TaskRow(task: TodoItem, listNames: Map<String, String>, onToggle: (TodoItem) -> Unit) {
    Row(
        Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Checkbox(checked = task.isCompleted, onCheckedChange = { onToggle(task) })
        Column(Modifier.weight(1f).padding(vertical = 6.dp)) {
            Text(
                task.title,
                style = MaterialTheme.typography.bodyLarge,
                textDecoration = if (task.isCompleted) TextDecoration.LineThrough else null,
                color = if (task.isCompleted) MaterialTheme.colorScheme.onSurfaceVariant
                        else MaterialTheme.colorScheme.onSurface,
            )
            val meta = listOfNotNull(
                listNames[task.listId],
                task.dueDate?.take(10),
                task.tags.takeIf { it.isNotEmpty() }?.joinToString(" ") { "#$it" },
            )
            if (meta.isNotEmpty()) {
                Text(
                    meta.joinToString("  ·  "),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
    HorizontalDivider()
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SyncScreen(
    sync: SyncState,
    onSignIn: (String, String) -> Unit,
    onSignUp: (String, String) -> Unit,
    onGithub: () -> Unit,
    onPassphrase: (String) -> Unit,
    onMfaCode: (String) -> Unit,
    onRefresh: () -> Unit,
    onPush: () -> Unit,
    onSignOut: () -> Unit,
    onBack: () -> Unit,
    snackbar: SnackbarHostState,
) {
    // Hoisted above the `when` deliberately: the Working branch removes the form from
    // composition, so state remembered inside it would be discarded on every attempt and
    // the user would have to retype after each failure.
    var email by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }
    var creating by rememberSaveable { mutableStateOf(false) }
    var passphrase by rememberSaveable { mutableStateOf("") }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        topBar = {
            TopAppBar(
                title = { Text("Sync") },
                navigationIcon = { TextButton(onClick = onBack) { Text("Back") } },
            )
        },
    ) { padding ->
        Box(Modifier.fillMaxSize().padding(padding).padding(24.dp)) {
            when (sync) {
                SyncState.NotConfigured -> Info(
                    "Not configured",
                    "Add supabase.url and supabase.key to mobile/local.properties, then rebuild.",
                )
                SyncState.Working -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                    CircularProgressIndicator()
                }
                is SyncState.Off -> CredentialsForm(
                    email = email,
                    password = password,
                    creating = creating,
                    error = sync.error,
                    notice = sync.notice,
                    onEmail = { email = it },
                    onPassword = { password = it },
                    onToggleMode = { creating = !creating },
                    onSubmit = { if (creating) onSignUp(email, password) else onSignIn(email, password) },
                    onGithub = onGithub,
                )
                is SyncState.NeedsMfaCode -> MfaCodeForm(
                    error = sync.error,
                    onSubmit = onMfaCode,
                    onSignOut = onSignOut,
                )
                SyncState.NeedsPassphrase -> PassphraseForm(
                    "These tasks are end-to-end encrypted. Enter your sync passphrase to read them.",
                    isError = false,
                    onSubmit = onPassphrase,
                )
                SyncState.WrongPassphrase -> PassphraseForm(
                    "That passphrase can't decrypt this account's data. Nothing has been changed or lost.",
                    isError = true,
                    onSubmit = onPassphrase,
                )
                is SyncState.On -> Column {
                    Text("Sync is on", style = MaterialTheme.typography.titleMedium)
                    Spacer(Modifier.height(4.dp))
                    Text(
                        sync.email ?: "signed in",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    if (sync.serverUpdatedAt.isNotEmpty()) {
                        Text(
                            // Trimmed from the raw ISO stamp: seconds and microseconds are
                            // noise to a person deciding whether their data is current.
                            "Server copy: " + sync.serverUpdatedAt.take(16).replace('T', ' ') + " UTC",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }

                    if (!sync.hasPassphrase) {
                        Spacer(Modifier.height(16.dp))
                        Banner(
                            "Set a passphrase to send changes up. Your tasks are encrypted on " +
                                "this phone before they leave it, so nobody with server access " +
                                "can read them — not even us. There is no way to recover it.",
                            MaterialTheme.colorScheme.secondaryContainer,
                            MaterialTheme.colorScheme.onSecondaryContainer,
                        )
                        Spacer(Modifier.height(12.dp))
                        OutlinedTextField(
                            value = passphrase,
                            onValueChange = { passphrase = it },
                            label = { Text("Passphrase") },
                            singleLine = true,
                            visualTransformation = PasswordVisualTransformation(),
                            keyboardOptions = KeyboardOptions(
                                keyboardType = KeyboardType.Password,
                                imeAction = ImeAction.Done,
                            ),
                            modifier = Modifier.fillMaxWidth(),
                        )
                        Text(
                            "At least $MIN_PASSPHRASE characters.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(top = 4.dp),
                        )
                        Spacer(Modifier.height(8.dp))
                        Button(
                            onClick = { onPassphrase(passphrase) },
                            // Must match the Windows minimum exactly. A shorter passphrase
                            // set here would be impossible to type on Windows, which
                            // rejects under 8 — the two clients would silently diverge.
                            enabled = passphrase.length >= MIN_PASSPHRASE,
                            modifier = Modifier.fillMaxWidth(),
                        ) { Text("Set passphrase") }
                    }

                    Spacer(Modifier.height(20.dp))
                    Button(onClick = onRefresh, Modifier.fillMaxWidth()) { Text("Pull now") }
                    Spacer(Modifier.height(8.dp))
                    Button(
                        onClick = onPush,
                        enabled = sync.hasPassphrase,
                        modifier = Modifier.fillMaxWidth(),
                    ) { Text("Push now") }
                    Spacer(Modifier.height(8.dp))
                    OutlinedButton(onClick = onSignOut, Modifier.fillMaxWidth()) { Text("Sign out") }
                }
                is SyncState.Failed -> Column {
                    Info("Sync failed", sync.message)
                    Spacer(Modifier.height(20.dp))
                    Button(onClick = onRefresh, Modifier.fillMaxWidth()) { Text("Try again") }
                }
            }
        }
    }
}

@Composable
private fun CredentialsForm(
    email: String,
    password: String,
    creating: Boolean,
    error: String?,
    notice: String?,
    onEmail: (String) -> Unit,
    onPassword: (String) -> Unit,
    onToggleMode: () -> Unit,
    onSubmit: () -> Unit,
    onGithub: () -> Unit,
) {
    Column {
        Text(
            if (creating) "Create a sync account" else "Sync across devices",
            style = MaterialTheme.typography.titleMedium,
        )
        Spacer(Modifier.height(4.dp))
        Text(
            "Optional. Your tasks stay on this phone until you sign in.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        if (error != null) {
            Spacer(Modifier.height(16.dp))
            Banner(error, MaterialTheme.colorScheme.errorContainer, MaterialTheme.colorScheme.onErrorContainer)
        }
        if (notice != null) {
            Spacer(Modifier.height(16.dp))
            Banner(notice, MaterialTheme.colorScheme.secondaryContainer, MaterialTheme.colorScheme.onSecondaryContainer)
        }

        Spacer(Modifier.height(20.dp))
        OutlinedTextField(
            value = email,
            onValueChange = onEmail,
            label = { Text("Email") },
            singleLine = true,
            isError = error != null,
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Email,
                imeAction = ImeAction.Next,
            ),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(12.dp))
        OutlinedTextField(
            value = password,
            onValueChange = onPassword,
            label = { Text("Password") },
            singleLine = true,
            isError = error != null,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Password,
                imeAction = ImeAction.Done,
            ),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(20.dp))
        Button(
            onClick = onSubmit,
            enabled = email.isNotBlank() && password.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) { Text(if (creating) "Create account" else "Sign in") }
        Spacer(Modifier.height(8.dp))
        TextButton(onClick = onToggleMode, modifier = Modifier.fillMaxWidth()) {
            Text(if (creating) "I already have an account" else "Create an account instead")
        }

        Spacer(Modifier.height(20.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            HorizontalDivider(Modifier.weight(1f))
            Text(
                "  or  ",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            HorizontalDivider(Modifier.weight(1f))
        }
        Spacer(Modifier.height(20.dp))
        OutlinedButton(onClick = onGithub, modifier = Modifier.fillMaxWidth()) {
            Text("Continue with GitHub")
        }
    }
}

@Composable
private fun Banner(text: String, container: androidx.compose.ui.graphics.Color, content: androidx.compose.ui.graphics.Color) {
    Surface(color = container, shape = MaterialTheme.shapes.small, modifier = Modifier.fillMaxWidth()) {
        Text(
            text,
            style = MaterialTheme.typography.bodyMedium,
            color = content,
            modifier = Modifier.padding(12.dp),
        )
    }
}

@Composable
private fun MfaCodeForm(error: String?, onSubmit: (String) -> Unit, onSignOut: () -> Unit) {
    var value by remember { mutableStateOf("") }

    Column {
        Text("Two-factor code", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        Text(
            "This account uses an authenticator app. Enter the current 6-digit code to " +
                "turn sync on. Your tasks on this phone are unaffected.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        if (error != null) {
            Spacer(Modifier.height(16.dp))
            Banner(error, MaterialTheme.colorScheme.errorContainer, MaterialTheme.colorScheme.onErrorContainer)
        }

        Spacer(Modifier.height(20.dp))
        OutlinedTextField(
            value = value,
            // Authenticators show digits only; filtering here stops a paste of "123 456"
            // from being rejected by the server for a reason the user cannot see.
            onValueChange = { value = it.filter(Char::isDigit).take(MFA_CODE_LENGTH) },
            label = { Text("6-digit code") },
            singleLine = true,
            isError = error != null,
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.NumberPassword,
                imeAction = ImeAction.Done,
            ),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(20.dp))
        Button(
            onClick = { onSubmit(value) },
            enabled = value.length == MFA_CODE_LENGTH,
            modifier = Modifier.fillMaxWidth(),
        ) { Text("Verify") }
        Spacer(Modifier.height(8.dp))
        // The only way out of this state. Without it a lost authenticator strands the
        // screen with no route back to the task list's sign-in.
        OutlinedButton(onClick = onSignOut, Modifier.fillMaxWidth()) { Text("Sign out") }
    }
}

@Composable
private fun PassphraseForm(prompt: String, isError: Boolean, onSubmit: (String) -> Unit) {
    var value by remember { mutableStateOf("") }

    Column {
        Text("Sync passphrase", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        Text(
            prompt,
            style = MaterialTheme.typography.bodyMedium,
            color = if (isError) MaterialTheme.colorScheme.error
                    else MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.height(20.dp))
        OutlinedTextField(
            value = value,
            onValueChange = { value = it },
            label = { Text("Passphrase") },
            singleLine = true,
            isError = isError,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Password,
                imeAction = ImeAction.Done,
            ),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(20.dp))
        Button(
            onClick = { onSubmit(value) },
            // Deliberately NOT the minimum: this unlocks an existing row. The minimum
            // constrains choosing a new passphrase, and applying it here would lock a user
            // out of data they encrypted before the rule existed.
            enabled = value.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) { Text("Unlock") }
    }
}

@Composable
private fun Info(title: String, body: String) {
    Column {
        Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
        Spacer(Modifier.height(8.dp))
        Text(
            body,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
