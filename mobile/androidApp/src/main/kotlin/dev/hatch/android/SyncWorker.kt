package dev.hatch.android

import android.content.Context
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import dev.hatch.sync.PullResult
import dev.hatch.sync.PushResult
import dev.hatch.sync.SyncClient
import java.util.concurrent.TimeUnit

private const val SYNC_WORK_NAME = "hatch-periodic-sync"

// Scheduled at sign-in, cancelled at sign-out, never at first launch — sync is opt-in.
fun schedulePeriodicSync(context: Context) {
    val request = PeriodicWorkRequestBuilder<SyncWorker>(1, TimeUnit.HOURS)
        .setConstraints(
            Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()
        )
        .build()

    // KEEP: re-entering the app must not reset the period every time.
    WorkManager.getInstance(context)
        .enqueueUniquePeriodicWork(SYNC_WORK_NAME, ExistingPeriodicWorkPolicy.KEEP, request)
}

fun cancelPeriodicSync(context: Context) {
    WorkManager.getInstance(context).cancelUniqueWork(SYNC_WORK_NAME)
}

// A cold process with no ViewModel. The key comes from the Keystore rather than being
// re-derived — ADR-0005 exists so this path does not pay 600k PBKDF2 iterations per wake.
class SyncWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        val url = BuildConfig.SUPABASE_URL
        val key = BuildConfig.SUPABASE_KEY
        if (url.isEmpty() || key.isEmpty()) return Result.success()

        val store = LocalTaskStore(applicationContext)
        val syncKey = SyncKeyStore(applicationContext).load()
        val client = SyncClient(url, key)

        // Retry: no session usually means the restore lost a race.
        if (!client.awaitSession()) return Result.retry()

        val local = store.load()

        // pushMerged is read → merge → upload, so it moves data both ways. Without a key it
        // is refused by contract (§2), leaving a pull as all this process can do.
        val merged = if (syncKey != null) {
            when (val result = client.pushMerged(local, syncKey)) {
                is PushResult.Success -> result.merged
                // A passphrase, 2FA challenge or unreadable row needs a person.
                else -> return Result.success()
            }
        } else {
            when (val result = client.pull(null)) {
                is PullResult.Success -> result.data
                else -> return Result.success()
            }
        }

        store.save(merged)
        rescheduleReminders(applicationContext, merged.tasks)
        return Result.success()
    }
}
