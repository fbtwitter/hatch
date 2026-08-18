package dev.hatch.sync

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

// Transcription of Services/SyncWire.cs + the payload schema in docs/sync-protocol.md §4.
//
// Timestamps stay String deliberately: the contract pins the wire text, and a date type
// would risk re-formatting it on the way back out.

// Guid.Empty on Windows (§4) — the default list, shown as "Tasks" in the UI and "Task" in an
// export. Lives here rather than in the Android module because the wire types and the export
// formatter both need it, and neither is allowed to know about the other.
const val DEFAULT_LIST_ID = "00000000-0000-0000-0000-000000000000"

@Serializable
data class TasksFile(
    @SerialName("Tasks") val tasks: List<TodoItem> = emptyList(),
    @SerialName("Lists") val lists: List<TaskList> = emptyList(),
)

@Serializable
data class TodoItem(
    @SerialName("Id") val id: String,
    @SerialName("Title") val title: String,
    @SerialName("IsCompleted") val isCompleted: Boolean = false,
    @SerialName("CompletedAt") val completedAt: String? = null,
    // Tombstone (§5). The default is what makes a v1 payload read as entirely live.
    @SerialName("IsDeleted") val isDeleted: Boolean = false,
    @SerialName("IsStarred") val isStarred: Boolean = false,
    @SerialName("IsInMyDay") val isInMyDay: Boolean = false,
    @SerialName("MyDayDate") val myDayDate: String? = null,
    @SerialName("DueDate") val dueDate: String? = null,
    @SerialName("ListId") val listId: String,
    @SerialName("Recurrence") val recurrence: Int = 0,
    @SerialName("Priority") val priority: Int = 0,
    @SerialName("Tags") val tags: List<String> = emptyList(),
    @SerialName("CreatedAt") val createdAt: String,
    @SerialName("UpdatedAt") val updatedAt: String,
    @SerialName("Notes") val notes: String? = null,
)

@Serializable
data class TaskList(
    @SerialName("Id") val id: String,
    @SerialName("Name") val name: String,
    @SerialName("AccentColor") val accentColor: String,
    @SerialName("IsPinned") val isPinned: Boolean = false,
    @SerialName("SortOrder") val sortOrder: Int = 0,
    @SerialName("CustomIcon") val customIcon: String? = null,
    @SerialName("IsDeleted") val isDeleted: Boolean = false,
    @SerialName("UpdatedAt") val updatedAt: String,
)

object SyncWire {
    val json: Json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
        explicitNulls = true
        prettyPrint = false
    }

    fun serialize(data: TasksFile): String = json.encodeToString(data)

    fun deserialize(payload: String): TasksFile = json.decodeFromString(payload)
}
