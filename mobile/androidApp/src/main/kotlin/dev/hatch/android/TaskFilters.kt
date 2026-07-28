package dev.hatch.android

import dev.hatch.sync.TaskList
import dev.hatch.sync.TaskSearchMatcher
import dev.hatch.sync.TaskSorting
import dev.hatch.sync.TodoItem

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
