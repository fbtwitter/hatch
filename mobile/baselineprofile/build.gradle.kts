plugins {
    id("com.android.test") version "8.13.2"
    kotlin("android") version "2.4.10"
    id("androidx.baselineprofile") version "1.4.1"
}

android {
    namespace = "dev.hatch.baselineprofile"
    // Matches androidApp: the androidx wave in use declares aar-metadata requiring 36.
    compileSdk = 36

    defaultConfig {
        // Macrobenchmark's own floor is 23, but profile *generation* and the frame metrics
        // used here want 28+. The app itself still ships minSdk 26 — this module is a test
        // harness and is never packaged with it.
        minSdk = 28
        targetSdk = 36
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    targetProjectPath = ":androidApp"
    // The benchmark process instruments the app process rather than a separate one, which is
    // what lets a non-rooted device drive it.
    experimentalProperties["android.experimental.self-instrumenting"] = true
}

kotlin {
    jvmToolchain(17)
}

baselineProfile {
    // No AVDs and no system images exist on this machine, and none are needed: profile
    // generation dropped its root requirement for API 33+, and the target device is API 36.
    // A Gradle Managed Device would mean an AOSP image download purely to avoid a phone
    // that is already attached.
    useConnectedDevices = true
}

dependencies {
    implementation("androidx.test.ext:junit:1.3.0")
    implementation("androidx.test.uiautomator:uiautomator:2.3.0")
    implementation("androidx.benchmark:benchmark-macro-junit4:1.4.1")
}

// UTP leaves a java.util.logging lock file (`utp.N.log.lck`) in the results directory, and on
// Windows it stays locked after the run — so Gradle fails while snapshotting this task's
// outputs, *after* the instrumentation has already succeeded and the profile has been
// collected. The failure is in the bookkeeping, not the work. Gradle's own error message names
// the remedy, and the cost is nil here: this task talks to a physical device, so it is never
// legitimately up to date and there was no incremental reuse to lose.
tasks.matching { it.name.startsWith("connected") && it.name.endsWith("AndroidTest") }
    .configureEach { doNotTrackState("UTP leaves a locked .lck file Gradle cannot hash on Windows") }
