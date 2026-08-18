package dev.hatch.android

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.DateRange
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material.icons.rounded.Warning
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MediumTopAppBar
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
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
            Column(
                Modifier
                    .widthIn(max = ContentMaxWidth)
                    .padding(horizontal = ScreenPadding, vertical = 12.dp),
            ) {
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

// Each tile is tinted by what it is saying rather than all four being the same grey card —
// overdue reads as a warning at a glance, and a day with nothing slipping does not.
@Composable
private fun SummaryTileCard(tile: SummaryTile, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val container = when (tile.tone) {
        SummaryTileTone.Neutral -> MaterialTheme.colorScheme.surfaceContainer
        SummaryTileTone.Success -> MaterialTheme.colorScheme.primaryContainer
        SummaryTileTone.Critical -> MaterialTheme.colorScheme.errorContainer
        SummaryTileTone.Starred -> MaterialTheme.colorScheme.tertiaryContainer
    }
    val content = when (tile.tone) {
        SummaryTileTone.Neutral -> MaterialTheme.colorScheme.onSurface
        SummaryTileTone.Success -> MaterialTheme.colorScheme.onPrimaryContainer
        SummaryTileTone.Critical -> MaterialTheme.colorScheme.onErrorContainer
        SummaryTileTone.Starred -> MaterialTheme.colorScheme.onTertiaryContainer
    }
    // The supporting text on a tinted tile cannot use onSurfaceVariant — that is a neutral
    // grey and would sit on a coloured ground. Same colour at lower alpha instead.
    val muted = content.copy(alpha = 0.75f)

    Surface(
        onClick = onClick,
        shape = RoundedCornerShape(CardCorner),
        color = container,
        contentColor = content,
        modifier = modifier.heightIn(min = 132.dp),
    ) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.Center) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                Icon(tileIcon(tile.id), contentDescription = null, tint = muted, modifier = Modifier.size(15.dp))
                Text(tile.title, style = MaterialTheme.typography.labelMedium, color = muted)
            }
            Spacer(Modifier.height(8.dp))
            Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    tile.value,
                    style = MaterialTheme.typography.headlineMedium,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (tile.secondaryValue != null) {
                    Text(
                        tile.secondaryValue,
                        style = MaterialTheme.typography.labelLarge,
                        color = muted,
                        modifier = Modifier.padding(bottom = 3.dp),
                    )
                }
            }
            if (tile.progress != null) {
                Spacer(Modifier.height(8.dp))
                // Animated so arriving on the tab fills the bar rather than presenting it.
                val progress by animateFloatAsState(
                    targetValue = tile.progress,
                    animationSpec = tween(MotionMedium, easing = EmphasizedDecelerate),
                    label = "myDayProgress",
                )
                LinearProgressIndicator(
                    progress = { progress },
                    color = content,
                    trackColor = content.copy(alpha = 0.20f),
                    drawStopIndicator = {},
                    modifier = Modifier.fillMaxWidth().height(6.dp).clip(MaterialTheme.shapes.small),
                )
            }
            Spacer(Modifier.height(6.dp))
            Text(
                tile.description,
                style = MaterialTheme.typography.bodySmall,
                color = muted,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
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
            shape = RoundedCornerShape(CardCorner),
            color = MaterialTheme.colorScheme.surfaceContainer,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(
                emptyText,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
                modifier = Modifier.fillMaxWidth().padding(20.dp),
            )
        }
    } else {
        Column(
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(CardCorner))
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
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    },
                    colors = ListItemDefaults.colors(containerColor = Color.Transparent),
                )
                if (index != rows.lastIndex) {
                    HorizontalDivider(
                        color = MaterialTheme.colorScheme.outlineVariant,
                        modifier = Modifier.padding(horizontal = 16.dp),
                    )
                }
            }
        }
    }
    Spacer(Modifier.height(20.dp))
}
