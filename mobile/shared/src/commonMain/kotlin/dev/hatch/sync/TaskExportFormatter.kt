package dev.hatch.sync

import kotlinx.datetime.LocalDate
import kotlinx.serialization.json.Json

// Kotlin transcription of windows/Helpers/TaskExportFormatter.cs. Pure formatting — writing
// the result to a file is the caller's job, exactly as it is on Windows.
//
// One deliberate difference: C# StringBuilder.AppendLine emits Environment.NewLine, so the
// Windows export is CRLF and this one is LF. Every reader of a .csv or .md accepts either,
// and forcing CRLF here would be the odd choice on the platform actually writing the file.
object TaskExportFormatter {

    // Separate from SyncWire.json, which pins the wire contract: this one exists to be read
    // by a person, so it is indented and must never be handed to the sync path.
    private val exportJson = Json {
        prettyPrint = true
        encodeDefaults = true
        explicitNulls = true
    }

    fun toJson(data: TasksFile): String = exportJson.encodeToString(data)

    fun toCsv(data: TasksFile): String {
        val listNames = buildListNameMap(data)
        val sb = StringBuilder()
        sb.appendLine("Title,List,Due Date,Priority,Completed,Tags,Notes")

        for (task in data.tasks) {
            sb.appendLine(
                listOf(
                    csvField(task.title),
                    csvField(listNameFor(task, listNames)),
                    csvField(dateAsWritten(task.dueDate)),
                    csvField(priorityName(task.priority)),
                    csvField(if (task.isCompleted) "Yes" else "No"),
                    csvField(task.tags.joinToString("; ")),
                    csvField(task.notes ?: ""),
                ).joinToString(",")
            )
        }

        return sb.toString()
    }

    // `today` is a parameter rather than a clock read, unlike the C# original — an export
    // whose first line changes every day cannot be asserted on.
    fun toMarkdown(data: TasksFile, today: LocalDate): String {
        val listNames = buildListNameMap(data)
        val sb = StringBuilder()
        sb.appendLine("# Hatch Tasks — $today")
        sb.appendLine()

        data.tasks
            .groupBy { listNameFor(it, listNames) }
            .toList()
            // lowercase(), not an ordinal-ignore-case comparator: kotlin.test's common API has
            // no equivalent of StringComparer.OrdinalIgnoreCase, and the two agree on every
            // list name that is not mixing scripts.
            .sortedBy { (name, _) -> name.lowercase() }
            .forEach { (name, tasks) ->
                sb.appendLine("## $name")

                tasks.sortedWith(
                    compareBy<TodoItem> { it.isCompleted }
                        .thenByDescending { TaskSorting.createdInstant(it) }
                ).forEach { task ->
                    val box = if (task.isCompleted) "[x]" else "[ ]"
                    val meta = buildList {
                        dueDateLabel(task.dueDate)?.let { add("due $it") }
                        priorityName(task.priority).takeIf { it.isNotEmpty() }?.let { add(it) }
                        if (task.tags.isNotEmpty()) add(task.tags.joinToString(" ") { "#$it" })
                    }
                    val suffix = if (meta.isEmpty()) "" else " (${meta.joinToString(", ")})"
                    sb.appendLine("- $box ${task.title}$suffix")
                }

                sb.appendLine()
            }

        return sb.toString()
    }

    private fun buildListNameMap(data: TasksFile): Map<String, String> =
        data.lists.associate { it.id to it.name }

    // Singular, matching the C# original: the default list is "Task" in an export and
    // "Tasks" in the UI. Kept as-is so a file exported on either client reads the same.
    private fun listNameFor(task: TodoItem, listNames: Map<String, String>): String =
        if (task.listId == DEFAULT_LIST_ID) "Task" else listNames[task.listId] ?: "Task"

    // Index is the wire value of Priority (§4); 0 prints nothing at all.
    private fun priorityName(priority: Int): String =
        PriorityNames.getOrElse(priority) { "" }

    private val PriorityNames = listOf("", "Low", "Medium", "High")

    private val MonthAbbreviations = listOf(
        "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
    )

    // The date as written, never through a time zone — a 20:00+00:00 due date exported east
    // of UTC used to come out as the next day (TaskExportFormatterTests.cs).
    private fun dateAsWritten(iso: String?): String =
        iso?.let { RecurrenceHelper.calendarDateOf(it) }?.toString() ?: ""

    private fun dueDateLabel(iso: String?): String? {
        val date = iso?.let { RecurrenceHelper.calendarDateOf(it) } ?: return null
        // ordinal, not a month number: Month is a common enum here, and January is 0.
        return "${MonthAbbreviations[date.month.ordinal]} ${date.day}"
    }

    private fun csvField(value: String): String =
        if (value.any { it == ',' || it == '"' || it == '\n' }) {
            "\"" + value.replace("\"", "\"\"") + "\""
        } else {
            value
        }
}
