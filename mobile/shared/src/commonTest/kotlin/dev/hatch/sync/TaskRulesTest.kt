package dev.hatch.sync

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Mirrors windows/Hatch.Tests.Unit/TaskSearchMatcherTests.cs and TaskSortingTests.cs.
class TaskRulesTest {

    private fun task(
        title: String,
        notes: String? = null,
        tags: List<String> = emptyList(),
        priority: Int = 0,
        createdAt: String = "2026-01-01T00:00:00Z",
    ) = TodoItem(
        id = "11111111-1111-1111-1111-111111111111",
        title = title,
        notes = notes,
        tags = tags,
        priority = priority,
        listId = "00000000-0000-0000-0000-000000000000",
        createdAt = createdAt,
        updatedAt = createdAt,
    )

    @Test
    fun matches_title_substring_case_insensitive() {
        val t = task("Buy Groceries")
        assertTrue(TaskSearchMatcher.matches(t, "groceries"))
        assertTrue(TaskSearchMatcher.matches(t, "GROCERIES"))
    }

    @Test
    fun matches_notes_substring() {
        assertTrue(TaskSearchMatcher.matches(task("Task", notes = "remember the milk"), "milk"))
    }

    @Test
    fun matches_tag_substring() {
        assertTrue(TaskSearchMatcher.matches(task("Task", tags = listOf("work", "urgent")), "urg"))
    }

    @Test
    fun does_not_match_when_query_absent_everywhere() {
        val t = task("Task", notes = "notes", tags = listOf("tag"))
        assertFalse(TaskSearchMatcher.matches(t, "nonexistent"))
    }

    @Test
    fun matches_handles_null_notes_gracefully() {
        assertFalse(TaskSearchMatcher.matches(task("Task", notes = null), "anything"))
    }

    @Test
    fun for_important_orders_by_priority_descending() {
        val low = task("low", priority = 1)
        val high = task("high", priority = 3)
        val medium = task("medium", priority = 2)
        val none = task("none", priority = 0)

        val ordered = TaskSorting.forImportant(listOf(low, high, medium, none))

        assertEquals(listOf("high", "medium", "low", "none"), ordered.map { it.title })
    }

    @Test
    fun for_important_breaks_ties_by_newest_first() {
        val older = task("older", priority = 3, createdAt = "2026-01-01T00:00:00Z")
        val newer = task("newer", priority = 3, createdAt = "2026-01-01T00:10:00Z")

        val ordered = TaskSorting.forImportant(listOf(older, newer))

        assertEquals(listOf("newer", "older"), ordered.map { it.title })
    }

    // Windows stamps createdAt local, this app stamps it UTC. Sorting the text puts a task
    // added here below one the desktop created ten minutes earlier.
    @Test
    fun newest_first_compares_instants_not_text_across_offsets() {
        val fromPhone = task("from phone", createdAt = "2026-07-26T12:00:00Z")
        val fromDesktop = task("from desktop", createdAt = "2026-07-26T18:50:00+07:00")

        val ordered = TaskSorting.newestFirst(listOf(fromDesktop, fromPhone))

        assertEquals(listOf("from phone", "from desktop"), ordered.map { it.title })
    }

    @Test
    fun for_important_breaks_ties_by_instant_across_offsets() {
        val fromPhone = task("from phone", priority = 3, createdAt = "2026-07-26T12:00:00Z")
        val fromDesktop = task("from desktop", priority = 3, createdAt = "2026-07-26T18:50:00+07:00")

        val ordered = TaskSorting.forImportant(listOf(fromDesktop, fromPhone))

        assertEquals(listOf("from phone", "from desktop"), ordered.map { it.title })
    }

    @Test
    fun unparseable_created_at_sorts_oldest_rather_than_first() {
        val good = task("good", createdAt = "2026-07-26T12:00:00Z")
        val broken = task("broken", createdAt = "not a date")

        val ordered = TaskSorting.newestFirst(listOf(broken, good))

        assertEquals(listOf("good", "broken"), ordered.map { it.title })
    }
}
