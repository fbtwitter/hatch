package dev.hatch.sync

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// Mirrors windows/Hatch.Tests.Unit/SyncMergeTests.cs case for case. If these disagree, the
// two clients resolve the same conflict differently.
class SyncMergeTest {

    private fun task(id: String, title: String, updatedAt: String) = TodoItem(
        id = id,
        title = title,
        listId = "00000000-0000-0000-0000-000000000000",
        createdAt = "2026-01-01T00:00:00Z",
        updatedAt = updatedAt,
    )

    private val now = "2026-07-21T12:00:00+00:00"
    private val fiveMinutesAgo = "2026-07-21T11:55:00+00:00"

    @Test
    fun keeps_tasks_unique_to_each_side() {
        val local = TasksFile(tasks = listOf(task("11111111-1111-1111-1111-111111111111", "local only", now)))
        val server = TasksFile(tasks = listOf(task("22222222-2222-2222-2222-222222222222", "server only", now)))

        val merged = SyncMerge.merge(local, server)

        assertEquals(2, merged.tasks.size)
        assertTrue(merged.tasks.any { it.title == "local only" })
        assertTrue(merged.tasks.any { it.title == "server only" })
    }

    @Test
    fun same_id_keeps_later_updated_at_local_wins() {
        val id = "11111111-1111-1111-1111-111111111111"
        val local = TasksFile(tasks = listOf(task(id, "edited locally", now)))
        val server = TasksFile(tasks = listOf(task(id, "stale server copy", fiveMinutesAgo)))

        val merged = SyncMerge.merge(local, server)

        assertEquals(1, merged.tasks.size)
        assertEquals("edited locally", merged.tasks[0].title)
    }

    @Test
    fun same_id_keeps_later_updated_at_server_wins() {
        val id = "11111111-1111-1111-1111-111111111111"
        val local = TasksFile(tasks = listOf(task(id, "stale local copy", fiveMinutesAgo)))
        val server = TasksFile(tasks = listOf(task(id, "edited on another device", now)))

        val merged = SyncMerge.merge(local, server)

        assertEquals(1, merged.tasks.size)
        assertEquals("edited on another device", merged.tasks[0].title)
    }

    @Test
    fun exact_tie_keeps_the_local_copy() {
        val id = "11111111-1111-1111-1111-111111111111"
        val local = TasksFile(tasks = listOf(task(id, "local", now)))
        val server = TasksFile(tasks = listOf(task(id, "server", now)))

        assertEquals("local", SyncMerge.merge(local, server).tasks.single().title)
    }

    // Why UpdatedAt is parsed: a lexicographic comparison gets this wrong.
    @Test
    fun compares_instants_not_strings_across_z_and_offset_forms() {
        val id = "11111111-1111-1111-1111-111111111111"
        val local = TasksFile(tasks = listOf(task(id, "local", "2026-07-21T12:00:00Z")))
        val server = TasksFile(tasks = listOf(task(id, "server", "2026-07-21T13:00:00+02:00")))

        // 13:00+02:00 is 11:00Z, so local is genuinely newer.
        assertEquals("local", SyncMerge.merge(local, server).tasks.single().title)
    }

    @Test
    fun never_drops_data_union_matches_distinct_ids() {
        val shared = "11111111-1111-1111-1111-111111111111"
        val local = TasksFile(
            tasks = listOf(
                task(shared, "local version", now),
                task("22222222-2222-2222-2222-222222222222", "local extra", now),
            )
        )
        val server = TasksFile(
            tasks = listOf(
                task(shared, "server version", fiveMinutesAgo),
                task("33333333-3333-3333-3333-333333333333", "server extra", now),
            )
        )

        assertEquals(3, SyncMerge.merge(local, server).tasks.size)
    }

    @Test
    fun empty_server_returns_local_unchanged() {
        val local = TasksFile(tasks = listOf(task("11111111-1111-1111-1111-111111111111", "only task", now)))

        assertEquals(1, SyncMerge.merge(local, TasksFile()).tasks.size)
    }

    // Tombstones need no merge code of their own; these pin that.
    private fun tombstone(id: String, title: String, updatedAt: String) =
        task(id, title, updatedAt).copy(isDeleted = true)

    @Test
    fun delete_beats_older_live_copy() {
        val id = "11111111-1111-1111-1111-111111111111"
        val local = TasksFile(tasks = listOf(tombstone(id, "deleted on this device", now)))
        val server = TasksFile(tasks = listOf(task(id, "still alive on the server", fiveMinutesAgo)))

        val merged = SyncMerge.merge(local, server)

        assertEquals(1, merged.tasks.size)
        assertTrue(merged.tasks[0].isDeleted)
    }

    @Test
    fun edit_after_delete_revives_the_task() {
        val id = "11111111-1111-1111-1111-111111111111"
        val local = TasksFile(tasks = listOf(task(id, "edited just now", now)))
        val server = TasksFile(tasks = listOf(tombstone(id, "deleted earlier elsewhere", fiveMinutesAgo)))

        val merged = SyncMerge.merge(local, server)

        assertEquals(1, merged.tasks.size)
        assertEquals(false, merged.tasks[0].isDeleted)
        assertEquals("edited just now", merged.tasks[0].title)
    }

    @Test
    fun tombstone_survives_when_the_other_side_never_saw_the_task() {
        val local = TasksFile(tasks = listOf(tombstone("11111111-1111-1111-1111-111111111111", "gone", now)))

        val merged = SyncMerge.merge(local, TasksFile())

        assertEquals(1, merged.tasks.size)
        assertTrue(merged.tasks[0].isDeleted)
    }

    @Test
    fun deleted_list_beats_older_live_copy() {
        val id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
        val live = TaskList(id = id, name = "Groceries", accentColor = "#0078D4", updatedAt = fiveMinutesAgo)
        val local = TasksFile(lists = listOf(live.copy(updatedAt = now, isDeleted = true)))
        val server = TasksFile(lists = listOf(live))

        val merged = SyncMerge.merge(local, server)

        assertEquals(1, merged.lists.size)
        assertTrue(merged.lists[0].isDeleted)
    }
}
