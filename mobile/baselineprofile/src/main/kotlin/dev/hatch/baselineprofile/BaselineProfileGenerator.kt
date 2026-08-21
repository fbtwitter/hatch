package dev.hatch.baselineprofile

import androidx.benchmark.macro.junit4.BaselineProfileRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

// Produces the list of hot methods that get AOT-compiled at install time. Compose's own AARs
// already ship profiles, but nothing covers Hatch's own code, and a sideloaded install — the
// only kind this app gets — AOT-compiles nothing else on its own.
//
// Generation runs on the connected phone: Macrobenchmark dropped the root requirement for
// API 33+, and the device is API 36. There is no emulator image on this machine to fall back
// to, which is why useConnectedDevices is on in build.gradle.kts.
@RunWith(AndroidJUnit4::class)
class BaselineProfileGenerator {

    @get:Rule
    val rule = BaselineProfileRule()

    // Split from the interaction pass below, rather than collected as one. The startup profile
    // is not just "the profile" — it additionally drives dex layout, packing the methods it
    // names together so a cold start touches fewer pages. Folding tab switches and list
    // scrolling into it spreads that layout across code the first frame never runs, which is
    // the opposite of the point. Collected as one, the two files came out byte-identical.
    @Test
    fun startup() = rule.collect(
        packageName = TARGET_PACKAGE,
        includeInStartupProfile = true,
    ) {
        pressHome()
        startActivityAndWait()
    }

    // The rest of what a session actually touches. Still AOT-compiled, just not allowed to
    // influence dex layout for the launch path.
    @Test
    fun interactions() = rule.collect(
        packageName = TARGET_PACKAGE,
        includeInStartupProfile = false,
    ) {
        pressHome()
        startActivityAndWait()

        // Walk the four tabs so their composables, not just My Day's, end up in the profile.
        // Tapping by label rather than by index: the bar is the app's own navigation spine
        // and its labels are stable, whereas coordinates would silently drift with layout.
        openTab("Lists")
        openTab("Summary")
        openTab("Settings")
        openTab("My Day")

        // The list is where the time actually goes, so its row composables and the scroll
        // machinery belong in the profile too.
        scrollTaskList()
    }
}
