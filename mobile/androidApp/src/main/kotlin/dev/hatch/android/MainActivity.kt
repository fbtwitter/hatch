package dev.hatch.android

import android.content.Intent
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.animation.animateColorAsState
import androidx.compose.foundation.clickable
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem

// Mirrors the Windows minimum (SettingsViewModel.SetSyncPassphraseAsync).
private const val MIN_PASSPHRASE = 8

// TOTP is always 6 digits (docs/mfa-spec.md).
private const val MFA_CODE_LENGTH = 6

// Material's adaptive guidance caps line length for readability. Without this, a task title
// on a 2340px landscape screen runs the full width and reads like a spreadsheet row; the
// checkbox also ends up marooned from its label. Centred, so portrait is unaffected.
private val ContentMaxWidth = 640.dp

class MainActivity : ComponentActivity() {

    // Same instance the composables get from viewModel(), since both resolve against this
    // activity's ViewModelStore.
    private val vm: CompanionViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        // Draws behind the system bars. Mandatory on Android 15+, and the reason Scaffold
        // is left to apply its own insets rather than being given hardcoded padding.
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)
        vm.handleDeeplink(intent)
        setContent {
            val themeMode by vm.state.collectAsState()
            HatchTheme(themeMode.themeMode) {
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

// Material You: on Android 12+ the palette is derived from the user's wallpaper, which is
// what makes a Compose app look like it belongs on the device rather than merely running on
// it. Below 12 there is no wallpaper palette, so the stock scheme is the correct fallback.
@Composable
private fun HatchTheme(mode: ThemeMode, content: @Composable () -> Unit) {
    val dark = when (mode) {
        ThemeMode.System -> isSystemInDarkTheme()
        ThemeMode.Light -> false
        ThemeMode.Dark -> true
    }
    val context = LocalContext.current
    val scheme = when {
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.S ->
            if (dark) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)
        dark -> darkColorScheme()
        else -> lightColorScheme()
    }
    MaterialTheme(colorScheme = scheme, content = content)
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
            onShowRecovery = vm::showRecoveryCodeEntry,
            onRedeemRecovery = vm::redeemRecoveryCode,
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
            loaded = state.loaded,
            themeMode = state.themeMode,
            snackbar = snackbar,
            onAdd = vm::addTask,
            onToggle = vm::toggleComplete,
            onThemeMode = vm::setThemeMode,
            onOpenSync = { showSync = true },
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TaskScreen(
    tasks: List<TodoItem>,
    lists: List<TaskList>,
    loaded: Boolean,
    themeMode: ThemeMode,
    snackbar: SnackbarHostState,
    onAdd: (String) -> Unit,
    onToggle: (TodoItem) -> Unit,
    onThemeMode: (ThemeMode) -> Unit,
    onOpenSync: () -> Unit,
) {
    var draft by rememberSaveable { mutableStateOf("") }
    // Derived, not recomputed: without remember these three run on every recomposition —
    // including every keystroke in the add field, which touches none of them.
    val open = remember(tasks) { tasks.filter { !it.isCompleted } }
    val done = remember(tasks) { tasks.filter { it.isCompleted } }
    val listNames = remember(lists) { lists.associate { it.id to it.name } }

    val scrollBehavior = TopAppBarDefaults.enterAlwaysScrollBehavior()
    val submit = { if (draft.isNotBlank()) { onAdd(draft); draft = "" } }

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        snackbarHost = { SnackbarHost(snackbar) },
        topBar = {
            TopAppBar(
                title = { Text("Hatch", fontWeight = FontWeight.SemiBold) },
                actions = {
                    TextButton(onClick = onOpenSync) { Text("Sync") }
                    ThemeMenu(themeMode, onThemeMode)
                },
                // The bar tints toward surfaceContainer as content scrolls under it —
                // standard M3 behaviour that gives the list somewhere to go.
                colors = TopAppBarDefaults.topAppBarColors(
                    scrolledContainerColor = MaterialTheme.colorScheme.surfaceContainer,
                ),
                scrollBehavior = scrollBehavior,
            )
        },
        bottomBar = {
            // surfaceContainer is the M3 token for a raised container, rather than picking
            // a tonalElevation dp value by eye — it tracks the dynamic palette correctly in
            // both light and dark.
            Surface(color = MaterialTheme.colorScheme.surfaceContainer) {
              Box(Modifier.fillMaxWidth()) {
                Row(
                    Modifier
                        // imePadding lifts the field above the keyboard; navigationBars
                        // keeps it clear of the gesture bar under edge-to-edge.
                        .imePadding()
                        .navigationBarsPadding()
                        .widthIn(max = ContentMaxWidth)
                        .align(Alignment.TopCenter)
                        .padding(horizontal = 12.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    OutlinedTextField(
                        value = draft,
                        onValueChange = { draft = it },
                        placeholder = { Text("Add a task") },
                        singleLine = true,
                        shape = MaterialTheme.shapes.large,
                        keyboardOptions = KeyboardOptions(
                            capitalization = KeyboardCapitalization.Sentences,
                            imeAction = ImeAction.Done,
                        ),
                        // Capture in ≤4s is the product premise; reaching for the button
                        // after typing is the slowest part of it.
                        keyboardActions = KeyboardActions(onDone = { submit() }),
                        modifier = Modifier.weight(1f),
                    )
                    Spacer(Modifier.width(8.dp))
                    FilledIconButton(
                        onClick = submit,
                        enabled = draft.isNotBlank(),
                        modifier = Modifier.size(52.dp),
                    ) { Icon(Icons.Rounded.Add, contentDescription = "Add task") }
                }
              }
            }
        },
    ) { padding ->
        // Nothing at all until the disk read lands: showing "Nothing yet" for one frame and
        // then replacing it with the real list reads as a bug.
        if (!loaded) return@Scaffold

        if (tasks.isEmpty()) {
            Column(
                Modifier.fillMaxSize().padding(padding).padding(32.dp),
                verticalArrangement = Arrangement.Center,
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Icon(
                    Icons.Rounded.CheckCircle,
                    contentDescription = null,
                    modifier = Modifier.size(56.dp),
                    tint = MaterialTheme.colorScheme.primary.copy(alpha = 0.35f),
                )
                Spacer(Modifier.height(16.dp))
                Text("Nothing yet", style = MaterialTheme.typography.titleMedium)
                Spacer(Modifier.height(6.dp))
                Text(
                    "Add a task below. No account needed — Sync is optional.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                )
            }
            return@Scaffold
        }

        Box(Modifier.fillMaxSize()) {
            LazyColumn(
                modifier = Modifier
                    .fillMaxHeight()
                    .widthIn(max = ContentMaxWidth)
                    .align(Alignment.TopCenter),
                contentPadding = padding,
            ) {
                // contentType lets row composables be reused across both sections instead of
                // being torn down and rebuilt when scrolling past the divider.
                items(open, key = { it.id }, contentType = { "task" }) { task ->
                    TaskRow(task, listNames, onToggle, Modifier.animateItem())
                }
                if (done.isNotEmpty()) {
                    item(key = "completed-header", contentType = "header") {
                        Text(
                            "Completed · ${done.size}",
                            style = MaterialTheme.typography.labelLarge,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier
                                .animateItem()
                                .padding(start = 20.dp, top = 24.dp, bottom = 8.dp),
                        )
                    }
                    items(done, key = { it.id }, contentType = { "task" }) { task ->
                        TaskRow(task, listNames, onToggle, Modifier.animateItem())
                    }
                }
            }
        }
    }
}

// Overflow menu is the M3 pattern for a secondary setting that does not deserve a screen of
// its own. Mirrors the Windows Settings → Appearance → Theme options exactly.
@Composable
private fun ThemeMenu(current: ThemeMode, onSelect: (ThemeMode) -> Unit) {
    var open by remember { mutableStateOf(false) }

    Box {
        IconButton(onClick = { open = true }) {
            Icon(Icons.Rounded.MoreVert, contentDescription = "More options")
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            Text(
                "Theme",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(start = 12.dp, top = 8.dp, bottom = 4.dp),
            )
            ThemeMode.entries.forEach { mode ->
                DropdownMenuItem(
                    text = {
                        Text(
                            when (mode) {
                                ThemeMode.System -> "System default"
                                ThemeMode.Light -> "Light"
                                ThemeMode.Dark -> "Dark"
                            }
                        )
                    },
                    // Leading check reserves the same space for every row, so the labels do
                    // not shift as the selection moves.
                    leadingIcon = {
                        if (mode == current) {
                            Icon(Icons.Rounded.Check, contentDescription = "Selected")
                        } else {
                            Spacer(Modifier.size(24.dp))
                        }
                    },
                    onClick = { onSelect(mode); open = false },
                )
            }
        }
    }
}

@Composable
private fun TaskRow(
    task: TodoItem,
    listNames: Map<String, String>,
    onToggle: (TodoItem) -> Unit,
    modifier: Modifier = Modifier,
) {
    val haptics = LocalHapticFeedback.current
    val meta = remember(task.listId, task.dueDate, task.tags, listNames) {
        listOfNotNull(
            listNames[task.listId],
            task.dueDate?.take(10),
            task.tags.takeIf { it.isNotEmpty() }?.joinToString(" ") { "#$it" },
        ).joinToString("  ·  ")
    }
    // Completing is the most-repeated action in the app; easing the colour change stops the
    // row from snapping and makes the tap feel acknowledged. Only the two title colours are
    // animated — animating the whole row would run a per-row animation while scrolling.
    val titleColor by animateColorAsState(
        if (task.isCompleted) MaterialTheme.colorScheme.onSurfaceVariant
        else MaterialTheme.colorScheme.onSurface,
        label = "titleColor",
    )

    // remember: a fresh lambda each recomposition is a new instance, which defeats the
    // skipping that the stability config just bought us.
    val toggle = remember(task.id, task.isCompleted) {
        {
            haptics.performHapticFeedback(HapticFeedbackType.LongPress)
            onToggle(task)
        }
    }

    Column(modifier) {
        ListItem(
            // The whole row is the target, not just the checkbox — a 48dp checkbox is a
            // hard aim one-handed.
            modifier = Modifier.clickable(onClick = toggle),
            leadingContent = {
                Checkbox(checked = task.isCompleted, onCheckedChange = { toggle() })
            },
            headlineContent = {
                Text(
                    task.title,
                    style = MaterialTheme.typography.bodyLarge,
                    textDecoration = if (task.isCompleted) TextDecoration.LineThrough else null,
                    color = titleColor,
                )
            },
            supportingContent = if (meta.isEmpty()) null else {
                { Text(meta, style = MaterialTheme.typography.bodySmall) }
            },
            colors = ListItemDefaults.colors(containerColor = Color.Transparent),
        )
        HorizontalDivider(
            modifier = Modifier.padding(start = 56.dp),
            color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f),
        )
    }
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
    onShowRecovery: (Boolean) -> Unit,
    onRedeemRecovery: (String) -> Unit,
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
                is SyncState.NeedsMfaCode ->
                    if (sync.redeeming) {
                        RecoveryCodeForm(
                            error = sync.error,
                            onSubmit = onRedeemRecovery,
                            onBack = { onShowRecovery(false) },
                        )
                    } else {
                        MfaCodeForm(
                            error = sync.error,
                            onSubmit = onMfaCode,
                            onUseRecovery = { onShowRecovery(true) },
                            onSignOut = onSignOut,
                        )
                    }
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
private fun RecoveryCodeForm(error: String?, onSubmit: (String) -> Unit, onBack: () -> Unit) {
    var value by remember { mutableStateOf("") }

    Column {
        Text("Use a recovery code", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        // Stated plainly because it is not what most people expect a recovery code to do.
        Text(
            "This turns two-factor authentication OFF and discards your remaining codes — " +
                "it is not a one-time sign-in. Set it up again afterwards on Windows.\n\n" +
                "Your tasks and your sync passphrase are unaffected.",
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
            onValueChange = { value = it.uppercase() },
            label = { Text("Recovery code") },
            placeholder = { Text("XXXXX-XXXXX") },
            singleLine = true,
            isError = error != null,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(20.dp))
        Button(
            onClick = { onSubmit(value) },
            enabled = value.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) { Text("Turn off two-factor and continue") }
        Spacer(Modifier.height(8.dp))
        OutlinedButton(onClick = onBack, Modifier.fillMaxWidth()) { Text("Back") }
    }
}

@Composable
private fun MfaCodeForm(
    error: String?,
    onSubmit: (String) -> Unit,
    onUseRecovery: () -> Unit,
    onSignOut: () -> Unit,
) {
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
        // The routes out of this state. Without them a lost authenticator strands the
        // screen with no way back — and since the aal2 policy landed, signing out alone
        // would not have helped either.
        TextButton(onClick = onUseRecovery, Modifier.fillMaxWidth()) {
            Text("Lost your authenticator? Use a recovery code")
        }
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
