package dev.hatch.android

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.automirrored.rounded.List
import androidx.compose.material.icons.rounded.DateRange
import androidx.compose.material.icons.rounded.Delete
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Badge
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalDrawerSheet
import androidx.compose.material3.NavigationDrawerItem
import androidx.compose.material3.NavigationDrawerItemDefaults
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem

// Same order and names as MainPage's NavigationView.
@Composable
fun ListsDrawerSheet(
    tasks: List<TodoItem>,
    lists: List<TaskList>,
    activeNav: String,
    onNavigate: (String) -> Unit,
    onCreateList: () -> Unit,
    onEditList: (TaskList) -> Unit,
) {
    val sorted = remember(lists) {
        lists.sortedWith(compareByDescending<TaskList> { it.isPinned }.thenBy { it.sortOrder })
    }

    ModalDrawerSheet(drawerContainerColor = MaterialTheme.colorScheme.surfaceContainerLow) {
        // The lists scroll; the footer does not. With everything in one scrolling column,
        // Settings sat wherever the list of lists happened to end — halfway up the sheet for
        // a few lists, and off the bottom edge for many.
        Column(Modifier.fillMaxHeight()) {
            Column(
                Modifier
                    .weight(1f)
                    .verticalScroll(rememberScrollState()),
            ) {
                Text(
                    "Hatch",
                    style = MaterialTheme.typography.headlineSmall,
                    modifier = Modifier.padding(start = 28.dp, top = 24.dp, bottom = 4.dp),
                )
                Text(
                    "Local first · sync optional",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(start = 28.dp, bottom = 16.dp),
                )

                // Not DateRange for both My Day and Planned: two nav items with the same glyph
                // are unreadable at a glance. The outlined "today" and "sun" icons are in
                // material-icons-extended, so CheckCircle is the distinct core-set option.
                SmartListItem(NAV_MY_DAY, "My Day", Icons.Rounded.CheckCircle, tasks, activeNav, onNavigate)
                SmartListItem(NAV_IMPORTANT, "Important", Icons.Rounded.Star, tasks, activeNav, onNavigate)
                SmartListItem(NAV_PLANNED, "Planned", Icons.Rounded.DateRange, tasks, activeNav, onNavigate)
                // AutoMirrored: the manifest declares supportsRtl.
                SmartListItem(NAV_ALL_TASKS, "All Tasks", Icons.AutoMirrored.Rounded.List, tasks, activeNav, onNavigate)

                HorizontalDivider(
                    Modifier.padding(horizontal = 28.dp, vertical = 12.dp),
                    color = MaterialTheme.colorScheme.outlineVariant,
                )

                Text(
                    "Lists",
                    style = MaterialTheme.typography.labelLarge,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(start = 28.dp, bottom = 4.dp),
                )

                sorted.forEach { list ->
                    val count = remember(tasks, list.id) { navCount(tasks, list.id) }
                    NavigationDrawerItem(
                        selected = activeNav == list.id,
                        onClick = { onNavigate(list.id) },
                        // Windows shows the emoji when a list has one; falling back to the
                        // colour dot keeps every row the same width either way.
                        icon = {
                            if (list.customIcon.isNullOrBlank()) ColorDot(list.accentColor)
                            else Text(list.customIcon!!, style = MaterialTheme.typography.bodyLarge)
                        },
                        label = { Text(list.name) },
                        badge = {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                if (count > 0) Badge { Text("$count") }
                                IconButton(onClick = { onEditList(list) }) {
                                    Icon(
                                        Icons.Rounded.MoreVert,
                                        contentDescription = "Options for ${list.name}",
                                    )
                                }
                            }
                        },
                        modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding),
                    )
                }

                NavigationDrawerItem(
                    selected = false,
                    onClick = onCreateList,
                    icon = { Icon(Icons.Rounded.Add, contentDescription = null) },
                    label = { Text("New list") },
                    modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding),
                )
                Spacer(Modifier.height(16.dp))
            }
        }
    }
}

@Composable
private fun SmartListItem(
    nav: String,
    label: String,
    icon: ImageVector,
    tasks: List<TodoItem>,
    activeNav: String,
    onNavigate: (String) -> Unit,
) {
    val count = remember(tasks, nav) { navCount(tasks, nav) }
    NavigationDrawerItem(
        selected = activeNav == nav,
        onClick = { onNavigate(nav) },
        icon = { Icon(icon, contentDescription = null) },
        label = { Text(label) },
        badge = { if (count > 0) Badge { Text("$count") } },
        modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding),
    )
}

@Composable
private fun ColorDot(hex: String, size: Int = 16) {
    // AccentColor is free text on the wire, so a bad value must not take the drawer down.
    val color = remember(hex) {
        runCatching { Color(("ff" + hex.removePrefix("#")).toLong(16)) }.getOrDefault(Color.Gray)
    }
    Box(Modifier.size(size.dp).clip(CircleShape).background(color))
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
