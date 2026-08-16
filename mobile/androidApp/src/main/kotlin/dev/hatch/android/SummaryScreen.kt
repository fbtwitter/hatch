package dev.hatch.android

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.DateRange
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material.icons.rounded.Warning
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MediumTopAppBar
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem

// Mirrors windows/Views/StatsPage.xaml: a KPI tile grid, then Today, then Upcoming. Computed
// on navigation to this tab, not live-bound — the task list and Summary can't be visible at
// the same time, same as the Windows StatsPage.OnNavigatedTo comment explains.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SummaryScreen(
    tasks: List<TodoItem>,
    lists: List<TaskList>,
    onNavigateToList: (String) -> Unit,
    onOpenTask: (TodoItem) -> Unit,
) {
    val listNames = remember(lists) { lists.associate { it.id to it.name } }
    val summary = remember(tasks, listNames) { computeSummary(tasks, listNames) }
    val scrollBehavior = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        topBar = {
            MediumTopAppBar(
                title = { Text("Summary") },
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
            Column(Modifier.widthIn(max = ContentMaxWidth).padding(horizontal = 12.dp, vertical = 12.dp)) {
                summary.tiles.chunked(2).forEach { rowTiles ->
                    Row(
                        Modifier.fillMaxWidth().padding(bottom = 12.dp),
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        rowTiles.forEach { tile ->
                            SummaryTileCard(
                                tile = tile,
                                modifier = Modifier.weight(1f),
                                onClick = { onNavigateToList(tile.navTarget) },
                            )
                        }
                        // An odd tile count would otherwise stretch the last tile full-width.
                        if (rowTiles.size == 1) Spacer(Modifier.weight(1f))
                    }
                }

                SummarySection(
                    header = "Today",
                    rows = summary.todayTasks,
                    emptyText = "Nothing due today.",
                    onOpenTask = onOpenTask,
                )
                SummarySection(
                    header = "Upcoming",
                    rows = summary.upcomingTasks,
                    emptyText = "Nothing due soon.",
                    onOpenTask = onOpenTask,
                )

                Spacer(Modifier.height(24.dp))
            }
        }
    }
}

@Composable
private fun SummaryTileCard(tile: SummaryTile, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val tone = when (tile.tone) {
        SummaryTileTone.Neutral -> MaterialTheme.colorScheme.onSurfaceVariant
        SummaryTileTone.Success -> MaterialTheme.colorScheme.primary
        SummaryTileTone.Critical -> MaterialTheme.colorScheme.error
        SummaryTileTone.Starred -> MaterialTheme.colorScheme.tertiary
    }
    Surface(
        onClick = onClick,
        shape = MaterialTheme.shapes.large,
        color = MaterialTheme.colorScheme.surfaceContainer,
        modifier = modifier.heightIn(min = 124.dp),
    ) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.Center) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(tileIcon(tile.id), contentDescription = null, tint = tone, modifier = Modifier.size(16.dp))
                Text(
                    tile.title,
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Spacer(Modifier.height(6.dp))
            Text(tile.value, style = MaterialTheme.typography.headlineMedium)
            Spacer(Modifier.height(2.dp))
            Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                if (tile.secondaryValue != null) {
                    Text(
                        tile.secondaryValue,
                        style = MaterialTheme.typography.labelLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                Text(
                    tile.description,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

private fun tileIcon(id: String): ImageVector = when (id) {
    "myday" -> Icons.Rounded.CheckCircle
    "duetoday" -> Icons.Rounded.DateRange
    "overdue" -> Icons.Rounded.Warning
    else -> Icons.Rounded.Star
}

@Composable
private fun SummarySection(
    header: String,
    rows: List<SummaryTaskRow>,
    emptyText: String,
    onOpenTask: (TodoItem) -> Unit,
) {
    Text(
        header,
        style = MaterialTheme.typography.titleSmall,
        modifier = Modifier.padding(start = 4.dp, bottom = 8.dp),
    )
    if (rows.isEmpty()) {
        Surface(
            shape = MaterialTheme.shapes.large,
            color = MaterialTheme.colorScheme.surfaceContainer,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(
                emptyText,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
                modifier = Modifier.fillMaxWidth().padding(16.dp),
            )
        }
    } else {
        Column(
            Modifier
                .fillMaxWidth()
                .clip(MaterialTheme.shapes.large)
                .background(MaterialTheme.colorScheme.surfaceContainer),
        ) {
            rows.forEachIndexed { index, row ->
                ListItem(
                    modifier = Modifier.clickable { onOpenTask(row.task) },
                    headlineContent = {
                        Text(row.title, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    },
                    trailingContent = {
                        Text(
                            row.detail,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    },
                    colors = ListItemDefaults.colors(containerColor = Color.Transparent),
                )
                if (index != rows.lastIndex) {
                    HorizontalDivider(
                        color = MaterialTheme.colorScheme.outlineVariant,
                        modifier = Modifier.padding(start = 16.dp),
                    )
                }
            }
        }
    }
    Spacer(Modifier.height(20.dp))
}
