package dev.hatch.android

import android.content.Context
import dev.hatch.sync.SyncWire
import dev.hatch.sync.TasksFile
import java.io.File
import java.nio.file.Files
import java.nio.file.StandardCopyOption

// Mirrors windows/Services/TaskStorageService.cs: the sole writer to tasks.json, writing
// atomically via a temp file + replace. A phone is killed mid-write routinely where a
// desktop rarely is, so the atomic path matters more here, not less.
//
// Lives in androidApp rather than commonMain because file I/O is platform work and
// ADR-0006 keeps the shared module free of state and platform APIs.
class LocalTaskStore(context: Context) {

    private val file = File(context.filesDir, "tasks.json")

    // A missing file on first run is not an error — same contract as the Windows app.
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
