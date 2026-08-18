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
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem
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

// Route names, not an enum: Navigation-Compose keys its back stack by route string. `List`
// is the pattern rather than a filled-in route — which is also what NavDestination.route
// reports, so the bottom bar can match on it without parsing the argument back out.
private object Routes {
    const val MyDay = "myday"
    const val Lists = "lists"
    const val List = "list/{nav}"
    const val Summary = "summary"
    const val Settings = "settings"
    const val Sync = "settings/sync"
    const val Search = "search"

    const val NavArg = "nav"

    fun list(nav: String) = "list/$nav"
}

@Composable
private fun HatchApp(vm: CompanionViewModel = viewModel()) {
    val state by vm.state.collectAsState()
    // The app opens on My Day and never asks who you are — sync is opt-in.
    val navController = rememberNavController()
    val currentRoute = navController.currentBackStackEntryAsState().value?.destination?.route
    val snackbar = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()

    // Held here rather than per-screen: the detail sheet is a modal over the whole app, and
    // three screens open it. While each screen owned its own copy, Summary had to hand the
    // task list an id through the back stack and hope the collector on the other side was
    // still alive — it was not (b00761a).
    val editingId = rememberSaveable { mutableStateOf<String?>(null) }
    val openTask: (TodoItem) -> Unit = remember { { task -> editingId.value = task.id } }

    // A nullable TaskList alone could not tell "closed" from "creating".
    var listDialog by remember { mutableStateOf<ListDialog?>(null) }

    // The Google-recommended bottom-nav pattern: peer destinations save/restore each other's
    // state and never stack on top of one another, so back from any tab goes straight to the
    // start destination instead of walking tab-visit history.
    fun goToTab(route: String) = navController.navigate(route) {
        popUpTo(navController.graph.findStartDestination().id) { saveState = true }
        launchSingleTop = true
        restoreState = true
    }

    // Opening a list always means the Lists tab, wherever it was asked for — a Summary tile
    // that pushed a list onto the Summary tab would leave Summary highlighted while showing
    // Planned. popUpTo keeps that tab one list deep instead of stacking them.
    fun openList(nav: String) {
        if (nav == NAV_MY_DAY) {
            goToTab(Routes.MyDay)
            return
        }
        goToTab(Routes.Lists)
        navController.navigate(Routes.list(nav)) {
            popUpTo(Routes.Lists)
            launchSingleTop = true
        }
    }

    // One deletion path for the swipe, the search results and the sheet's Delete button. The
    // sheet used to call the ViewModel directly, so the most deliberate delete in the app —
    // the one that propagates to every synced device — was the only one with no way back.
    val deleteWithUndo: (TodoItem) -> Unit = { task ->
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
                goToTab(Routes.MyDay)
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
            if (!wasWorking || route == Routes.Sync || route == Routes.Settings) return@collect
            when (sync) {
                is SyncState.Failed -> snackbar.showSnackbar(sync.message)
                SyncState.NeedsPassphrase, SyncState.WrongPassphrase, is SyncState.NeedsMfaCode ->
                    snackbar.showSnackbar("Sync needs your attention — open Settings → Sync")
                else -> Unit
            }
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
                        // it the pill indicator is doing the work alone.
                        NavTab(
                            selected = currentRoute == Routes.MyDay,
                            filled = Icons.Rounded.CheckCircle,
                            outlined = Icons.Outlined.CheckCircle,
                            label = "My Day",
                            onClick = { goToTab(Routes.MyDay) },
                        )
                        NavTab(
                            // The pattern, so every list opened inside this tab keeps it lit.
                            selected = currentRoute == Routes.Lists || currentRoute == Routes.List,
                            filled = Icons.AutoMirrored.Rounded.List,
                            outlined = Icons.AutoMirrored.Outlined.List,
                            label = "Lists",
                            onClick = { goToTab(Routes.Lists) },
                        )
                        NavTab(
                            selected = currentRoute == Routes.Summary,
                            filled = HatchIcons.SummaryFilled,
                            outlined = HatchIcons.SummaryOutlined,
                            label = "Summary",
                            onClick = { goToTab(Routes.Summary) },
                        )
                        NavTab(
                            selected = currentRoute == Routes.Settings || currentRoute == Routes.Sync,
                            filled = Icons.Rounded.Settings,
                            outlined = Icons.Outlined.Settings,
                            label = "Settings",
                            onClick = { goToTab(Routes.Settings) },
                        )
                    }
                }
            },
        ) { outerPadding ->
            // Fade-through between the bottom bar's own peer destinations — Material's
            // guidance for switching bottom-tab siblings — reserving the shared-axis slide in
            // Motion.kt for an actual forward/back step, overridden per-destination below.
            NavHost(
                navController = navController,
                startDestination = Routes.MyDay,
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
                composable(Routes.MyDay) {
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
                        // A tab, so there is nowhere back to go.
                        onBack = null,
                        onOpenSearch = { navController.navigate(Routes.Search) },
                        onRefresh = vm::refresh,
                        onAdd = { title -> vm.addTask(title, NAV_MY_DAY) },
                        onToggle = vm::toggleComplete,
                        onOpen = openTask,
                        onDelete = deleteWithUndo,
                        onAddToMyDay = { task -> vm.setMyDay(task, true) },
                    )
                }

                composable(Routes.Lists) {
                    ListsScreen(
                        tasks = state.tasks,
                        lists = state.lists,
                        onOpenList = ::openList,
                        onOpenSearch = { navController.navigate(Routes.Search) },
                        onCreateList = { listDialog = ListDialog.New },
                        onEditList = { listDialog = ListDialog.Edit(it) },
                    )
                }

                composable(
                    Routes.List,
                    arguments = listOf(navArgument(Routes.NavArg) { type = NavType.StringType }),
                    enterTransition = { screenTransition(forward = true).targetContentEnter },
                    exitTransition = { screenTransition(forward = true).initialContentExit },
                    popEnterTransition = { screenTransition(forward = false).targetContentEnter },
                    popExitTransition = { screenTransition(forward = false).initialContentExit },
                ) { entry ->
                    val nav = entry.arguments?.getString(Routes.NavArg) ?: NAV_ALL_TASKS
                    // A custom list can stop existing while it is open — deleted from the
                    // dialog, or a tombstone arriving on the next pull. Leaving the route up
                    // would show an empty list with the deleted list's name on it.
                    val listGone = state.loaded &&
                        nav !in SmartListNavs &&
                        state.lists.none { it.id == nav }
                    LaunchedEffect(listGone) { if (listGone) navController.popBackStack() }

                    TaskListScreen(
                        nav = nav,
                        tasks = state.tasks,
                        lists = state.lists,
                        loaded = state.loaded,
                        refreshEnabled = state.sync is SyncState.On || state.sync is SyncState.Working,
                        refreshing = state.sync is SyncState.Working,
                        snackbar = snackbar,
                        onBack = { navController.popBackStack() },
                        onOpenSearch = { navController.navigate(Routes.Search) },
                        onRefresh = vm::refresh,
                        onAdd = { title -> vm.addTask(title, nav) },
                        onToggle = vm::toggleComplete,
                        onOpen = openTask,
                        onDelete = deleteWithUndo,
                        onAddToMyDay = { task -> vm.setMyDay(task, true) },
                    )
                }

                composable(Routes.Summary) {
                    SummaryScreen(
                        tasks = state.tasks,
                        lists = state.lists,
                        onNavigateToList = ::openList,
                        onOpenTask = openTask,
                    )
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

                composable(Routes.Settings) {
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

// Every nav tag that is not a list id — the set a `list/{nav}` route may hold without any
// TaskList existing for it.
private val SmartListNavs = setOf(NAV_MY_DAY, NAV_IMPORTANT, NAV_PLANNED, NAV_ALL_TASKS)

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
