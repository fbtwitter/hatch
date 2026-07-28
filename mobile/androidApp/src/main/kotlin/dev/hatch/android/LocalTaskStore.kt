package dev.hatch.android

import android.content.Context
import dev.hatch.sync.SyncWire
import dev.hatch.sync.TasksFile
import java.io.File
import java.nio.file.Files
import java.nio.file.StandardCopyOption

// Mirrors windows/Services/TaskStorageService.cs: sole writer to tasks.json, atomic via
// temp + replace. In androidApp, not commonMain, because ADR-0006 keeps the shared module
// free of platform APIs.
class LocalTaskStore(context: Context) {

    // Lazy: keeps the filesDir hit off onCreate, where this is constructed.
    private val file by lazy { File(context.filesDir, "tasks.json") }

    // A missing file on first run is not an error.
    fun load(): TasksFile =
        if (!file.exists()) TasksFile()
        else runCatching { SyncWire.deserialize(file.readText()) }.getOrElse { TasksFile() }

    fun save(data: TasksFile) {
        val tmp = File(file.parentFile, "tasks.json.tmp")
        tmp.writeText(SyncWire.serialize(data))
        Files.move(
            tmp.toPath(),
            file.toPath(),
            StandardCopyOption.REPLACE_EXISTING,
            StandardCopyOption.ATOMIC_MOVE,
        )
    }
}
