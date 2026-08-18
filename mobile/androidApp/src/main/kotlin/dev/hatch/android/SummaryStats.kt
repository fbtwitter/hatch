package dev.hatch.android

import dev.hatch.sync.TaskSorting
import dev.hatch.sync.TodoItem
import java.time.LocalDate
import java.time.format.DateTimeFormatter

// Transcription of windows/ViewModels/StatsViewModel.cs RefreshStats(). Not in commonMain for
// the same reason TaskFilters.kt isn't — the C# original lives on a WinUI ViewModel, so
// nothing pins the two together yet.

enum class SummaryTileTone { Neutral, Success, Critical, Starred }

data class SummaryTile(
    val id: String,
    val title: String,
    val value: String,
    val secondaryValue: String?,
    val description: String,
    val tone: SummaryTileTone,
    val navTarget: String,
    // 0f..1f for a tile that measures completion, null for one that just counts. Only My Day
    // has a denominator; "3 overdue" is not 3 out of anything.
    val progress: Float? = null,
)

data class SummaryTaskRow(val task: TodoItem, val title: String, val detail: String)

data class SummaryData(
    val tiles: List<SummaryTile>,
    val todayTasks: List<SummaryTaskRow>,
    val upcomingTasks: List<SummaryTaskRow>,
)

private val UpcomingDateFormatter: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE, MMM d")

fun computeSummary(tasks: List<TodoItem>, listNames: Map<String, String>): SummaryData {
    val today = LocalDate.now()

    val myDayTasks = tasks.filter { it.isInMyDay }
    val myDayTotal = myDayTasks.size
    val myDayCompleted = myDayTasks.count { it.isCompleted }
    val myDayPlanned = myDayTotal > 0
    val myDayPercent = if (myDayPlanned) Math.round(myDayCompleted * 100.0 / myDayTotal).toInt() else 0

    val dueToday = tasks.count { !it.isCompleted && localDateOf(it.dueDate) == today }
    val overdue = tasks.count { !it.isCompleted && localDateOf(it.dueDate)?.isBefore(today) == true }
    // Matches the Important nav filter: starred and not yet completed, so a completed task
    // doesn't sit in this count forever.
    val starred = tasks.count { it.isStarred && !it.isCompleted }

    val tiles = listOf(
        SummaryTile(
            id = "myday",
            title = "My Day",
            // completed/total, not remaining/total — the percentage beneath measures
            // completion, and remaining/total would render an untouched day as "2 / 2" at 0%.
            value = if (myDayPlanned) "$myDayCompleted / $myDayTotal" else "0",
            secondaryValue = if (myDayPlanned) "$myDayPercent%" else null,
            description = if (myDayPlanned) "complete today" else "Nothing planned yet",
            tone = if (myDayPlanned) SummaryTileTone.Success else SummaryTileTone.Neutral,
            navTarget = NAV_MY_DAY,
            progress = if (myDayPlanned) myDayCompleted.toFloat() / myDayTotal else null,
        ),
        SummaryTile(
            id = "duetoday",
            title = "Due today",
            value = dueToday.toString(),
            secondaryValue = null,
            description = "tasks on the docket",
            tone = SummaryTileTone.Neutral,
            navTarget = NAV_PLANNED,
        ),
        SummaryTile(
            id = "overdue",
            title = "Overdue",
            value = overdue.toString(),
            secondaryValue = null,
            description = if (overdue > 0) "Catch up when you can" else "Nothing slipping",
            tone = if (overdue > 0) SummaryTileTone.Critical else SummaryTileTone.Neutral,
            navTarget = NAV_PLANNED,
        ),
        SummaryTile(
            id = "starred",
            title = "Starred",
            value = starred.toString(),
            secondaryValue = null,
            description = "Important & pinned",
            tone = SummaryTileTone.Starred,
            navTarget = NAV_IMPORTANT,
        ),
    )

    val todayTasks = tasks
        .filter { !it.isCompleted && localDateOf(it.dueDate) == today }
        .sortedBy { TaskSorting.createdInstant(it) }
        .map { SummaryTaskRow(it, it.title, listNames[it.listId].orEmpty()) }

    val tomorrow = today.plusDays(1)
    val upcomingTasks = tasks
        .filter { !it.isCompleted && localDateOf(it.dueDate)?.isAfter(today) == true }
        .sortedBy { it.dueDate }
        .take(5)
        .map { task ->
            val date = localDateOf(task.dueDate)!!
            val label = if (date == tomorrow) "Tomorrow" else date.format(UpcomingDateFormatter)
            SummaryTaskRow(task, task.title, label)
        }

    return SummaryData(tiles, todayTasks, upcomingTasks)
}
