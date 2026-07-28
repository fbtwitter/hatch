package dev.hatch.android

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.work.Data
import androidx.work.ExistingWorkPolicy
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.Worker
import androidx.work.WorkerParameters
import dev.hatch.sync.TodoItem
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.concurrent.TimeUnit

// Due-date reminders, scheduled on-device from the last decrypted pull (ADR-0002). No
// server push is possible — the server holds only an opaque envelope.

private const val CHANNEL_ID = "hatch_due"

// Due dates are stored at midnight, so a literal alarm would fire at 00:00.
private val REMINDER_TIME_OF_DAY: LocalTime = LocalTime.of(9, 0)

// One work name per task, so rescheduling replaces only that task's alarm.
private fun workName(taskId: String) = "hatch-due-$taskId"

fun ensureNotificationChannel(context: Context) {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
    val channel = NotificationChannel(
        CHANNEL_ID,
        "Due tasks",
        NotificationManager.IMPORTANCE_DEFAULT,
    ).apply { description = "Reminders for tasks with a due date" }
    context.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
}

// Rebuilt from the whole snapshot rather than tracked per edit, so it cannot drift.
fun rescheduleReminders(context: Context, tasks: List<TodoItem>) {
    val work = WorkManager.getInstance(context)
    val now = OffsetDateTime.now()

    for (task in tasks) {
        val name = workName(task.id)
        val due = localDateOf(task.dueDate)

        if (task.isCompleted || task.isDeleted || due == null) {
            work.cancelUniqueWork(name)
            continue
        }

        val fireAt = due.atTime(REMINDER_TIME_OF_DAY).atZone(ZoneId.systemDefault()).toOffsetDateTime()
        if (fireAt.isBefore(now)) {
            work.cancelUniqueWork(name)
            continue
        }

        val delayMs = fireAt.toInstant().toEpochMilli() - now.toInstant().toEpochMilli()
        val request = OneTimeWorkRequestBuilder<ReminderWorker>()
            .setInitialDelay(delayMs, TimeUnit.MILLISECONDS)
            .setInputData(
                Data.Builder()
                    .putString(ReminderWorker.KEY_TITLE, task.title)
                    .putInt(ReminderWorker.KEY_ID, task.id.hashCode())
                    .build()
            )
            .build()

        // REPLACE: editing a due date must move the alarm, not queue a second one.
        work.enqueueUniqueWork(name, ExistingWorkPolicy.REPLACE, request)
    }
}

// Explicit unschedule at the point of deletion. rescheduleReminders only ever walks the
// *live* task list, and a deleted task is filtered out of that list before persist() calls
// it — so a task's own alarm is never revisited once the task is gone, and would otherwise
// survive the delete and fire a reminder for a task that no longer exists. Mirrors
// NotificationSchedulerService.UnscheduleForTask, called at the same call sites on Windows.
fun cancelReminder(context: Context, taskId: String) {
    WorkManager.getInstance(context).cancelUniqueWork(workName(taskId))
}

class ReminderWorker(context: Context, params: WorkerParameters) : Worker(context, params) {

    override fun doWork(): Result {
        val title = inputData.getString(KEY_TITLE) ?: return Result.success()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(applicationContext, Manifest.permission.POST_NOTIFICATIONS)
            != PackageManager.PERMISSION_GRANTED
        ) {
            return Result.success()
        }

        ensureNotificationChannel(applicationContext)
        val notification = NotificationCompat.Builder(applicationContext, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.ic_popup_reminder)
            .setContentTitle("Due today")
            .setContentText(title)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .build()

        NotificationManagerCompat.from(applicationContext)
            .notify(inputData.getInt(KEY_ID, 0), notification)
        return Result.success()
    }

    companion object {
        const val KEY_TITLE = "title"
        const val KEY_ID = "id"
    }
}
