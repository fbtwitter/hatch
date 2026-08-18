package dev.hatch.android

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.List
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.DateRange
import androidx.compose.material.icons.rounded.Delete
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MediumTopAppBar
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem

// The Lists tab: every destination that is not one of the other three tabs. This replaced a
// ModalNavigationDrawer, which put half the app's navigation behind a gesture and left the
// bottom bar with no tab highlighted whenever you were in Important, Planned or a custom
// list — a state Material's bottom-nav spec does not have.
//
// My Day is deliberately absent: it is the first tab, permanently on screen, and listing it
// again here would be the one row that navigates somewhere the bar already points at.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ListsScreen(
    tasks: List<TodoItem>,
    lists: List<TaskList>,
    onOpenList: (String) -> Unit,
    onOpenSearch: () -> Unit,
    onCreateList: () -> Unit,
    onEditList: (TaskList) -> Unit,
) {
    val sorted = remember(lists) {
        lists.sortedWith(compareByDescending<TaskList> { it.isPinned }.thenBy { it.sortOrder })
    }
    val scrollBehavior = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        topBar = {
            MediumTopAppBar(
                title = { Text("Lists") },
                actions = {
                    IconButton(onClick = onOpenSearch) {
                        Icon(Icons.Rounded.Search, contentDescription = "Search")
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    scrolledContainerColor = MaterialTheme.colorScheme.surfaceContainer,
                ),
                scrollBehavior = scrollBehavior,
            )
        },
    ) { padding ->
        Column(
            Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(padding),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Column(Modifier.widthIn(max = ContentMaxWidth).padding(horizontal = ScreenPadding)) {
                Spacer(Modifier.height(4.dp))
                Column(verticalArrangement = Arrangement.spacedBy(GroupGap)) {
                    SmartLists.forEachIndexed { index, entry ->
                        ListBrowseRow(
                            shape = groupedShape(index, SmartLists.size),
                            label = entry.label,
                            leading = {
                                TonalIcon(
                                    entry.icon,
                                    smartContainerColor(entry.nav),
                                    smartContentColor(entry.nav),
                                )
                            },
                            count = remember(tasks, entry.nav) { navCount(tasks, entry.nav) },
                            onClick = { onOpenList(entry.nav) },
                        )
                    }
                }

                SectionLabel("Lists")
                Column(verticalArrangement = Arrangement.spacedBy(GroupGap)) {
                    // +1 so the "New list" row rounds off the group instead of floating.
                    val rowCount = sorted.size + 1
                    sorted.forEachIndexed { index, list ->
                        ListBrowseRow(
                            shape = groupedShape(index, rowCount),
                            label = list.name,
                            // Windows shows the emoji when a list has one; the tinted avatar
                            // keeps every row the same width either way.
                            leading = { ListAvatar(list.accentColor, list.customIcon) },
                            count = remember(tasks, list.id) { navCount(tasks, list.id) },
                            onClick = { onOpenList(list.id) },
                            trailing = {
                                IconButton(onClick = { onEditList(list) }) {
                                    Icon(
                                        Icons.Rounded.MoreVert,
                                        contentDescription = "Options for ${list.name}",
                                        tint = MaterialTheme.colorScheme.onSurfaceVariant,
                                    )
                                }
                            },
                        )
                    }
                    ListBrowseRow(
                        shape = groupedShape(rowCount - 1, rowCount),
                        label = "New list",
                        leading = {
                            TonalIcon(
                                Icons.Rounded.Add,
                                MaterialTheme.colorScheme.surfaceContainerHighest,
                                MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        },
                        count = 0,
                        onClick = onCreateList,
                    )
                }

                Spacer(Modifier.height(24.dp))
            }
        }
    }
}

// Not DateRange for both My Day and Planned: two rows with the same glyph are unreadable at
// a glance. My Day is absent entirely — it is the first tab.
private class SmartListEntry(val nav: String, val label: String, val icon: ImageVector)

private val SmartLists = listOf(
    SmartListEntry(NAV_ALL_TASKS, "All Tasks", Icons.AutoMirrored.Rounded.List),
    SmartListEntry(NAV_IMPORTANT, "Important", Icons.Rounded.Star),
    SmartListEntry(NAV_PLANNED, "Planned", Icons.Rounded.DateRange),
)

// Important is gold wherever it appears — the star on a row, the Starred tile on Summary,
// and here.
@Composable
private fun smartContainerColor(nav: String): Color = when (nav) {
    NAV_IMPORTANT -> MaterialTheme.colorScheme.tertiaryContainer
    NAV_PLANNED -> MaterialTheme.colorScheme.primaryContainer
    else -> MaterialTheme.colorScheme.secondaryContainer
}

@Composable
private fun smartContentColor(nav: String): Color = when (nav) {
    NAV_IMPORTANT -> MaterialTheme.colorScheme.onTertiaryContainer
    NAV_PLANNED -> MaterialTheme.colorScheme.onPrimaryContainer
    else -> MaterialTheme.colorScheme.onSecondaryContainer
}

@Composable
private fun ListBrowseRow(
    shape: Shape,
    label: String,
    leading: @Composable () -> Unit,
    count: Int,
    onClick: () -> Unit,
    trailing: (@Composable () -> Unit)? = null,
) {
    ListItem(
        // Clip first, then the click: the other order leaves the ripple square inside a
        // rounded row.
        modifier = Modifier.clip(shape).clickable(onClick = onClick),
        headlineContent = {
            Text(label, maxLines = 1, overflow = TextOverflow.Ellipsis)
        },
        leadingContent = leading,
        trailingContent = {
            Row(verticalAlignment = Alignment.CenterVertically) {
                // A plain number, not a Badge: Material's badge is an error colour by
                // default, so a list with four things to do was rendering the same red dot
                // an app uses to say something is wrong. Open only — a count that included
                // completed tasks would never go down.
                if (count > 0) {
                    Text(
                        "$count",
                        style = MaterialTheme.typography.labelLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                if (trailing != null) {
                    Spacer(Modifier.width(4.dp))
                    trailing()
                }
            }
        },
        colors = ListItemDefaults.colors(
            containerColor = MaterialTheme.colorScheme.surfaceContainer,
        ),
    )
}

@Composable
private fun SectionLabel(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.labelLarge,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(start = 16.dp, top = 24.dp, bottom = 8.dp),
    )
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
