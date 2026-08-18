package dev.hatch.android

import dev.hatch.sync.DEFAULT_LIST_ID
import dev.hatch.sync.TaskList
import dev.hatch.sync.TaskSearchMatcher
import dev.hatch.sync.TaskSorting
import dev.hatch.sync.TodoItem
import java.time.LocalDate

// Transcription of MainViewModel.MatchesFilter + RefreshActiveTasks. Not in commonMain
// because the C# original is not in the portable core either — so nothing pins the two
// together yet. See context/current-feature.md.
fun tasksForNav(tasks: List<TodoItem>, nav: String): List<TodoItem> = when (nav) {
    NAV_MY_DAY -> tasks.filter { it.isInMyDay }.sortedWith(
        compareByDescending<TodoItem> { TaskSorting.createdInstant(it) }.thenBy { it.isCompleted }
    )

    NAV_IMPORTANT -> TaskSorting.forImportant(tasks.filter { it.isStarred })

    // Undated tasks are deliberately absent — Planned is a time-based view.
    NAV_PLANNED -> tasks.filter { it.dueDate != null && !it.isCompleted }.sortedBy { it.dueDate }

    NAV_ALL_TASKS -> TaskSorting.newestFirst(tasks)

    // Anything else is a list id.
    else -> TaskSorting.newestFirst(tasks.filter { it.listId == nav })
}

// Spans every list and both completion states, ignoring the current view — like Ctrl+F.
fun searchResults(tasks: List<TodoItem>, query: String): List<TodoItem> {
    val trimmed = query.trim()
    if (trimmed.isEmpty()) return emptyList()
    return TaskSorting.newestFirst(tasks.filter { TaskSearchMatcher.matches(it, trimmed) })
}

// Mirrors MainViewModel.MatchesFilter's tag clause: case-insensitive, and an exact tag
// rather than a substring — this is a chip you tapped, not something you typed.
fun withTagFilter(tasks: List<TodoItem>, tag: String?): List<TodoItem> =
    if (tag == null) tasks else tasks.filter { task -> task.tags.any { it.equals(tag, true) } }

// Transcription of MainViewModel.Suggestions.cs: everything still open that today has not
// claimed yet, newest first.
fun suggestions(tasks: List<TodoItem>): List<TodoItem> =
    TaskSorting.newestFirst(tasks.filter { !it.isCompleted && !it.isInMyDay })

// One rendered block of the task list: an optional header, then its rows.
data class TaskSection(val title: String?, val tasks: List<TodoItem>)

fun sectionsFor(nav: String, visible: List<TodoItem>, today: LocalDate): List<TaskSection> =
    if (nav == NAV_PLANNED) plannedSections(visible, today) else openThenCompleted(visible)

private fun openThenCompleted(visible: List<TodoItem>): List<TaskSection> {
    val open = visible.filter { !it.isCompleted }
    val done = visible.filter { it.isCompleted }
    return buildList {
        if (open.isNotEmpty()) add(TaskSection(null, open))
        if (done.isNotEmpty()) add(TaskSection("Completed · ${done.size}", done))
    }
}

private val PlannedGroupOrder = listOf("Overdue", "Today", "Tomorrow", "This week", "Later")

// Transcription of MainViewModel.BuildPlannedGroups, headings included — Planned is the one
// list where a flat due-date sort hides the only thing worth seeing, which is where the
// cliff is. Input order is preserved inside each group, so each stays soonest-first.
private fun plannedSections(visible: List<TodoItem>, today: LocalDate): List<TaskSection> {
    val tomorrow = today.plusDays(1)
    // C# numbers the week from Sunday = 0, so `7 - dayOfWeek` always lands on the coming
    // Sunday; java.time numbers it from Monday = 1, hence the modulo.
    val weekEnd = today.plusDays((7 - today.dayOfWeek.value % 7).toLong())

    return visible
        .groupBy { task ->
            val due = localDateOf(task.dueDate)
            when {
                // Unparseable rather than absent: nav filtering already dropped undated tasks.
                due == null -> "Later"
                due.isBefore(today) -> "Overdue"
                due == today -> "Today"
                due == tomorrow -> "Tomorrow"
                !due.isAfter(weekEnd) -> "This week"
                else -> "Later"
            }
        }
        .toList()
        .sortedBy { (name, _) -> PlannedGroupOrder.indexOf(name) }
        .map { (name, tasks) -> TaskSection(name, tasks) }
}

fun navTitle(nav: String, lists: List<TaskList>): String = when (nav) {
    NAV_MY_DAY -> "My Day"
    NAV_IMPORTANT -> "Important"
    NAV_PLANNED -> "Planned"
    NAV_ALL_TASKS -> "All Tasks"
    else -> lists.firstOrNull { it.id == nav }?.name ?: "Tasks"
}

// Open only — a badge counting completed tasks would never go down.
fun navCount(tasks: List<TodoItem>, nav: String): Int =
    tasksForNav(tasks, nav).count { !it.isCompleted }

// The smart lists are views, not containers, so a task added there lands in the default.
fun listIdForNav(nav: String): String = when (nav) {
    NAV_MY_DAY, NAV_IMPORTANT, NAV_PLANNED, NAV_ALL_TASKS -> DEFAULT_LIST_ID
    else -> nav
}
