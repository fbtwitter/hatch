package dev.hatch.android

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.List
import androidx.compose.material.icons.automirrored.rounded.List
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarDuration
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem
import kotlin.math.roundToInt
import kotlinx.coroutines.launch

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
            val appearance by vm.state.collectAsState()
            HatchTheme(appearance.themeMode, appearance.useDynamicColor) {
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

// Route names, not an enum: Navigation-Compose keys its back stack by route string. There is
// no per-list route — which list the Lists tab shows is a folder selection inside that tab,
// not a destination, so nothing can push a second copy of it onto the back stack. My Day,
// Lists, Summary and Settings are not routes at all any more — they are pages of the
// HorizontalPager Routes.Home hosts, matching how Lists' own folder strip already pages
// between folders. Only screens actually pushed on top of that — Search, Sync — stay real
// NavHost destinations.
private object Routes {
    const val Home = "home"
    const val Sync = "settings/sync"
    const val Search = "search"
}

// Page indices into the Home pager, in bottom-bar order.
private const val PageMyDay = 0
private const val PageLists = 1
private const val PageSummary = 2
private const val PageSettings = 3
private const val PageCount = 4

@Composable
private fun HatchApp(vm: CompanionViewModel = viewModel()) {
    val state by vm.state.collectAsState()
    val selectedFolder by vm.selectedFolder.collectAsState()
    // The app opens on My Day and never asks who you are — sync is opt-in.
    val navController = rememberNavController()
    val currentRoute = navController.currentBackStackEntryAsState().value?.destination?.route
    val snackbar = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()

    // Drives the bottom bar and the pager together — real WhatsApp/Telegram-style tab paging,
    // replacing the earlier hand-built peek-and-commit gesture (SwipePeek.kt, now removed): a
    // real HorizontalPager tracks the finger continuously, natively supports both directions,
    // and now covers all four tabs, not just the two the hand-built version singled out.
    val pagerState = rememberPagerState(initialPage = PageMyDay) { PageCount }

    // Held here rather than per-screen: the detail sheet is a modal over the whole app, and
    // three screens open it. While each screen owned its own copy, Summary had to hand the
    // task list an id through the back stack and hope the collector on the other side was
    // still alive — it was not (b00761a).
    val editingId = rememberSaveable { mutableStateOf<String?>(null) }
    val openTask: (TodoItem) -> Unit = remember { { task -> editingId.value = task.id } }

    // A nullable TaskList alone could not tell "closed" from "creating".
    var listDialog by remember { mutableStateOf<ListDialog?>(null) }

    // A tab tap (or a cross-tab pager drag committing) always has to land on Home first —
    // Search and Sync are pushed on top of it, not peers of it, so there may be nothing to
    // animate the pager onto until whatever's on top is popped back off.
    fun goToPage(page: Int) {
        if (navController.currentBackStackEntry?.destination?.route != Routes.Home) {
            navController.popBackStack(Routes.Home, false)
        }
        scope.launch { pagerState.animateScrollToPage(page) }
    }

    // Opening a list always means the Lists tab, wherever it was asked for — a Summary tile
    // that pushed a list onto the Summary tab would leave Summary highlighted while showing
    // Planned. Selecting the folder before switching tabs means the tab arrives already
    // showing it, with nothing extra on the back stack.
    fun openList(nav: String) {
        if (nav == NAV_MY_DAY) {
            goToPage(PageMyDay)
            return
        }
        vm.selectFolder(nav)
        goToPage(PageLists)
    }

    // One deletion path for the swipe, the search results and the sheet's Delete button. The
    // sheet used to call the ViewModel directly, so the most deliberate delete in the app —
    // the one that propagates to every synced device — was the only one with no way back.
    //
    // remember, matching openTask above: `state` is read at the top of this composable, so
    // HatchApp recomposes on every task edit, toggle and sync pull. An un-hoisted lambda here
    // was a fresh instance every one of those times, and passing it down as a TaskRow/
    // SuggestionRow parameter made every visible row on My Day look "changed" to Compose on
    // essentially every data change — the exact whole-list-invalidation failure mode
    // ListAdapter/DiffUtil exist to avoid on RecyclerView, just reached from the other
    // direction. vm/scope/snackbar are all stable across recomposition, so this only needs to
    // be built once.
    val deleteWithUndo: (TodoItem) -> Unit = remember {
        { task ->
            vm.deleteTask(task)
            scope.launch {
                // Long, not Short: four seconds to notice a destructive action and reach the
                // button is not enough, and the delete now propagates to every synced device.
                val result = snackbar.showSnackbar(
                    message = "Deleted \"${task.title.take(30)}\"",
                    actionLabel = "Undo",
                    duration = SnackbarDuration.Long,
                )
                if (result == SnackbarResult.ActionPerformed) vm.restoreTask(task)
            }
        }
    }

    // Same reasoning as deleteWithUndo above — these three used to be built inline at each
    // TaskListScreen call site, which recreated them on every HatchApp recomposition too.
    val openSearch: () -> Unit = remember { { navController.navigate(Routes.Search) } }
    val addTaskToMyDay: (String) -> String? = remember { { title -> vm.addTask(title, NAV_MY_DAY) } }
    val addSuggestionToMyDay: (TodoItem) -> Unit = remember { { task -> vm.setMyDay(task, true) } }
    // Shared with the Lists tab below too — same bug, same fix, one instance either way.
    val addTask: (String, String) -> String? = remember { { title, nav -> vm.addTask(title, nav) } }

    // Stopped on pause so a backgrounded app holds no timer.
    LifecycleResumeEffect(Unit) {
        vm.startAutoPull()
        onPauseOrDispose { vm.stopAutoPull() }
    }

    // A fetch, not a setting: on success get out of the way. Only from the Sync screen,
    // though — a pull-to-refresh on a list is already where the user wants to be, and
    // yanking them to another tab for it would be the opposite of getting out of the way.
    LaunchedEffect(Unit) {
        vm.pullCompleted.collect { count ->
            if (navController.currentBackStackEntry?.destination?.route == Routes.Sync) {
                goToPage(PageMyDay)
            }
            snackbar.showSnackbar("Pulled — $count task${if (count == 1) "" else "s"}")
        }
    }

    // Push does not navigate away: nothing on the task list changes as a result.
    LaunchedEffect(Unit) {
        vm.pushCompleted.collect { count ->
            snackbar.showSnackbar("Pushed — $count task${if (count == 1) "" else "s"} encrypted and sent")
        }
    }

    LaunchedEffect(Unit) {
        vm.exportFinished.collect { message -> snackbar.showSnackbar(message) }
    }

    // A pull-to-refresh that ends badly would otherwise just stop its spinner with no
    // explanation. Gated on the previous state being Working so only a deliberate operation
    // reports here — a failed background push (previous state On) stays silent, or a flaky
    // network would raise a snackbar on every edit made offline. Also gated on Settings and
    // Sync not being what's showing: both already report the state directly, in more detail.
    //
    // The route is read from the controller rather than from `currentRoute`, which is a plain
    // val captured once when this effect was launched: it was null on that first composition,
    // so this snackbar could never fire at all.
    LaunchedEffect(Unit) {
        var previous: SyncState? = null
        snapshotFlow { state.sync }.collect { sync ->
            val wasWorking = previous is SyncState.Working
            previous = sync
            val route = navController.currentBackStackEntry?.destination?.route
            val onSettingsPage = route == Routes.Home && pagerState.currentPage == PageSettings
            if (!wasWorking || route == Routes.Sync || onSettingsPage) return@collect
            when (sync) {
                is SyncState.Failed -> snackbar.showSnackbar(sync.message)
                SyncState.NeedsPassphrase, SyncState.WrongPassphrase, is SyncState.NeedsMfaCode ->
                    snackbar.showSnackbar("Sync needs your attention — open Settings → Sync")
                else -> Unit
            }
        }
    }

    // Built once here rather than inline in the pager below, matching the earlier lambda-
    // hoisting fix for the same reason: an inline lambda would be rebuilt on every HatchApp
    // recomposition (every task edit, toggle, sync pull) and passed to the pager as a fresh
    // instance each time.
    val myDayContent: @Composable () -> Unit = {
        TaskListScreen(
            nav = NAV_MY_DAY,
            tasks = state.tasks,
            lists = state.lists,
            loaded = state.loaded,
            // Working included so the box stays mounted for the pull it is
            // spinning for — On alone would unmount it the moment a pull began.
            refreshEnabled = state.sync is SyncState.On || state.sync is SyncState.Working,
            refreshing = state.sync is SyncState.Working,
            snackbar = snackbar,
            onOpenSearch = openSearch,
            onRefresh = vm::refresh,
            onAdd = addTaskToMyDay,
            onToggle = vm::toggleComplete,
            onOpen = openTask,
            onDelete = deleteWithUndo,
            onAddToMyDay = addSuggestionToMyDay,
            // The outer pager owns horizontal swipe on every page now — a per-row dismiss
            // gesture would fight it for the same axis on every page, not just this one.
            swipeToDeleteEnabled = false,
        )
    }
    val listsContent: @Composable () -> Unit = {
        ListsScreen(
            selectedNav = selectedFolder,
            tasks = state.tasks,
            lists = state.lists,
            loaded = state.loaded,
            refreshEnabled = state.sync is SyncState.On || state.sync is SyncState.Working,
            refreshing = state.sync is SyncState.Working,
            snackbar = snackbar,
            onSelectFolder = vm::selectFolder,
            onOpenSearch = openSearch,
            onRefresh = vm::refresh,
            onAdd = addTask,
            onToggle = vm::toggleComplete,
            onOpen = openTask,
            onDelete = deleteWithUndo,
            onCreateList = { listDialog = ListDialog.New },
            onEditList = { listDialog = ListDialog.Edit(it) },
            // Same reasoning as My Day: Lists' own folder pager already competes with the
            // outer one for the same axis, so a per-row dismiss on top would be a third
            // competitor. Delete still works via the detail sheet and search results.
            swipeToDeleteEnabled = false,
        )
    }
    val summaryContent: @Composable () -> Unit = {
        SummaryScreen(
            tasks = state.tasks,
            lists = state.lists,
            onNavigateToList = ::openList,
            onOpenTask = openTask,
        )
    }
    val settingsContent: @Composable () -> Unit = {
        SettingsScreen(
            themeMode = state.themeMode,
            useDynamicColor = state.useDynamicColor,
            sync = state.sync,
            snackbar = snackbar,
            onThemeMode = vm::setThemeMode,
            onDynamicColor = vm::setDynamicColor,
            onOpenSync = { navController.navigate(Routes.Sync) },
            onExport = vm::exportTo,
        )
    }

    // The page a live drag is currently closest to, not just the settled one — the bottom
    // bar's highlight tracks this so it moves with the finger the same way the pager's own
    // content does, instead of only snapping once the gesture ends.
    val livePage by remember {
        derivedStateOf {
            (pagerState.currentPage + pagerState.currentPageOffsetFraction)
                .roundToInt()
                .coerceIn(0, PageCount - 1)
        }
    }

    Box(Modifier.fillMaxSize()) {
        // No manual BackHandler here: once composed inside a NavHost, the system/predictive
        // back gesture is handled by the NavController itself.
        Scaffold(
            // Zeroed on purpose. With the default (systemBars) this Scaffold has no top bar
            // of its own, so it handed the status-bar inset down as content padding — and
            // then every screen's own TopAppBar applied that same inset again underneath it.
            // Every title on every screen was sitting a full status bar too low. The bars
            // that actually need insets apply their own: NavigationBar below, and each
            // screen's TopAppBar above.
            contentWindowInsets = WindowInsets(0, 0, 0, 0),
            bottomBar = {
                // Absent on search, which is a full-screen field rather than a peer of the
                // tabs — leaving the bar up there with nothing highlighted is the exact
                // state this navigation rework exists to remove.
                if (currentRoute != Routes.Search) {
                    NavigationBar {
                        // Filled when selected, outlined when not: the icon swap is half of
                        // what makes a Material bottom bar readable at a glance, and without
                        // it the pill indicator is doing the work alone. Selected reads off
                        // livePage, not pagerState.currentPage directly, so the highlight
                        // moves with a drag in progress instead of only snapping at the end.
                        NavTab(
                            selected = currentRoute == Routes.Home && livePage == PageMyDay,
                            filled = Icons.Rounded.CheckCircle,
                            outlined = Icons.Outlined.CheckCircle,
                            label = "My Day",
                            onClick = { goToPage(PageMyDay) },
                        )
                        NavTab(
                            selected = currentRoute == Routes.Home && livePage == PageLists,
                            filled = Icons.AutoMirrored.Rounded.List,
                            outlined = Icons.AutoMirrored.Outlined.List,
                            label = "Lists",
                            onClick = { goToPage(PageLists) },
                        )
                        NavTab(
                            selected = currentRoute == Routes.Home && livePage == PageSummary,
                            filled = HatchIcons.SummaryFilled,
                            outlined = HatchIcons.SummaryOutlined,
                            label = "Summary",
                            onClick = { goToPage(PageSummary) },
                        )
                        NavTab(
                            selected = (currentRoute == Routes.Home && livePage == PageSettings) ||
                                currentRoute == Routes.Sync,
                            filled = Icons.Rounded.Settings,
                            outlined = Icons.Outlined.Settings,
                            label = "Settings",
                            onClick = { goToPage(PageSettings) },
                        )
                    }
                }
            },
        ) { outerPadding ->
            // fadeThrough no longer governs tab-to-tab motion — the pager's own drag-through
            // transition replaces it for My Day/Lists/Summary/Settings. It still applies here
            // for Home <-> Search, the one case left where the NavHost-level default is used
            // rather than a composable(...)-level override.
            NavHost(
                navController = navController,
                startDestination = Routes.Home,
                // consumeWindowInsets alongside the padding, not padding alone. This Scaffold
                // lifts its content by the navigation bar's height to clear the bottom bar,
                // but without consuming the matching insets a descendant asking for
                // imePadding() still sees the keyboard measured from the window's own bottom
                // edge — and adds all of it on top of an offset it already has. That is why
                // the composer floated a bar's height above the keyboard instead of resting
                // on it. Consuming makes imePadding() apply only what is left over.
                modifier = Modifier
                    .padding(outerPadding)
                    .consumeWindowInsets(outerPadding),
                enterTransition = { fadeThrough().targetContentEnter },
                exitTransition = { fadeThrough().initialContentExit },
                popEnterTransition = { fadeThrough().targetContentEnter },
                popExitTransition = { fadeThrough().initialContentExit },
            ) {
                composable(Routes.Home) {
                    // beyondViewportPageCount stays at its default of 0: only the visible
                    // page, plus whatever a swipe is actively dragging in, is composed — four
                    // tabs do not mean four live screens running at once.
                    //
                    // userScrollEnabled is off while Lists is the current page: Lists owns its
                    // own HorizontalPager for its folder strip, and nesting two pagers on the
                    // same axis is not a priority dispute Compose resolves for free — the
                    // outer one, being the ancestor, gets first claim on every drag via nested
                    // scroll's pre-scroll pass, so a swipe meant to switch folders instead
                    // always advanced this outer pager to the next tab. Folders are Lists'
                    // own, already-shipped swipe feature, so it keeps full-width swipe there;
                    // this pager still reaches Lists (and leaves it) via the bottom bar, just
                    // not by dragging across Lists' own content.
                    HorizontalPager(
                        state = pagerState,
                        userScrollEnabled = pagerState.currentPage != PageLists,
                        modifier = Modifier.fillMaxSize(),
                    ) { page ->
                        when (page) {
                            PageMyDay -> myDayContent()
                            PageLists -> listsContent()
                            PageSummary -> summaryContent()
                            else -> settingsContent()
                        }
                    }
                }

                composable(
                    Routes.Search,
                    enterTransition = { screenTransition(forward = true).targetContentEnter },
                    exitTransition = { screenTransition(forward = true).initialContentExit },
                    popEnterTransition = { screenTransition(forward = false).targetContentEnter },
                    popExitTransition = { screenTransition(forward = false).initialContentExit },
                ) {
                    SearchScreen(
                        tasks = state.tasks,
                        lists = state.lists,
                        snackbar = snackbar,
                        onBack = { navController.popBackStack() },
                        onToggle = vm::toggleComplete,
                        onOpen = openTask,
                        onDelete = deleteWithUndo,
                    )
                }

                composable(
                    Routes.Sync,
                    enterTransition = { screenTransition(forward = true).targetContentEnter },
                    exitTransition = { screenTransition(forward = true).initialContentExit },
                    popEnterTransition = { screenTransition(forward = false).targetContentEnter },
                    popExitTransition = { screenTransition(forward = false).initialContentExit },
                ) {
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
                        onBack = { navController.popBackStack() },
                        snackbar = snackbar,
                    )
                }
            }
        }

        // Both of these are modals in their own window, so they sit outside the Scaffold
        // rather than inside a destination — and outside the NavHost, so a tab change while
        // one is open does not tear it down mid-animation.
        TaskDetailHost(
            editingId = editingId,
            tasks = state.tasks,
            lists = state.lists,
            onSave = vm::saveTask,
            onDelete = deleteWithUndo,
        )

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

@Composable
private fun RowScope.NavTab(
    selected: Boolean,
    filled: ImageVector,
    outlined: ImageVector,
    label: String,
    onClick: () -> Unit,
) {
    NavigationBarItem(
        selected = selected,
        onClick = onClick,
        // contentDescription is null because the label below is already the accessible name;
        // announcing it twice is how a nav bar ends up reading "Lists, Lists".
        icon = { Icon(if (selected) filled else outlined, contentDescription = null) },
        label = { Text(label) },
    )
}

// Takes the MutableState rather than its value, so reading which task is open happens here
// and not in HatchApp — opening a sheet must not recompose the nav graph.
@Composable
private fun TaskDetailHost(
    editingId: MutableState<String?>,
    tasks: List<TodoItem>,
    lists: List<TaskList>,
    onSave: (TodoItem) -> Unit,
    onDelete: (TodoItem) -> Unit,
) {
    // By id, not by value: a save replaces the instance in the list.
    val editing = editingId.value?.let { id -> tasks.firstOrNull { it.id == id } } ?: return

    TaskDetailSheet(
        task = editing,
        lists = lists,
        onSave = onSave,
        onDelete = onDelete,
        onDismiss = { editingId.value = null },
    )
}

private sealed interface ListDialog {
    data object New : ListDialog
    data class Edit(val list: TaskList) : ListDialog
}
