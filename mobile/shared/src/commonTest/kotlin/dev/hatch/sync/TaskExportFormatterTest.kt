package dev.hatch.sync

import kotlinx.datetime.LocalDate
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// Mirrors windows/Hatch.Tests.Unit/TaskExportFormatterTests.cs case for case.
class TaskExportFormatterTest {

    private val customListId = "11111111-2222-3333-4444-555555555555"

    private fun task(
        title: String,
        listId: String = DEFAULT_LIST_ID,
        priority: Int = 0,
        tags: List<String> = emptyList(),
        dueDate: String? = null,
        isCompleted: Boolean = false,
        notes: String? = null,
    ) = TodoItem(
        id = title,
        title = title,
        listId = listId,
        priority = priority,
        tags = tags,
        dueDate = dueDate,
        isCompleted = isCompleted,
        notes = notes,
        createdAt = "2026-07-01T00:00:00+00:00",
        updatedAt = "2026-07-01T00:00:00+00:00",
    )

    private fun sampleData() = TasksFile(
        lists = listOf(
            TaskList(
                id = customListId,
                name = "Work Project",
                accentColor = "#0078D4",
                updatedAt = "2026-07-01T00:00:00+00:00",
            )
        ),
        tasks = listOf(
            task("Default list task"),
            task(
                "Custom list task",
                listId = customListId,
                priority = 3,
                tags = listOf("urgent", "client"),
                dueDate = "2026-07-15T00:00:00+00:00",
                isCompleted = true,
            ),
        ),
    )

    private val exportDay = LocalDate(2026, 8, 17)

    @Test
    fun toJson_round_trips_task_count() {
        val json = TaskExportFormatter.toJson(sampleData())
        assertTrue(json.contains("Default list task"))
        assertTrue(json.contains("Custom list task"))
    }

    @Test
    fun toCsv_includes_header_and_both_tasks() {
        val csv = TaskExportFormatter.toCsv(sampleData())
        val lines = csv.split("\n").filter { it.isNotEmpty() }

        assertEquals("Title,List,Due Date,Priority,Completed,Tags,Notes", lines[0])
        assertEquals(3, lines.size) // header + 2 tasks
        assertTrue(csv.contains("Work Project"))
        assertTrue(csv.contains("High"))
    }

    @Test
    fun toCsv_dueDate_is_the_date_as_written() {
        // 20:00 +00:00 exported through a local-time conversion became the next day east of UTC.
        val data = TasksFile(tasks = listOf(task("t", dueDate = "2026-07-15T20:00:00+00:00")))

        assertTrue(TaskExportFormatter.toCsv(data).contains("2026-07-15"))
    }

    @Test
    fun toCsv_quotes_fields_containing_commas() {
        val data = TasksFile(tasks = listOf(task("Buy milk, eggs, bread")))

        assertTrue(TaskExportFormatter.toCsv(data).contains("\"Buy milk, eggs, bread\""))
    }

    @Test
    fun toCsv_escapes_embedded_quotes_by_doubling_them() {
        val data = TasksFile(tasks = listOf(task("Read \"Dune\"")))

        assertTrue(TaskExportFormatter.toCsv(data).contains("\"Read \"\"Dune\"\"\""))
    }

    @Test
    fun toMarkdown_groups_by_list_and_uses_checkboxes() {
        val md = TaskExportFormatter.toMarkdown(sampleData(), exportDay)

        assertTrue(md.contains("## Task"))
        assertTrue(md.contains("## Work Project"))
        assertTrue(md.contains("- [ ] Default list task"))
        assertTrue(md.contains("- [x] Custom list task"))
        assertTrue(md.contains("#urgent"))
    }

    @Test
    fun toMarkdown_writes_the_due_date_as_a_short_month_and_day() {
        val md = TaskExportFormatter.toMarkdown(sampleData(), exportDay)

        assertTrue(md.contains("(due Jul 15, High, #urgent #client)"))
    }

    @Test
    fun toMarkdown_empty_data_still_produces_a_header() {
        val md = TaskExportFormatter.toMarkdown(TasksFile(), exportDay)

        assertTrue(md.startsWith("# Hatch Tasks"))
    }
}
