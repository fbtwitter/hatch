package dev.hatch.android

import android.content.Context

// Mirrors the Windows app's Settings → Appearance → Theme (light / dark / system default).
enum class ThemeMode { System, Light, Dark }

// Deliberately NOT loaded off the main thread, unlike LocalTaskStore and SyncKeyStore.
// Those were moved to Dispatchers.IO because they cost seconds; this is one key in a file
// of its own, and reading it late would repaint the whole app in the wrong theme first —
// a flash the user sees on every launch. Milliseconds here buy that away.
class AppPrefs(context: Context) {

    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    var themeMode: ThemeMode
        get() = runCatching { ThemeMode.valueOf(prefs.getString(THEME, null) ?: "") }
            .getOrDefault(ThemeMode.System)
        set(value) = prefs.edit().putString(THEME, value.name).apply()

    private companion object {
        const val PREFS = "hatch_prefs"
        const val THEME = "theme_mode"
    }
}
