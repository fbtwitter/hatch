import java.util.Properties

plugins {
    id("com.android.application") version "8.13.2"
    kotlin("android") version "2.4.10"
    id("org.jetbrains.kotlin.plugin.compose") version "2.4.10"
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
    implementation("androidx.compose.ui:ui-tooling-preview")
    debugImplementation("androidx.compose.ui:ui-tooling")

    implementation("androidx.activity:activity-compose:1.12.4")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.9.4")

    // supabase-kt needs an explicit Ktor engine per platform.
    implementation("io.ktor:ktor-client-okhttp:3.2.0")
}
