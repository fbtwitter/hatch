import java.util.Properties

plugins {
    id("com.android.application") version "8.13.2"
    kotlin("android") version "2.4.10"
    id("org.jetbrains.kotlin.plugin.compose") version "2.4.10"
    // Consumes :baselineprofile. Creates the nonMinifiedRelease / benchmarkRelease variants
    // the generator and the benchmarks run against, both derived from `release`.
    id("androidx.baselineprofile") version "1.4.1"
}

// Supabase credentials come from mobile/local.properties, which is gitignored — mirroring
// windows/Services/Secrets.cs. Absent values compile to empty strings so a fresh clone
// still builds; the app reports the missing config at runtime rather than failing the build.
val localProps = Properties().apply {
    val file = rootProject.file("local.properties")
    if (file.exists()) file.inputStream().use { load(it) }
}

android {
    namespace = "dev.hatch.android"
    // 36 is a floor, not a preference: AndroidX artifacts pulled by the Compose BOM
    // declare aar-metadata requiring compileSdk >= 36.
    compileSdk = 36

    defaultConfig {
        applicationId = "dev.hatch.android"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"

        buildConfigField("String", "SUPABASE_URL", "\"${localProps.getProperty("supabase.url", "")}\"")
        buildConfigField("String", "SUPABASE_KEY", "\"${localProps.getProperty("supabase.key", "")}\"")
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    buildTypes {
        release {
            // The libraries in use (kotlinx-serialization, supabase-kt, Ktor, OkHttp) all
            // ship consumer keep rules, so proguard-rules.pro stays near-empty — add to it
            // only from R8's own missing-rules output, never speculatively.
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }

        // The baseline-profile plugin derives `nonMinifiedRelease` (profile generation) and
        // `benchmarkRelease` (measurement) from `release` after this block is evaluated, so
        // they are reached through the live collection rather than declared here. Both need to
        // be installable and neither may be debuggable — Macrobenchmark refuses a debuggable
        // build, and rightly: debug overhead is what made this app "feel laggy" once already.
        //
        // Only these two get a signing config. `release` itself stays unsigned: signing it is
        // a distribution decision, not a benchmarking one, and a previous session had to add
        // and immediately revert exactly that.
        all {
            if (name == "benchmarkRelease" || name == "nonMinifiedRelease") {
                signingConfig = signingConfigs.getByName("debug")
            }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    jvmToolchain(17)
}

// Without this the shared module's data classes are inferred unstable, so every visible
// task row recomposes on any state change. See mobile/compose_stability.conf.
composeCompiler {
    stabilityConfigurationFiles.add(
        rootProject.layout.projectDirectory.file("compose_stability.conf")
    )

    // Compose compiler metrics/reports are deliberately NOT wired up here. metricsDestination
    // and reportsDestination were tried, exactly as documented, and on this toolchain
    // (Kotlin 2.4.10, AGP 8.13.2) the compiler creates a single empty file named after the
    // root project and writes no report at all — so the flag would look like it worked while
    // proving nothing. See context/current-feature.md. Retry after a toolchain bump, or fall
    // back to passing the plugin option through freeCompilerArgs.
}

dependencies {
    implementation(project(":shared"))

    // Pinned to the last androidx wave targeting compileSdk 36. Newer versions declare
    // aar-metadata requiring 37, which AGP 8.13.2 does not support.
    implementation(platform("androidx.compose:compose-bom:2025.12.01"))
    implementation("androidx.compose.material3:material3")
    // Icons only — the material (M2) components themselves are deliberately not pulled in.
    implementation("androidx.compose.material:material-icons-core")
    implementation("androidx.compose.ui:ui")
    // No @Preview composables exist in this codebase, so ui-tooling-preview (which ships in
    // every variant, including release) would be pure weight. ui-tooling stays — it's
    // debug-only and keeps Android Studio's Layout Inspector working.
    debugImplementation("androidx.compose.ui:ui-tooling")

    implementation("androidx.activity:activity-compose:1.12.4")
    // Bottom-bar navigation (My Day / All Tasks / Summary / Settings). String routes only —
    // no kotlinx-serialization type-safe routes, so this doesn't need the serialization
    // Gradle plugin on top of what :shared already pulls in for SyncWire.
    implementation("androidx.navigation:navigation-compose:2.9.8")
    // Android-12-style icon splash on API 26–31, and keep-on-screen until the first disk
    // read lands — without it those releases cold-start on a bare white window.
    implementation("androidx.core:core-splashscreen:1.2.0")
    // Compose AARs carry baseline profiles, but a sideloaded install — the only way this
    // app is installed — never AOT-compiles them unless this installer is present.
    implementation("androidx.profileinstaller:profileinstaller:1.4.1")
    // WindowCompat, for making the system-bar icons follow the in-app theme rather than
    // the system one. Already arrives transitively; declared because Theme.kt uses it.
    implementation("androidx.core:core-ktx:1.16.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.9.4")
    // LifecycleResumeEffect — foreground/background is what gates the auto-pull loop, and
    // a raw Activity callback cannot be observed from a composable.
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.9.4")

    // Background sync + due-date reminders (ADR-0002: scheduled on-device, never pushed).
    implementation("androidx.work:work-runtime-ktx:2.10.5")

    // supabase-kt needs an explicit Ktor engine per platform.
    implementation("io.ktor:ktor-client-okhttp:3.2.0")

    // Generates src/release/generated/baselineProfiles/. profileinstaller above is what
    // actually applies it at install time on a sideloaded build.
    baselineProfile(project(":baselineprofile"))
}
