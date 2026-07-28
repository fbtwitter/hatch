package dev.hatch.android

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Delete
import androidx.compose.material.icons.rounded.Menu
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material3.*
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.pulltorefresh.PullToRefreshDefaults
import androidx.compose.material3.pulltorefresh.rememberPullToRefreshState
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.autofill.ContentType
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.draw.clip
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.contentType
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.viewmodel.compose.viewModel
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.launch

// Mirrors the Windows minimum (SettingsViewModel.SetSyncPassphraseAsync).
private const val MIN_PASSPHRASE = 8

// TOTP is always 6 digits (docs/mfa-spec.md).
private const val MFA_CODE_LENGTH = 6

// Caps line length on wide screens, where a full-width title reads like a spreadsheet row
// and strands the checkbox from its label. Centred, so portrait is unaffected.
internal val ContentMaxWidth = 640.dp

class MainActivity : ComponentActivity() {

    // Same instance the composables get from viewModel(), since both resolve against this
    // activity's ViewModelStore.
    private val vm: CompanionViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        // Must precede super.onCreate. Below Android 12 this draws the icon splash the
        // system only gained in 12; on 12+ it re-themes the one the system already shows.
        val splash = installSplashScreen()
        // Mandatory on Android 15+, and why Scaffold applies its own insets.
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)
        // Holds the splash over the frames the Scaffold would otherwise paint blank while
        // the first disk read is in flight. One small file — never long enough to ANR.
        splash.setKeepOnScreenCondition { !vm.state.value.loaded }
        vm.handleDeeplink(intent)
        ensureNotificationChannel(this)

        // Asked once, never insisted upon: a refusal costs reminders and nothing else.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 0)
        }
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

// Three destinations and no library: a nav graph for two leaves would cost a dependency and
// an extra indirection for a phone app whose whole job is one list.
private enum class Screen { Tasks, Settings, Sync }

