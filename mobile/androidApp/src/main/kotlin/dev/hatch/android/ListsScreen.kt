package dev.hatch.android

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.automirrored.rounded.List
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Delete
import androidx.compose.material.icons.rounded.Edit
import androidx.compose.material.icons.rounded.Menu
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.PrimaryScrollableTabRow
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.launch

// The Lists tab, as folders rather than a menu. It used to be a browse screen where every row
// pushed a `list/{nav}` destination, so seeing a list was two taps and switching between two
// lists was four — out to the menu and back in. Now the tab *is* a task list, showing All
// Tasks by default, with a folder strip across the top: one tap to any smart list.
//
// Tap-only, deliberately not swipeable: the outer bottom-nav pager (MainActivity.kt) already
// owns the one gesture for "move to a different part of the app" — the WhatsApp-style swipe
// between My Day/Lists/Summary/Settings — and this strip is a smaller, secondary distinction
// (filtering the same list) where losing swipe costs little, since it is three short tabs, easy
// to tap. Splitting it this way also means only one pager on this screen is ever swipeable, so
// there is nothing left here to fight the outer pager for the same horizontal drag.
//
// Custom lists live behind a drawer reachable only from this tab (the hamburger in its top
// bar), also tap-only — never an edge swipe, which would collide with that same outer pager's
// drag. Not folder-strip tabs either: the strip needs a fixed set of children for its selection
// indicator, and letting a swipe (or a tap) wander from Planned into an arbitrary custom list
// would reproduce the exact "no tab highlighted" state the drawer-based nav was rewritten to
// fix. Picking a custom list from the drawer instead opens a small in-tab detail view — its own
// back arrow, no folder strip — so the pager itself only ever holds the three smart lists.
//
// The drawer itself is owned and drawn by MainActivity.kt, not this screen: it wraps the whole
// app, bottom nav bar included, so its scrim sits above every tab and a tap meant for the
// drawer can never land on a nav item underneath. This screen only asks for it to open, via
// `onOpenDrawer`.
//
// My Day is deliberately absent from the strip: it is the first bottom-bar tab, permanently on
// screen, and a folder for it would be the one tab pointing where the bar already points.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ListsScreen(
    selectedNav: String,
    tasks: List<TodoItem>,
    lists: List<TaskList>,
    loaded: Boolean,
    refreshEnabled: Boolean,
    refreshing: Boolean,
    snackbar: SnackbarHostState,
    onSelectFolder: (String) -> Unit,
    onOpenSearch: () -> Unit,
    onRefresh: () -> Unit,
    onAdd: (String, String) -> String?,
    onToggle: (TodoItem) -> Unit,
    onOpen: (TodoItem) -> Unit,
    onDelete: (TodoItem) -> Unit,
    onCreateList: () -> Unit,
    onEditList: (TaskList) -> Unit,
    // Opens the app-level drawer in MainActivity.kt — see the file comment above for why it
    // is not owned here.
    onOpenDrawer: () -> Unit,
    // False when Lists is a page of the outer 4-tab HorizontalPager (MainActivity.kt) — a
    // per-row horizontal dismiss would fight both that pager and this screen's own folder
    // pager for the same axis. True (the default) is what a standalone caller still gets.
    swipeToDeleteEnabled: Boolean = true,
) {
    val smartFolders = remember { smartFolders() }
    val customLists = remember(lists) { sortedCustomLists(lists) }
    val selectedCustomList = customLists.firstOrNull { it.id == selectedNav }

    // What the drawer's back arrow returns to — the last smart folder shown, so closing a
    // drawer list is a pop, not a reset to All Tasks.
    var lastSmartNav by rememberSaveable { mutableStateOf(NAV_ALL_TASKS) }
    LaunchedEffect(selectedNav) {
        if (smartFolders.any { it.nav == selectedNav }) lastSmartNav = selectedNav
    }

    // A selected custom list can stop existing while it is open — deleted from the dialog, or
    // a tombstone arriving on the next sync pull. Falls back the same way the old pushed route
    // used to.
    LaunchedEffect(selectedNav, customLists, loaded) {
        val isSmart = smartFolders.any { it.nav == selectedNav }
        if (loaded && !isSmart && customLists.none { it.id == selectedNav }) {
            onSelectFolder(lastSmartNav)
        }
    }

    val selectedSmartIndex = smartFolders.indexOfFirst { it.nav == selectedNav }.coerceAtLeast(0)
    val pagerState = rememberPagerState(initialPage = selectedSmartIndex) { smartFolders.size }
    val scope = rememberCoroutineScope()

    val uiPage = pagerState.currentPage
    val currentSmart = smartFolders.getOrNull(uiPage)

    // Snapped, not animated: arriving on the tab from a Summary tile should already be showing
    // the folder that was asked for, rather than sliding to it from wherever the tab was left.
    LaunchedEffect(selectedSmartIndex, selectedCustomList) {
        if (selectedCustomList == null && selectedSmartIndex != pagerState.currentPage) {
            pagerState.scrollToPage(selectedSmartIndex)
        }
    }

    // drop(1) skips the emission composition itself produces. Without it, re-entering the tab
    // reported the pager's *restored* page and immediately overwrote the folder a Summary tile
    // had just selected — the jump landed on the Lists tab still showing the previous folder.
    LaunchedEffect(pagerState) {
        snapshotFlow { pagerState.isScrollInProgress }
            .drop(1)
            .filter { !it }
            .collect {
                smartFolders.getOrNull(pagerState.currentPage)?.let { onSelectFolder(it.nav) }
            }
    }

    // Device back, while a drawer list is open, pops back to folder browsing rather than
    // leaving the tab — matching how Search and Sync already treat their own pushed state.
    BackHandler(enabled = selectedCustomList != null) { onSelectFolder(lastSmartNav) }

    // One filter for the tab, not one per folder: a filter still applied on a folder you
    // cannot see it on would be a trap. Cleared by switching folders.
    val currentNav = selectedCustomList?.id ?: (currentSmart?.nav ?: NAV_ALL_TASKS)
    var tagFilter by rememberSaveable(currentNav) { mutableStateOf<String?>(null) }

    // Hoisted rather than computed inside the body below: the app bar's own title needs the
    // same count, matching how TaskScreen's My Day title shows nav name + count together.
    val customListModel = selectedCustomList?.let { rememberTaskListModel(it.id, tasks, tagFilter) }

    // The composer belongs to the tab, not to a page, so it does not slide away under a swipe.
    // Each page reports its own scroll-driven collapse and only the settled/current one feeds
    // this shared flag.
    var composerCollapsed by remember { mutableStateOf(false) }

    val listNames = remember(lists) { lists.associate { it.id to it.name } }

    Scaffold(
        topBar = {
            Column {
                // A plain bar, not the collapsing MediumTopAppBar the browse screen used:
                // the folder strip has to stay reachable while the list underneath scrolls,
                // which is the whole point of it.
                TopAppBar(
                    title = {
                        if (selectedCustomList == null || customListModel == null) {
                            Text("Lists")
                        } else {
                            // Name over count, the same pairing My Day's own app bar uses — a
                            // custom list opened from the drawer is its own focused screen,
                            // not a folder tab, so it earns the same treatment.
                            //
                            // The tag-filter pill deliberately does *not* replace the count
                            // here. A TopAppBar title slot is 64dp and this two-line title
                            // already fills it; swapping in a pill overflowed the slot, which
                            // pushed the name up under the status bar and dropped the pill
                            // onto the navigation icon's row. It lives in its own full-width
                            // row below instead, shared with the folder pages.
                            Column {
                                Text(selectedCustomList.name)
                                Text(
                                    customListModel.countLabel,
                                    style = MaterialTheme.typography.labelMedium,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                        }
                    },
                    navigationIcon = {
                        if (selectedCustomList != null) {
                            IconButton(onClick = { onSelectFolder(lastSmartNav) }) {
                                Icon(
                                    Icons.AutoMirrored.Rounded.ArrowBack,
                                    contentDescription = "Back to folders",
                                )
                            }
                        } else {
                            IconButton(onClick = onOpenDrawer) {
                                Icon(Icons.Rounded.Menu, contentDescription = "Lists")
                            }
                        }
                    },
                    actions = {
                        IconButton(onClick = onOpenSearch) {
                            Icon(Icons.Rounded.Search, contentDescription = "Search")
                        }
                        FolderMenu(
                            list = selectedCustomList,
                            onCreateList = onCreateList,
                            onEditList = onEditList,
                        )
                    },
                )
                if (selectedCustomList == null) {
                    PrimaryScrollableTabRow(
                        selectedTabIndex = uiPage,
                        edgePadding = 8.dp,
                        divider = {},
                    ) {
                        smartFolders.forEachIndexed { index, folder ->
                            FolderTab(
                                selected = index == uiPage,
                                label = folder.label,
                                count = remember(tasks, folder.nav) { navCount(tasks, folder.nav) },
                                // Moves the pager; the settle collector above is what tells
                                // the ViewModel, so a tap and a swipe end up on the same path.
                                onClick = { scope.launch { pagerState.animateScrollToPage(index) } },
                            )
                        }
                    }
                }
                HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                // fillMaxWidth is load-bearing, not cosmetic: this sits in the Scaffold's
                // topBar, and the list below scrolls *under* that whole region. A Surface
                // sized to its content left the rest of the row transparent, so rows slid
                // past in the gap beside the pill and the pill read as a box floating over
                // the list. Used by folder pages and by a drawer-opened custom list alike.
                tagFilter?.let { tag ->
                    Surface(
                        Modifier.fillMaxWidth(),
                        color = MaterialTheme.colorScheme.surface,
                    ) {
                        Box(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
                            TagFilterPill(tag) { tagFilter = null }
                        }
                    }
                }
            }
        },
        bottomBar = {
            ComposerBar(
                collapsed = composerCollapsed,
                snackbar = snackbar,
                // Against the folder on screen, so a task can never be filed into a list
                // the tab is not showing.
                onSubmit = { title -> onAdd(title, currentNav) },
            )
        },
    ) { padding ->
        if (!loaded) return@Scaffold

        if (selectedCustomList != null && customListModel != null) {
            val listState = rememberLazyListState()
            val collapsed = rememberComposerCollapsed(listState)
            LaunchedEffect(collapsed) { composerCollapsed = collapsed }

            TaskListBody(
                nav = selectedCustomList.id,
                model = customListModel,
                anyTasks = tasks.isNotEmpty(),
                listNames = listNames,
                listState = listState,
                tagFilter = tagFilter,
                padding = padding,
                refreshEnabled = refreshEnabled,
                refreshing = refreshing,
                // No collapsing bar and no pager here — a pull at the top is always a pull.
                pullEnabled = true,
                onRefresh = onRefresh,
                onToggle = onToggle,
                onOpen = onOpen,
                onDelete = onDelete,
                onTagClick = { tag -> tagFilter = tag },
                // My Day is not a folder, so nothing here can offer "add to My Day".
                onAddToMyDay = {},
                swipeToDeleteEnabled = swipeToDeleteEnabled,
            )
        } else {
            HorizontalPager(
                state = pagerState,
                // Tap-only: this strip is a secondary, same-list filter (3 short tabs, easy
                // to tap), not the "move to a different part of the app" gesture — that one
                // belongs to the outer bottom-nav pager (MainActivity.kt), which now stays
                // swipeable everywhere, including from Lists, because there is nothing here
                // left to compete with it for the same drag.
                userScrollEnabled = false,
                // Default (0): only the folder on screen is composed.
                key = { smartFolders[it].nav },
                modifier = Modifier.fillMaxSize(),
            ) { index ->
                val folder = smartFolders[index]
                // Per page, so each folder keeps its own scroll position across a tab tap.
                val listState = rememberLazyListState()
                val collapsed = rememberComposerCollapsed(listState)
                val isCurrent = index == pagerState.currentPage
                LaunchedEffect(collapsed, isCurrent) {
                    if (isCurrent) composerCollapsed = collapsed
                }

                TaskListBody(
                    nav = folder.nav,
                    model = rememberTaskListModel(folder.nav, tasks, tagFilter.takeIf { isCurrent }),
                    anyTasks = tasks.isNotEmpty(),
                    listNames = listNames,
                    listState = listState,
                    tagFilter = tagFilter.takeIf { isCurrent },
                    padding = padding,
                    refreshEnabled = refreshEnabled,
                    refreshing = refreshing,
                    // No collapsing bar to fight over the same downward drag here, so a
                    // pull at the top is always a pull.
                    pullEnabled = true,
                    onRefresh = onRefresh,
                    onToggle = onToggle,
                    onOpen = onOpen,
                    onDelete = onDelete,
                    onTagClick = { tag -> tagFilter = tag },
                    onAddToMyDay = {},
                    swipeToDeleteEnabled = swipeToDeleteEnabled,
                )
            }
        }
    }
}

// Order is fixed: All Tasks first and default, then the two other smart lists. Custom lists are
// no longer folders — see the drawer in ListsScreen above.
internal class Folder(val nav: String, val label: String)

private fun smartFolders(): List<Folder> = listOf(
    Folder(NAV_ALL_TASKS, "All Tasks"),
    Folder(NAV_IMPORTANT, "Important"),
    Folder(NAV_PLANNED, "Planned"),
)

// Pinned first, then sortOrder — shared by this screen (to find the selected custom list) and
// MainActivity.kt's app-level drawer content, so the two never drift apart.
internal fun sortedCustomLists(lists: List<TaskList>): List<TaskList> =
    lists.sortedWith(compareByDescending<TaskList> { it.isPinned }.thenBy { it.sortOrder })

// Not Material's Tab: that one draws its own label and leaves no room for the count beside it.
// The tab row's indicator positions itself from the children's measured widths either way.
@Composable
private fun FolderTab(
    selected: Boolean,
    label: String,
    count: Int,
    onClick: () -> Unit,
) {
    val color =
        if (selected) MaterialTheme.colorScheme.primary
        else MaterialTheme.colorScheme.onSurfaceVariant
    Row(
        Modifier
            .height(48.dp)
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            label,
            style = MaterialTheme.typography.titleSmall,
            color = color,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        // A plain number, not a Badge: Material's badge is an error colour by default, so a
        // list with four things to do rendered the same red dot an app uses to say something
        // is wrong. Open only — a count including completed tasks would never go down.
        if (count > 0) {
            Spacer(Modifier.width(6.dp))
            Text(
                "$count",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

// A drawer row for one custom list — same plain-number-not-a-Badge count as FolderTab, and the
// same long-press-for-options gesture as the folder strip used to offer its own tabs. Used from
// MainActivity.kt's app-level drawer, not just this file — see the ListsScreen doc comment.
@OptIn(ExperimentalFoundationApi::class)
@Composable
internal fun DrawerListRow(
    list: TaskList,
    count: Int,
    selected: Boolean,
    onClick: () -> Unit,
    onLongClick: () -> Unit,
) {
    val color =
        if (selected) MaterialTheme.colorScheme.primary
        else MaterialTheme.colorScheme.onSurface
    Row(
        Modifier
            .fillMaxWidth()
            .combinedClickable(onClick = onClick, onLongClick = onLongClick)
            .padding(horizontal = 28.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(
            Icons.AutoMirrored.Rounded.List,
            contentDescription = null,
            tint = color,
            modifier = Modifier.size(20.dp),
        )
        Spacer(Modifier.width(16.dp))
        Text(
            list.name,
            style = MaterialTheme.typography.bodyLarge,
            color = color,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        if (count > 0) {
            Spacer(Modifier.width(8.dp))
            Text(
                "$count",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

// The keyboard- and screen-reader-reachable half of list management: everything the long press
// on a drawer row offers, plus creating one.
@Composable
private fun FolderMenu(
    list: TaskList?,
    onCreateList: () -> Unit,
    onEditList: (TaskList) -> Unit,
) {
    var open by remember { mutableStateOf(false) }
    IconButton(onClick = { open = true }) {
        Icon(Icons.Rounded.MoreVert, contentDescription = "List options")
    }
    DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
        DropdownMenuItem(
            text = { Text("New list") },
            leadingIcon = { Icon(Icons.Rounded.Add, contentDescription = null) },
            onClick = { open = false; onCreateList() },
        )
        if (list != null) {
            DropdownMenuItem(
                text = { Text("Edit \"${list.name}\"") },
                leadingIcon = { Icon(Icons.Rounded.Edit, contentDescription = null) },
                onClick = { open = false; onEditList(list) },
            )
        }
    }
}

@Composable
fun ListEditorDialog(
    existing: TaskList?,
    onCreate: (String) -> Unit,
    onRename: (TaskList, String) -> Unit,
    onTogglePin: (TaskList) -> Unit,
    onDelete: (TaskList) -> Unit,
    onDismiss: () -> Unit,
) {
    var name by remember(existing?.id) { mutableStateOf(existing?.name.orEmpty()) }
    var confirmingDelete by remember(existing?.id) { mutableStateOf(false) }

    if (confirmingDelete && existing != null) {
        AlertDialog(
            onDismissRequest = { confirmingDelete = false },
            icon = {
                Icon(
                    Icons.Rounded.Delete,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.error,
                )
            },
            title = { Text("Delete \"${existing.name}\"?") },
            text = {
                Text(
                    "The list and every task in it will be deleted on this phone and on " +
                        "every device you sync with. This cannot be undone."
                )
            },
            confirmButton = {
                TextButton(onClick = { onDelete(existing); onDismiss() }) {
                    Text("Delete", color = MaterialTheme.colorScheme.error)
                }
            },
            dismissButton = {
                TextButton(onClick = { confirmingDelete = false }) { Text("Cancel") }
            },
        )
        return
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(if (existing == null) "New list" else "Edit list") },
        text = {
            Column {
                OutlinedTextField(
                    value = name,
                    onValueChange = { name = it },
                    label = { Text("Name") },
                    singleLine = true,
                    shape = MaterialTheme.shapes.large,
                    modifier = Modifier.fillMaxWidth(),
                )

                if (existing != null) {
                    Spacer(Modifier.height(16.dp))
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        TextButton(onClick = { onTogglePin(existing) }) {
                            Text(if (existing.isPinned) "Unpin" else "Pin to top")
                        }
                        Spacer(Modifier.width(8.dp))
                        TextButton(onClick = { confirmingDelete = true }) {
                            Icon(
                                Icons.Rounded.Delete,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.error,
                                modifier = Modifier.size(18.dp),
                            )
                            Spacer(Modifier.width(4.dp))
                            Text("Delete", color = MaterialTheme.colorScheme.error)
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(
                enabled = name.isNotBlank(),
                onClick = {
                    if (existing == null) {
                        onCreate(name)
                    } else {
                        if (name.trim() != existing.name) onRename(existing, name)
                    }
                    onDismiss()
                },
            ) { Text(if (existing == null) "Create" else "Save") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
