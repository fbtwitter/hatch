package dev.hatch.android

import androidx.compose.foundation.ExperimentalFoundationApi
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
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Delete
import androidx.compose.material.icons.rounded.Edit
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
// Tasks by default, with a folder strip across the top: one tap, or a swipe, to any of them.
//
// My Day is deliberately absent: it is the first bottom-bar tab, permanently on screen, and a
// folder for it would be the one tab pointing where the bar already points.
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
    // False when Lists is a page of the outer 4-tab HorizontalPager (MainActivity.kt) — a
    // per-row horizontal dismiss would fight both that pager and this screen's own folder
    // pager for the same axis. True (the default) is what a standalone caller still gets.
    swipeToDeleteEnabled: Boolean = true,
) {
    val folders = remember(lists) { foldersFor(lists) }

    // A custom list can stop existing while it is selected — deleted from the dialog, or a
    // tombstone arriving on the next pull. This replaces the popBackStack the old pushed
    // route performed, and covers the sync case the same way.
    val selectedIndex = folders.indexOfFirst { it.nav == selectedNav }
    LaunchedEffect(selectedIndex, loaded) {
        if (loaded && selectedIndex < 0) onSelectFolder(NAV_ALL_TASKS)
    }
    val page = selectedIndex.coerceAtLeast(0)

    val pagerState = rememberPagerState(initialPage = page) { folders.size }
    val scope = rememberCoroutineScope()

    // While the tab is open the pager is the source of truth and the ViewModel mirrors it;
    // a selection arriving from outside pushes the pager instead. Splitting it that way is
    // what keeps the two from fighting — see the drop(1) below.
    val uiPage = pagerState.currentPage
    val current = folders.getOrNull(uiPage)

    // Snapped, not animated: arriving on the tab from a Summary tile should already be showing
    // the folder that was asked for, rather than sliding to it from wherever the tab was left.
    LaunchedEffect(page) {
        if (page != pagerState.currentPage) pagerState.scrollToPage(page)
    }

    // drop(1) skips the emission composition itself produces. Without it, re-entering the tab
    // reported the pager's *restored* page and immediately overwrote the folder a Summary tile
    // had just selected — the jump landed on the Lists tab still showing the previous folder.
    LaunchedEffect(pagerState, folders) {
        snapshotFlow { pagerState.isScrollInProgress }
            .drop(1)
            .filter { !it }
            .collect {
                folders.getOrNull(pagerState.currentPage)?.let { onSelectFolder(it.nav) }
            }
    }

    // One filter for the tab, not one per folder: a filter still applied on a folder you
    // cannot see it on would be a trap. Cleared by switching folders.
    val currentNav = current?.nav ?: NAV_ALL_TASKS
    var tagFilter by rememberSaveable(currentNav) { mutableStateOf<String?>(null) }

    // The composer belongs to the tab, not to a page, so it does not slide away under a swipe.
    // Each page reports its own scroll-driven collapse and only the settled one is listened to.
    var composerCollapsed by remember { mutableStateOf(false) }

    val listNames = remember(lists) { lists.associate { it.id to it.name } }
    val editableList = current?.list

    Scaffold(
        topBar = {
            Column {
                // A plain bar, not the collapsing MediumTopAppBar the browse screen used: the
                // folder strip has to stay reachable while the list underneath scrolls, which
                // is the whole point of it.
                TopAppBar(
                    title = { Text("Lists") },
                    actions = {
                        IconButton(onClick = onOpenSearch) {
                            Icon(Icons.Rounded.Search, contentDescription = "Search")
                        }
                        FolderMenu(
                            list = editableList,
                            onCreateList = onCreateList,
                            onEditList = onEditList,
                        )
                    },
                )
                PrimaryScrollableTabRow(
                    selectedTabIndex = uiPage,
                    edgePadding = 8.dp,
                    divider = {},
                ) {
                    folders.forEachIndexed { index, folder ->
                        FolderTab(
                            selected = index == uiPage,
                            label = folder.label,
                            count = remember(tasks, folder.nav) { navCount(tasks, folder.nav) },
                            // Moves the pager; the settle collector above is what tells the
                            // ViewModel, so a tap and a swipe end up on the same path.
                            onClick = { scope.launch { pagerState.animateScrollToPage(index) } },
                            // Telegram's own gesture for folder options. Duplicated in the
                            // app-bar menu above, because a long press is not reachable for
                            // every input method and list management cannot be gesture-only.
                            onLongClick = folder.list?.let { list -> { onEditList(list) } },
                        )
                    }
                }
                HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                tagFilter?.let { tag ->
                    Surface(color = MaterialTheme.colorScheme.surface) {
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
                // Against the folder on screen, so a task can never be filed into a list the
                // tab is not showing.
                onSubmit = { title -> current?.let { onAdd(title, it.nav) } },
            )
        },
    ) { padding ->
        if (!loaded) return@Scaffold

        HorizontalPager(
            state = pagerState,
            // Default (0): only the folder on screen, plus whatever a swipe is dragging in,
            // is composed — twenty lists do not mean twenty live LazyColumns.
            key = { folders[it].nav },
            modifier = Modifier.fillMaxSize(),
        ) { index ->
            val folder = folders[index]
            // Per page, so each folder keeps its own scroll position across a swipe.
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
                // No collapsing bar to fight over the same downward drag here, so a pull at
                // the top is always a pull.
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
        }
    }
}

// Order is fixed: All Tasks first and default, then the two other smart lists, then the custom
// lists in the order they already sort in — pinned first, then sortOrder.
internal class Folder(val nav: String, val label: String, val list: TaskList? = null)

private fun foldersFor(lists: List<TaskList>): List<Folder> = buildList {
    add(Folder(NAV_ALL_TASKS, "All Tasks"))
    add(Folder(NAV_IMPORTANT, "Important"))
    add(Folder(NAV_PLANNED, "Planned"))
    lists.sortedWith(compareByDescending<TaskList> { it.isPinned }.thenBy { it.sortOrder })
        .forEach { add(Folder(it.id, it.name, it)) }
}

// Not Material's Tab: that one owns its own click, and a folder needs a long press as well.
// The tab row's indicator positions itself from the children's measured widths either way.
@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun FolderTab(
    selected: Boolean,
    label: String,
    count: Int,
    onClick: () -> Unit,
    onLongClick: (() -> Unit)?,
) {
    val color =
        if (selected) MaterialTheme.colorScheme.primary
        else MaterialTheme.colorScheme.onSurfaceVariant
    Row(
        Modifier
            .height(48.dp)
            .combinedClickable(onClick = onClick, onLongClick = onLongClick)
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

// The keyboard- and screen-reader-reachable half of list management: everything the long press
// on a tab offers, plus creating one.
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
