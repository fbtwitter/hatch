package dev.hatch.baselineprofile

import androidx.benchmark.macro.MacrobenchmarkScope
import androidx.test.uiautomator.By
import androidx.test.uiautomator.Until

const val TARGET_PACKAGE = "dev.hatch.android"

private const val TIMEOUT_MS = 5_000L

// Shared by the generator and the benchmarks so both drive the app the same way — a profile
// collected over one path and a measurement taken over another would not describe each other.

// Tab labels are matched exactly. `text` rather than `desc`: NavigationBarItem renders its
// label as real text and carries no separate content description. "Lists" and "Summary" also
// appear as top-app-bar titles, so this deliberately clicks the *last* match — the bottom bar
// is below the bar it might collide with.
fun MacrobenchmarkScope.openTab(label: String) {
    device.wait(Until.hasObject(By.text(label)), TIMEOUT_MS)
    val matches = device.findObjects(By.text(label))
    if (matches.isEmpty()) return
    matches.maxByOrNull { it.visibleBounds.centerY() }?.click()
    device.waitForIdle()
}

// Raw device swipes rather than UiObject2.fling/scroll on purpose. Those wait for a scroll
// accessibility event and throw a 5s TimeoutException per call when none arrives — which is
// exactly what happens here, because a list of a dozen tasks is barely longer than the screen.
// The gesture still exercises the scroll path either way; the only difference is fifteen
// seconds of waiting for events that were never coming.
//
// Kept clear of the bottom edge: the composer sits there, and on a gesture-nav device the
// system takes the very bottom of the screen for itself.
fun MacrobenchmarkScope.scrollTaskList() {
    val w = device.displayWidth
    val h = device.displayHeight
    repeat(2) {
        device.swipe(w / 2, h * 3 / 4, w / 2, h / 4, 8)
        device.waitForIdle()
    }
    device.swipe(w / 2, h / 4, w / 2, h * 3 / 4, 8)
    device.waitForIdle()
}

// A horizontal drag across the middle of the screen, which is what the outer bottom-nav pager
// listens to. Deliberately a drag and not a tab tap: tapping calls scrollToPage and lands
// instantly, while the drag is the path that composes two task lists at once — the thing
// worth measuring.
fun MacrobenchmarkScope.swipeToNextTab() {
    val w = device.displayWidth
    val h = device.displayHeight
    device.swipe(w * 4 / 5, h / 2, w / 5, h / 2, 10)
    device.waitForIdle()
}

fun MacrobenchmarkScope.swipeToPreviousTab() {
    val w = device.displayWidth
    val h = device.displayHeight
    device.swipe(w / 5, h / 2, w * 4 / 5, h / 2, 10)
    device.waitForIdle()
}
