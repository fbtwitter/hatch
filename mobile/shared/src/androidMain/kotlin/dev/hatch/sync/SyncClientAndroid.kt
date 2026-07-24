package dev.hatch.sync

import android.content.Intent
import io.github.jan.supabase.auth.handleDeeplinks

// Platform glue, deliberately in androidMain rather than commonMain: an OAuth redirect is
// an Android Intent, and ADR-0006 keeps the common code free of platform APIs.
//
// Without this call the hatch://auth-callback redirect is received but never turned into a
// session, which presents as "the browser closed and nothing happened".
fun SyncClient.handleDeeplink(intent: Intent) {
    supabase.handleDeeplinks(intent)
}
