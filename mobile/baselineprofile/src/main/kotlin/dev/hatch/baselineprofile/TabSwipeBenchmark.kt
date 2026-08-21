package dev.hatch.baselineprofile

import androidx.benchmark.macro.BaselineProfileMode
import androidx.benchmark.macro.CompilationMode
import androidx.benchmark.macro.FrameTimingMetric
import androidx.benchmark.macro.StartupMode
import androidx.benchmark.macro.junit4.MacrobenchmarkRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

// Frame timing across a bottom-nav tab drag — the newest and least-measured path in the app.
// The 4-tab HorizontalPager landed in b983c9b and the folder rework in 35ebb0e, and both were
// validated by hand and by screenshot only.
//
// This particular gesture is worth a benchmark rather than list scrolling: dragging between
// tabs composes two pages at once, and two of the four are full task lists, so it is the one
// interaction whose cost does not depend on how many tasks happen to exist. A scroll benchmark
// would measure almost nothing on a list too short to recycle.
@RunWith(AndroidJUnit4::class)
class TabSwipeBenchmark {

    @get:Rule
    val rule = MacrobenchmarkRule()

    @Test
    fun swipeBetweenTabs() = rule.measureRepeated(
        packageName = TARGET_PACKAGE,
        metrics = listOf(FrameTimingMetric()),
        iterations = 10,
        // WARM, not COLD: the subject is the gesture, and a cold start inside the measured
        // block would bury the frame times under process creation.
        startupMode = StartupMode.WARM,
        compilationMode = CompilationMode.Partial(
            baselineProfileMode = BaselineProfileMode.Require
        ),
        setupBlock = {
            pressHome()
            startActivityAndWait()
        },
    ) {
        // Out to Settings and back, so both directions are covered and the pager ends where
        // it started — otherwise each iteration would begin from a different tab and the
        // measurements would not be comparable to each other.
        repeat(3) { swipeToNextTab() }
        repeat(3) { swipeToPreviousTab() }
    }
}
