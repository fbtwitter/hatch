package dev.hatch.android

import android.content.Context
import android.os.StrictMode

// Mirrors the Windows app's Settings → Appearance → Theme (light / dark / system default).
enum class ThemeMode { System, Light, Dark }

// Deliberately on the main thread, unlike LocalTaskStore and SyncKeyStore: two keys, and
// reading them late would flash the wrong theme on every launch.
class AppPrefs(context: Context) {

    // The exemption lives here, at the class whose whole reason for existing on the main
    // thread is documented above, rather than at each call site — a caller reading a theme
    // preference should not have to know it touches a disk.
    //
    // `.all` is not a redundant read. SharedPreferences opens the file eagerly but defers
    // parsing it to the first value access, so without this the disk hit lands on whichever
    // property is read first, outside this block — which is exactly where StrictMode caught
    // it. Forcing the load here means the read happens once, deliberately, and every ordinary
    // property get afterwards is memory-only.
    private val prefs = allowingDiskReads {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).also { it.all }
    }

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

// StrictMode's thread policy has no lambda form in the platform API, so this is the standard
// permit-then-restore dance. Used for the one main-thread disk read this app makes on purpose
// (see the class comment above): declaring the exemption where it happens keeps disk-read
// detection switched on everywhere else, which is where an accidental read would actually be a
// bug. Outside a debug build no policy is installed, so this is a pair of cheap no-ops.
inline fun <T> allowingDiskReads(body: () -> T): T {
    val restore = StrictMode.allowThreadDiskReads()
    return try {
        body()
    } finally {
        StrictMode.setThreadPolicy(restore)
    }
}