@Composable
private fun HatchApp(vm: CompanionViewModel = viewModel()) {
    val state by vm.state.collectAsState()
    // The app opens on the task list and never asks who you are — sync is opt-in.
    var screen by rememberSaveable { mutableStateOf(Screen.Tasks) }
    val snackbar = remember { SnackbarHostState() }

    // Stopped on pause so a backgrounded app holds no timer.
    LifecycleResumeEffect(Unit) {
        vm.startAutoPull()
        onPauseOrDispose { vm.stopAutoPull() }
    }

    // A fetch, not a setting: on success get out of the way. Failures and prompts keep you
    // on the Sync screen, because those need an answer.
    LaunchedEffect(Unit) {
        vm.pullCompleted.collect { count ->
            screen = Screen.Tasks
            snackbar.showSnackbar("Pulled — $count task${if (count == 1) "" else "s"}")
        }
    }

    // Push does not navigate away: nothing on the task list changes as a result.
    LaunchedEffect(Unit) {
        vm.pushCompleted.collect { count ->
            snackbar.showSnackbar("Pushed — $count task${if (count == 1) "" else "s"} encrypted and sent")
        }
    }

    // A pull-to-refresh that ends badly would otherwise just stop its spinner with no
    // explanation. Gated on the previous state being Working so only a deliberate
    // operation reports here — a failed background push (previous state On) stays silent,
    // or a flaky network would raise a snackbar on every edit made offline.
    LaunchedEffect(Unit) {
        var previous: SyncState? = null
        snapshotFlow { state.sync }.collect { sync ->
            val wasWorking = previous is SyncState.Working
            previous = sync
            if (!wasWorking || screen != Screen.Tasks) return@collect
            when (sync) {
                is SyncState.Failed -> snackbar.showSnackbar(sync.message)
                SyncState.NeedsPassphrase, SyncState.WrongPassphrase, is SyncState.NeedsMfaCode ->
                    snackbar.showSnackbar("Sync needs your attention — open Settings → Sync")
                else -> Unit
            }
        }
    }

    // Back left a secondary screen by leaving the app entirely, because nothing was
    // listening. Same target as each screen's own up arrow.
    BackHandler(enabled = screen != Screen.Tasks) {
        screen = if (screen == Screen.Sync) Screen.Settings else Screen.Tasks
    }

    // Shared-axis X between the three destinations. Direction comes from the enum order, so
    // going in slides one way and coming back slides the other without tracking history.
    AnimatedContent(
        targetState = screen,
        transitionSpec = { screenTransition(forward = targetState.ordinal > initialState.ordinal) },
        label = "screen",
    ) { current ->
        when (current) {
            Screen.Sync -> SyncScreen(
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
                onBack = { screen = Screen.Settings },
                snackbar = snackbar,
            )

            Screen.Settings -> SettingsScreen(
                themeMode = state.themeMode,
                sync = state.sync,
                onThemeMode = vm::setThemeMode,
                onOpenSync = { screen = Screen.Sync },
                onBack = { screen = Screen.Tasks },
            )

            Screen.Tasks -> {
                // By id, not by value: a save replaces the instance in the list.
                var editingId by rememberSaveable { mutableStateOf<String?>(null) }
                val editing = state.tasks.firstOrNull { it.id == editingId }
                val scope = rememberCoroutineScope()

                // A nullable TaskList alone could not tell "closed" from "creating".
                var listDialog by remember { mutableStateOf<ListDialog?>(null) }

                // One deletion path for the swipe and the sheet's Delete button. The sheet
                // used to call the ViewModel directly, so the most deliberate delete in the
                // app — the one that propagates to every synced device — was the only one
                // with no way back.
                val deleteWithUndo: (TodoItem) -> Unit = { task ->
                    vm.deleteTask(task)
                    scope.launch {
                        // Long, not Short: four seconds to notice a destructive action and
                        // reach the button is not enough, and the delete now propagates to
                        // every synced device.
                        val result = snackbar.showSnackbar(
                            message = "Deleted \"${task.title.take(30)}\"",
                            actionLabel = "Undo",
                            duration = SnackbarDuration.Long,
                        )
                        if (result == SnackbarResult.ActionPerformed) vm.restoreTask(task)
                    }
                }

                TaskScreen(
                    tasks = state.tasks,
                    lists = state.lists,
                    loaded = state.loaded,
                    activeNav = state.activeNav,
                    searchQuery = state.searchQuery,
                    // Working included so the box stays mounted for the pull it is
                    // spinning for — On alone would unmount it the moment a pull began.
                    refreshEnabled = state.sync is SyncState.On || state.sync is SyncState.Working,
                    refreshing = state.sync is SyncState.Working,
                    snackbar = snackbar,
                    onNavigate = vm::setActiveNav,
                    onSearch = vm::setSearchQuery,
                    onRefresh = vm::refresh,
                    onCreateList = { listDialog = ListDialog.New },
                    onEditList = { listDialog = ListDialog.Edit(it) },
                    onAdd = vm::addTask,
                    onToggle = vm::toggleComplete,
                    onOpen = { editingId = it.id },
                    onDelete = deleteWithUndo,
                    onOpenSettings = { screen = Screen.Settings },
                )

                if (editing != null) {
                    TaskDetailSheet(
                        task = editing,
                        lists = state.lists,
                        onSave = vm::saveTask,
                        onDelete = deleteWithUndo,
                        onDismiss = { editingId = null },
                    )
                }

                listDialog?.let { dialog ->
                    ListEditorDialog(
                        existing = (dialog as? ListDialog.Edit)?.list,
                        onCreate = vm::createList,
                        onRename = vm::renameList,
                        onTogglePin = vm::togglePinList,
                        onDelete = vm::deleteList,
                        onDismiss = { listDialog = null },
                    )
                }
            }
        }
    }
}

