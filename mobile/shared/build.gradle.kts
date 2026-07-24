plugins {
    kotlin("multiplatform") version "2.4.10"
    kotlin("plugin.serialization") version "2.4.10"
    id("com.android.library") version "8.13.2"
}

kotlin {
    jvmToolchain(17)

    // jvm() is kept as the fast test target: the wire-contract tests run on any machine
    // with a JDK, no emulator. iOS targets are still undeclared — no Mac (ADR-0001).
    jvm()
    androidTarget()

    sourceSets {
        commonMain.dependencies {
            implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.11.0")
            // BOM-pinned: supabase-kt pulls cryptography 0.6.0 transitively, and a version
            // skew between the compile and runtime classpaths surfaces as NoSuchMethodError.
            implementation(project.dependencies.platform("dev.whyoleg.cryptography:cryptography-bom:0.6.0"))
            implementation("dev.whyoleg.cryptography:cryptography-core")
            implementation(project.dependencies.platform("io.github.jan-tennert.supabase:bom:3.7.0"))
            implementation("io.github.jan-tennert.supabase:auth-kt")
            implementation("io.github.jan-tennert.supabase:postgrest-kt")
        }
        commonTest.dependencies {
            implementation(kotlin("test"))
        }
        jvmMain.dependencies {
            implementation("dev.whyoleg.cryptography:cryptography-provider-jdk")
            // supabase-kt needs an explicit Ktor engine per platform.
            implementation("io.ktor:ktor-client-cio:3.2.0")
        }
        androidMain.dependencies {
            // The JDK provider is the correct one on Android too — it delegates to JCA.
            implementation("dev.whyoleg.cryptography:cryptography-provider-jdk")
            implementation("io.ktor:ktor-client-okhttp:3.2.0")
        }
    }
}

android {
    namespace = "dev.hatch.sync"
    compileSdk = 36
    defaultConfig {
        minSdk = 26
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

tasks.withType<Test>().configureEach {
    testLogging {
        events("passed", "failed", "skipped")
        showStandardStreams = true
    }
}
