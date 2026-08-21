package dev.hatch.baselineprofile

import androidx.benchmark.macro.BaselineProfileMode
import androidx.benchmark.macro.CompilationMode
import androidx.benchmark.macro.StartupMode
import androidx.benchmark.macro.StartupTimingMetric
import androidx.benchmark.macro.junit4.MacrobenchmarkRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

// Cold start, measured rather than felt. This exists because the last performance verdict on
// this app was "the release build feels smoother" — true, but not a number, and not something
// a later change can be checked against.
//
// It is also the direct test of a real suspicion: MainActivity holds the splash screen until
// the first disk read completes (`setKeepOnScreenCondition { !loaded }`), so any work added
// ahead of that shows up here as a longer splash rather than as a visibly slow app. That is
// exactly how the main-thread SyncClient construction hid for as long as it did.
@RunWith(AndroidJUnit4::class)
class StartupBenchmark {

    @get:Rule
    val rule = MacrobenchmarkRule()

    // The number that matters, because it is what a user who installs the app actually gets:
    // R8-minified and AOT-compiled from the committed profile.
    @Test
    fun startupWithBaselineProfile() = measure(
        CompilationMode.Partial(baselineProfileMode = BaselineProfileMode.Require)
    )

    // The control: nothing pre-compiled at all, which is what a fresh install without a
    // profile actually gets. On its own the number above says nothing — this is the
    // comparison that shows whether the profile earns its place, and whether it still does
    // after the app's hot paths move.
    //
    // `None()` and not `Partial(Disable)`. Partial with the profile disabled requires
    // warmupIterations > 0 ("Must set baselineProfileMode != Ignore, or warmup iterations > 0
    // to define which portion of the app to pre-compile"), and warmup launches JIT-compile the
    // app against the very run being measured — a control tuned by the thing it is supposed to
    // be a baseline for. Measured that way the "unprofiled" start came out *faster* than the
    // profiled one, which says nothing about the profile and everything about the control.
    @Test
    fun startupWithoutBaselineProfile() = measure(CompilationMode.None())

    private fun measure(compilationMode: CompilationMode) = rule.measureRepeated(
        packageName = TARGET_PACKAGE,
        metrics = listOf(StartupTimingMetric()),
        // Enough for a median to settle without turning a check into a coffee break; cold
        // starts are the slowest thing being measured here.
        iterations = 10,
        startupMode = StartupMode.COLD,
        compilationMode = compilationMode,
        setupBlock = { pressHome() },
    ) {
        startActivityAndWait()
    }
}
