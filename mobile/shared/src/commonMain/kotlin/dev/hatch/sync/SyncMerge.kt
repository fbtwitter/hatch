package dev.hatch.sync

import kotlin.time.Instant

// Transcription of windows/Services/SyncMerge.cs and docs/sync-protocol.md §5.
// Record-level last-write-wins union keyed by Id; local wins an exact tie.
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

    // Unparseable must lose: treating it as "now" would let bad data overwrite good.
    private fun instantOf(text: String): Instant =
        runCatching { Instant.parse(text) }.getOrElse { Instant.DISTANT_PAST }
}
