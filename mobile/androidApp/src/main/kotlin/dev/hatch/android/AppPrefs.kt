package dev.hatch.android

import android.content.Context

// Mirrors the Windows app's Settings → Appearance → Theme (light / dark / system default).
enum class ThemeMode { System, Light, Dark }

// Deliberately on the main thread, unlike LocalTaskStore and SyncKeyStore: two keys, and
// reading them late would flash the wrong theme on every launch.
class AppPrefs(context: Context) {

    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    var themeMode: ThemeMode
        get() = runCatching { ThemeMode.valueOf(prefs.getString(THEME, null) ?: "") }
            .getOrDefault(ThemeMode.System)
        set(value) = prefs.edit().putString(THEME, value.name).apply()

    // Material You, off by default. Hatch has its own colour (#0078D4, the app icon and the
    // Windows accent), and a wallpaper-derived palette overrides it with something that may
    // carry no meaning at all — on a greyscale wallpaper the whole app arrives grey, overdue
    // included. Opt-in rather than absent: reading the wallpaper is a real Android pleasure,
    // it just should not be the thing that decides what "overdue" looks like.
    var useDynamicColor: Boolean
        get() = prefs.getBoolean(DYNAMIC_COLOR, false)
        set(value) = prefs.edit().putBoolean(DYNAMIC_COLOR, value).apply()

    private companion object {
        const val PREFS = "hatch_prefs"
        const val THEME = "theme_mode"
        const val DYNAMIC_COLOR = "dynamic_color"
    }
}
