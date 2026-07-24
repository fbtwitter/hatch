package dev.hatch.sync

import kotlin.time.Instant

// Kotlin transcription of windows/Services/SyncMerge.cs and docs/sync-protocol.md §5.
// Record-level (not field-level) last-write-wins union keyed by Id: an item on one side
// only is kept; an item on both keeps whichever has the later UpdatedAt, and on an exact
// tie the local copy wins. Nothing is ever dropped.
object SyncMerge {

    fun merge(local: TasksFile, server: TasksFile): TasksFile = TasksFile(
        tasks = mergeById(local.tasks, server.tasks, { it.id }, { it.updatedAt }),
        lists = mergeById(local.lists, server.lists, { it.id }, { it.updatedAt }),
    )

    private fun <T> mergeById(
        local: List<T>,
        server: List<T>,
        idOf: (T) -> String,
        updatedAtOf: (T) -> String,
    ): List<T> {
        val merged = LinkedHashMap<String, T>()
        for (item in server) merged[idOf(item)] = item

        for (item in local) {
            val id = idOf(item)
            val existing = merged[id]
            if (existing == null || instantOf(updatedAtOf(item)) >= instantOf(updatedAtOf(existing))) {
                merged[id] = item
            }
        }
        return merged.values.toList()
    }

    // An unparseable timestamp must lose rather than win: treating it as "now" would let
    // malformed data silently overwrite good data.
    private fun instantOf(text: String): Instant =
        runCatching { Instant.parse(text) }.getOrElse { Instant.DISTANT_PAST }
}
