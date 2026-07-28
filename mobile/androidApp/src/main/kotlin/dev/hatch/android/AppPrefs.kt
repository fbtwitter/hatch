package dev.hatch.android

import android.content.Context

// Mirrors the Windows app's Settings → Appearance → Theme (light / dark / system default).
enum class ThemeMode { System, Light, Dark }

// Deliberately on the main thread, unlike LocalTaskStore and SyncKeyStore: one key, and
// reading it late would flash the wrong theme on every launch.
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