private sealed interface ListDialog {
    data object New : ListDialog
    data class Edit(val list: TaskList) : ListDialog
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TaskScreen(
    tasks: List<TodoItem>,
    lists: List<TaskList>,
    loaded: Boolean,
    activeNav: String,
    searchQuery: String,
    refreshEnabled: Boolean,
    refreshing: Boolean,
    snackbar: SnackbarHostState,
    onNavigate: (String) -> Unit,
    onSearch: (String) -> Unit,
    onRefresh: () -> Unit,
    onCreateList: () -> Unit,
    onEditList: (TaskList) -> Unit,
    onAdd: (String) -> String?,
    onToggle: (TodoItem) -> Unit,
    onOpen: (TodoItem) -> Unit,
    onDelete: (TodoItem) -> Unit,
    onOpenSettings: () -> Unit,
) {
    val searching = searchQuery.isNotEmpty()

    // Derived, not recomputed: without remember these run whenever anything on the screen
    // changes, and none of them depend on most of it.
    val visible = remember(tasks, activeNav, searchQuery) {
        if (searching) searchResults(tasks, searchQuery) else tasksForNav(tasks, activeNav)
    }
    val open = remember(visible) { visible.filter { !it.isCompleted } }
    val done = remember(visible) { visible.filter { it.isCompleted } }
    val listNames = remember(lists) { lists.associate { it.id to it.name } }

    // The flexible bar's second line. Open-only, matching the drawer badges.
    val countLabel = remember(open.size, done.size) {
        when {
            open.isEmpty() && done.isEmpty() -> "Nothing here"
            open.isEmpty() -> "All done"
            else -> "${open.size} open" + if (done.isEmpty()) "" else " · ${done.size} done"
        }
    }

    // exitUntilCollapsed, not enterAlways: a two-row flexible bar has a large title to give
    // back, and enterAlways would slam it open on the smallest upward flick.
    val scrollBehavior = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()
    val listState = rememberLazyListState()
    var pendingScrollId by remember { mutableStateOf<String?>(null) }

    // The add field is at the bottom, so a task added while scrolled down otherwise lands
    // off-screen and reads as nothing having happened. By id rather than index 0 because
    // Important and Planned do not put the newest task first.
    LaunchedEffect(pendingScrollId, open) {
        val id = pendingScrollId ?: return@LaunchedEffect
        val index = open.indexOfFirst { it.id == id }
        if (index >= 0) listState.animateScrollToItem(index)
        pendingScrollId = null
    }

    val drawerState = rememberDrawerState(DrawerValue.Closed)
    val scope = rememberCoroutineScope()

    // The search bar does not take the scroll behavior, so anything it collapsed while the
    // flexible bar was mounted would still be collapsed on the way back.
    LaunchedEffect(searching) { scrollBehavior.state.heightOffset = 0f }

    // Search was the one overlay state back did not unwind — it left the app instead.
    // The drawer and the detail sheet need nothing here: material3 1.4.0 registers its own
    // predictive-back callbacks for both (DrawerPredictiveBackHandler and the sheet dialog's
    // PredictiveBackOnBackPressedCallback), and those are composed deeper, so they take
    // priority over this one whenever they are open.
    BackHandler(enabled = searching) { onSearch("") }

    ModalNavigationDrawer(
        drawerState = drawerState,
        drawerContent = {
            ListsDrawerSheet(
                tasks = tasks,
                lists = lists,
                activeNav = activeNav,
                onNavigate = { onNavigate(it); scope.launch { drawerState.close() } },
                onCreateList = { onCreateList(); scope.launch { drawerState.close() } },
                onEditList = { onEditList(it); scope.launch { drawerState.close() } },
                onOpenSettings = { onOpenSettings(); scope.launch { drawerState.close() } },
            )
        },
    ) {
    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        snackbarHost = { SnackbarHost(snackbar) },
        topBar = {
            if (searching) {
                SearchTopBar(searchQuery, onSearch)
            } else {
                // The two-row collapsing bar Material uses for a screen's primary title.
                // The count rides in the title slot because the stable MediumTopAppBar has
                // no subtitle parameter — that arrived with the Flexible bars.
                MediumTopAppBar(
                    title = {
                        Column {
                            Text(navTitle(activeNav, lists))
                            Text(
                                countLabel,
                                style = MaterialTheme.typography.labelMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    },
                    navigationIcon = {
                        IconButton(onClick = { scope.launch { drawerState.open() } }) {
                            Icon(Icons.Rounded.Menu, contentDescription = "Lists")
                        }
                    },
                    // Search only. Settings is a preference, not an action on this list, so
                    // it lives in the drawer footer rather than beside the list's own verbs.
                    actions = {
                        // A space, not "": search is active when the query is non-empty.
                        // searchResults trims, so it still matches nothing.
                        IconButton(onClick = { onSearch(" ") }) {
                            Icon(Icons.Rounded.Search, contentDescription = "Search")
                        }
                    },
                    colors = TopAppBarDefaults.topAppBarColors(
                        scrolledContainerColor = MaterialTheme.colorScheme.surfaceContainer,
                    ),
                    scrollBehavior = scrollBehavior,
                )
            }
        },
        bottomBar = { AddTaskBar(onSubmit = { title -> pendingScrollId = onAdd(title) }) },
    ) { padding ->
        // Nothing at all until the disk read lands: showing "Nothing yet" for one frame and
        // then replacing it with the real list reads as a bug.
        if (!loaded) return@Scaffold

        // Completing or deleting the last task used to swap the list for the empty state
        // between two frames. A fade only — nothing has moved, so nothing should slide.
        val body: @Composable () -> Unit = {
            AnimatedContent(
                targetState = visible.isEmpty(),
                transitionSpec = { contentFade() },
                label = "body",
            ) { empty ->
                if (empty) {
                    EmptyState(
                        searching = searching,
                        activeNav = activeNav,
                        anyTasks = tasks.isNotEmpty(),
                        modifier = Modifier.padding(padding),
                    )
                } else {
                    Box(Modifier.fillMaxSize()) {
                        LazyColumn(
                            state = listState,
                            modifier = Modifier
                                .fillMaxHeight()
                                .widthIn(max = ContentMaxWidth)
                                .align(Alignment.TopCenter),
                            contentPadding = padding,
                            verticalArrangement = Arrangement.spacedBy(GroupGap),
                        ) {
                            // contentType lets rows be reused across both sections rather than rebuilt.
                            itemsIndexed(open, key = { _, t -> t.id }, contentType = { _, _ -> "task" }) { i, task ->
                                TaskRow(
                                    task, listNames, groupedShape(i, open.size),
                                    onToggle, onOpen, onDelete, Modifier.animateItem(),
                                )
                            }
                            if (done.isNotEmpty()) {
                                item(key = "completed-header", contentType = "header") {
                                    Text(
                                        "Completed · ${done.size}",
                                        style = MaterialTheme.typography.labelLarge,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                        modifier = Modifier
                                            .animateItem()
                                            .padding(start = 28.dp, top = 24.dp, bottom = 8.dp),
                                    )
                                }
                                itemsIndexed(done, key = { _, t -> t.id }, contentType = { _, _ -> "task" }) { i, task ->
                                    TaskRow(
                                        task, listNames, groupedShape(i, done.size),
                                        onToggle, onOpen, onDelete, Modifier.animateItem(),
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }

        // Composed only while signed in (or mid-pull): signed out gets no gesture and no
        // spinner at all — sync is opt-in, and a spring-back pull would advertise it.
        if (refreshEnabled) {
            val pullState = rememberPullToRefreshState()
            PullToRefreshBox(
                isRefreshing = refreshing,
                onRefresh = onRefresh,
                state = pullState,
                // The default slot pins the spinner to the box's own top edge, which sits
                // behind the app bar — the scaffold hands insets down as padding rather
                // than shrinking its content area.
                indicator = {
                    PullToRefreshDefaults.Indicator(
                        state = pullState,
                        isRefreshing = refreshing,
                        modifier = Modifier
                            .align(Alignment.TopCenter)
                            .padding(top = padding.calculateTopPadding()),
                    )
                },
            ) { body() }
        } else {
            body()
        }
    }
    }
}

// The draft lives here rather than in TaskScreen. Hoisted one level up, every character typed
// invalidated TaskScreen itself, so each keystroke re-ran the app bar, the drawer content and
// the whole list emit before the letter could appear — the single biggest contributor to
// typing feeling heavy, and worst in a debug build where composition is not optimised.
@Composable
private fun AddTaskBar(onSubmit: (String) -> Unit) {
    var draft by rememberSaveable { mutableStateOf("") }

    val submit = {
        if (draft.isNotBlank()) {
            onSubmit(draft)
            draft = ""
        }
    }

    // Insets on the Box and centring via contentAlignment, not Modifier.align on the Row: the
    // previous form measured as 1px tall while drawing full height, which left the Scaffold
    // placing the snackbar over an area that could not be touched.
    Box(
        Modifier
            .fillMaxWidth()
            .imePadding()
            .navigationBarsPadding()
            .padding(horizontal = 12.dp, vertical = 10.dp),
        contentAlignment = Alignment.TopCenter,
    ) {
        // Floats over the list rather than sitting in a full-width bar, which is where
        // Material put persistent actions once bottom bars stopped being a wall. Keeps the
        // field always visible — ≤4s capture is the product premise.
        Surface(
            color = MaterialTheme.colorScheme.surfaceContainerHigh,
            shape = MaterialTheme.shapes.extraLarge,
            shadowElevation = 6.dp,
            modifier = Modifier.widthIn(max = ContentMaxWidth),
        ) {
            Row(
                Modifier.padding(8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                // Borderless: the Surface is already the container, so an outline here would
                // draw a box inside a box.
                TextField(
                    value = draft,
                    onValueChange = { draft = it },
                    placeholder = { Text("Add a task") },
                    singleLine = true,
                    colors = TextFieldDefaults.colors(
                        focusedContainerColor = Color.Transparent,
                        unfocusedContainerColor = Color.Transparent,
                        focusedIndicatorColor = Color.Transparent,
                        unfocusedIndicatorColor = Color.Transparent,
                    ),
                    keyboardOptions = KeyboardOptions(
                        capitalization = KeyboardCapitalization.Sentences,
                        imeAction = ImeAction.Done,
                    ),
                    keyboardActions = KeyboardActions(onDone = { submit() }),
                    modifier = Modifier.weight(1f),
                )
                Spacer(Modifier.width(8.dp))
                // Grows as the field becomes submittable. A disabled-to-enabled colour flip
                // alone is easy to miss with a thumb over the button.
                val addScale by animateFloatAsState(
                    targetValue = if (draft.isNotBlank()) 1f else 0.88f,
                    animationSpec = tween(MotionShort, easing = EmphasizedDecelerate),
                    label = "addScale",
                )
                FilledIconButton(
                    onClick = submit,
                    enabled = draft.isNotBlank(),
                    modifier = Modifier
                        .size(52.dp)
                        .graphicsLayer { scaleX = addScale; scaleY = addScale },
                ) { Icon(Icons.Rounded.Add, contentDescription = "Add task") }
            }
        }
    }
}

@Composable
private fun EmptyState(
    searching: Boolean,
    activeNav: String,
    anyTasks: Boolean,
    modifier: Modifier = Modifier,
) {
    val (headline, subtext) = when {
        searching -> "No matches" to
            "Nothing in any list matches that — including completed tasks."
        activeNav == NAV_MY_DAY -> "My Day is clear" to
            "Add a task here, or open one and add it to My Day."
        activeNav == NAV_IMPORTANT -> "Nothing marked Important" to
            "Star a task to keep it here."
        activeNav == NAV_PLANNED -> "Nothing planned" to
            "Tasks with a due date show up here, soonest first."
        !anyTasks -> "Nothing yet" to
            "Add a task below. No account needed — Sync is optional."
        else -> "This list is empty" to "Add a task below."
    }

    Column(
        // Scrollable despite never overflowing: pull-to-refresh triggers off nested scroll,
        // and an empty list is exactly the state most worth refreshing.
        modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(32.dp),
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
        Text(headline, style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(6.dp))
        Text(
            subtext,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}

// A single-row bar while searching: the field is the subject, and a collapsing two-row bar
// would fight the keyboard for height.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SearchTopBar(searchQuery: String, onSearch: (String) -> Unit) {
    val focusRequester = remember { FocusRequester() }

    // Opening search used to leave the field unfocused, so the first tap on the icon did
    // nothing visible and the second one — on the field — is what started the search.
    LaunchedEffect(Unit) { focusRequester.requestFocus() }

    TopAppBar(
        title = {
            TextField(
                value = searchQuery,
                onValueChange = onSearch,
                placeholder = { Text("Search all tasks") },
                singleLine = true,
                colors = TextFieldDefaults.colors(
                    focusedContainerColor = Color.Transparent,
                    unfocusedContainerColor = Color.Transparent,
                    focusedIndicatorColor = Color.Transparent,
                    unfocusedIndicatorColor = Color.Transparent,
                ),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                modifier = Modifier.fillMaxWidth().focusRequester(focusRequester),
            )
        },
        navigationIcon = {
            IconButton(onClick = { onSearch("") }) {
                Icon(Icons.AutoMirrored.Rounded.ArrowBack, contentDescription = "Leave search")
            }
        },
        actions = {
            // Nothing to clear when the query is the placeholder space, and an inert
            // button reads as a broken one.
            if (searchQuery.isNotBlank()) {
                IconButton(onClick = { onSearch(" ") }) {
                    Icon(Icons.Rounded.Close, contentDescription = "Clear search")
                }
            }
        },
        colors = TopAppBarDefaults.topAppBarColors(
            containerColor = MaterialTheme.colorScheme.surfaceContainer,
        ),
    )
}

// Rows are one grouped container per section, the shape rounding only where the group
// actually ends — the pattern Pixel's own apps use in place of full-width dividers. Shared
// with the settings rows, so both screens speak the same shape language.
internal val GroupGap = 2.dp
private val GroupOuterCorner = 20.dp
private val GroupInnerCorner = 6.dp

internal fun groupedShape(index: Int, count: Int) = RoundedCornerShape(
    topStart = if (index == 0) GroupOuterCorner else GroupInnerCorner,
    topEnd = if (index == 0) GroupOuterCorner else GroupInnerCorner,
    bottomStart = if (index == count - 1) GroupOuterCorner else GroupInnerCorner,
    bottomEnd = if (index == count - 1) GroupOuterCorner else GroupInnerCorner,
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TaskRow(
    task: TodoItem,
    listNames: Map<String, String>,
    shape: RoundedCornerShape,
    onToggle: (TodoItem) -> Unit,
    onOpen: (TodoItem) -> Unit,
    onDelete: (TodoItem) -> Unit,
    modifier: Modifier = Modifier,
) {
    val haptics = LocalHapticFeedback.current
    val meta = remember(task.listId, task.dueDate, task.tags, task.priority, listNames) {
        listOfNotNull(
            listNames[task.listId],
            dueDateLabel(task.dueDate),
            PriorityMetaLabels.getOrNull(task.priority)?.takeIf { it.isNotEmpty() },
            task.tags.takeIf { it.isNotEmpty() }?.joinToString(" ") { "#$it" },
        ).joinToString("  ·  ")
    }
    // Title colour only: animating the whole row would run a per-row animation on scroll.
    val titleColor by animateColorAsState(
        if (task.isCompleted) MaterialTheme.colorScheme.onSurfaceVariant
        else MaterialTheme.colorScheme.onSurface,
        label = "titleColor",
    )

    // A fresh lambda each recomposition would defeat the stability config's skipping.
    val toggle = remember(task.id, task.isCompleted) {
        {
            haptics.performHapticFeedback(HapticFeedbackType.LongPress)
            onToggle(task)
        }
    }

    val dismissState = rememberSwipeToDismissBoxState()

    // The felt counterpart to the visual arming below: targetValue flips exactly when the
    // swipe crosses the commit threshold, and again if it retreats. Arming only — the
    // release already gets the LongPress in the delete collector.
    LaunchedEffect(dismissState) {
        snapshotFlow { dismissState.targetValue }
            .drop(1)
            .filter { it == SwipeToDismissBoxValue.EndToStart }
            .collect { haptics.performHapticFeedback(HapticFeedbackType.GestureThresholdActivate) }
    }

    // Keyed on the state object rather than on its value: the body below changes
    // currentValue, and keying on that cancelled this coroutine in the middle of its own work.
    LaunchedEffect(dismissState) {
        snapshotFlow { dismissState.currentValue }
            // The value the box already holds is not a gesture. LazyColumn saves each item's
            // state under its key, which is the task id, so a task restored by Undo came back
            // carrying the dismissed box it left with — and that alone re-ran the delete on
            // the next frame. That is why Undo looked like it did nothing.
            .drop(1)
            .filter { it == SwipeToDismissBoxValue.EndToStart }
            .collect {
                haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                // snapTo, not reset(): reset animates to Settled, and the row leaves the list
                // within a frame or two, cancelling that animation and saving a dismissed box
                // under the task id. Snapping first is instant, so the state that gets saved
                // is always upright.
                dismissState.snapTo(SwipeToDismissBoxValue.Settled)
                onDelete(task)
            }
    }

    Box(modifier.padding(horizontal = 12.dp).clip(shape)) {
        SwipeToDismissBox(
            state = dismissState,
            // One direction only: two-way makes an accidental delete far too easy.
            enableDismissFromStartToEnd = false,
            backgroundContent = {
                // Composed only while a swipe is actually under way. backgroundContent runs
                // for every row on screen, so animating unconditionally put three animation
                // objects behind each row for a gesture almost never in progress.
                if (dismissState.dismissDirection == SwipeToDismissBoxValue.Settled) {
                    return@SwipeToDismissBox
                }
                // Past the threshold the background commits to the stronger error colour and
                // the icon grows, so a swipe that will delete looks different from one that
                // will spring back — the only warning there is before the snackbar.
                val armed = dismissState.targetValue == SwipeToDismissBoxValue.EndToStart
                val container by animateColorAsState(
                    if (armed) MaterialTheme.colorScheme.error
                    else MaterialTheme.colorScheme.errorContainer,
                    tween(MotionShort),
                    label = "swipeContainer",
                )
                val iconTint by animateColorAsState(
                    if (armed) MaterialTheme.colorScheme.onError
                    else MaterialTheme.colorScheme.onErrorContainer,
                    tween(MotionShort),
                    label = "swipeIcon",
                )
                val iconScale by animateFloatAsState(
                    if (armed) 1.15f else 0.85f,
                    tween(MotionShort, easing = EmphasizedDecelerate),
                    label = "swipeIconScale",
                )
                Row(
                    Modifier
                        .fillMaxSize()
                        .background(container)
                        .padding(horizontal = 24.dp),
                    horizontalArrangement = Arrangement.End,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(
                        Icons.Rounded.Delete,
                        contentDescription = null,
                        tint = iconTint,
                        modifier = Modifier.graphicsLayer { scaleX = iconScale; scaleY = iconScale },
                    )
                }
            },
        ) {
            ListItem(
                // Body opens, checkbox completes: with editable fields there has to be a way
                // in that is not "complete it".
                modifier = Modifier.clickable { onOpen(task) },
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
                trailingContent = if (!task.isStarred) null else {
                    {
                        Icon(
                            Icons.Rounded.Star,
                            contentDescription = "Important",
                            tint = MaterialTheme.colorScheme.primary,
                        )
                    }
                },
                colors = ListItemDefaults.colors(
                    containerColor = if (task.isCompleted)
                        MaterialTheme.colorScheme.surfaceContainerLow
                    else MaterialTheme.colorScheme.surfaceContainer,
                ),
            )
        }
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
    // Hoisted above the `when`: the Working branch removes the form from composition, so
    // state remembered inside it would force a retype after every failure.
    var email by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }
    var creating by rememberSaveable { mutableStateOf(false) }
    var passphrase by rememberSaveable { mutableStateOf("") }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        topBar = {
            TopAppBar(
                title = { Text("Sync") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Rounded.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        // The spinner owns the whole screen, so it stays outside the scroll container —
        // fillMaxSize means nothing under an unbounded height constraint.
        if (sync is SyncState.Working) {
            Box(Modifier.fillMaxSize().padding(padding), Alignment.Center) {
                CircularProgressIndicator()
            }
            return@Scaffold
        }

        // Scrollable because the MFA and passphrase forms put their submit button under the
        // keyboard on a short screen, with no way to reach it.
        Column(
            Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .imePadding(),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Column(Modifier.widthIn(max = ContentMaxWidth).padding(24.dp)) {
                when (sync) {
                    SyncState.NotConfigured -> Info(
                        "Not configured",
                        "Add supabase.url and supabase.key to mobile/local.properties, then rebuild.",
                    )
                    // Handled above, before the scroll container.
                    SyncState.Working -> Unit
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
                                // Trimmed: seconds and microseconds are noise here.
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
                                // Must match Windows exactly: a shorter passphrase set here
                                // would be rejected there, and the clients would diverge.
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
            // Without content types password managers cannot see this form at all. The
            // passphrase and MFA fields deliberately carry none: a manager offering to
            // save the passphrase as "the password" would teach exactly the wrong thing.
            modifier = Modifier
                .fillMaxWidth()
                .semantics { contentType = ContentType.EmailAddress + ContentType.Username },
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
            modifier = Modifier
                .fillMaxWidth()
                .semantics {
                    contentType = if (creating) ContentType.NewPassword else ContentType.Password
                },
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
    Surface(color = container, shape = MaterialTheme.shapes.large, modifier = Modifier.fillMaxWidth()) {
        Text(
            text,
            style = MaterialTheme.typography.bodyMedium,
            color = content,
            modifier = Modifier.padding(16.dp),
        )
    }
}

@Composable
private fun RecoveryCodeForm(error: String?, onSubmit: (String) -> Unit, onBack: () -> Unit) {
    var value by remember { mutableStateOf("") }

    Column {
        Text("Use a recovery code", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        // Stated plainly: this is not what most people expect a recovery code to do.
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
            // Stops a paste of "123 456" being rejected for a reason the user cannot see.
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
        // The routes out: without them a lost authenticator strands this screen, and since
        // the aal2 policy landed, signing out alone would not help either.
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
            // Deliberately not the minimum: this unlocks an existing row, and enforcing it
            // would lock out data encrypted before the rule existed.
            enabled = value.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) { Text("Unlock") }
    }
}

@Composable
private fun Info(title: String, body: String) {
    Column {
        Text(title, style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        Text(
            body,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
