package dev.hatch.sync

import kotlin.time.Instant

// Transcription of windows/Helpers/TaskSorting.cs.
object TaskSorting {

    // Never compare createdAt as text: Windows stamps local time, this app stamps UTC, so
    // "18:00+07:00" and "11:00Z" are the same moment but sort seven hours apart.
    // Unparseable sorts oldest rather than jumping to the top.
    fun createdInstant(task: TodoItem): Instant =
        runCatching { Instant.parse(task.createdAt) }.getOrElse { Instant.DISTANT_PAST }

    fun newestFirst(tasks: List<TodoItem>): List<TodoItem> =
        tasks.sortedByDescending { createdInstant(it) }

    fun forImportant(tasks: List<TodoItem>): List<TodoItem> =
        tasks.sortedWith(
            compareByDescending<TodoItem> { it.priority }.thenByDescending { createdInstant(it) }
        )
}
