package dev.hatch.sync

// Transcription of windows/Helpers/TaskSearchMatcher.cs.
object TaskSearchMatcher {

    fun matches(task: TodoItem, query: String): Boolean =
        task.title.contains(query, ignoreCase = true) ||
            (task.notes?.contains(query, ignoreCase = true) == true) ||
            task.tags.any { it.contains(query, ignoreCase = true) }
}
