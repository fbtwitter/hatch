plugins {
    kotlin("multiplatform") version "2.1.21"
    kotlin("plugin.serialization") version "2.1.21"
}

kotlin {
    jvmToolchain(17)

    // JVM only for the spike: proving the envelope framing and the wire contract needs no
    // Android SDK and no Mac. Sources still live in commonMain so platform types cannot
    // leak in. androidTarget and the iOS targets land with the first real UI.
    jvm()

    sourceSets {
        commonMain.dependencies {
            implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.8.1")
            implementation("dev.whyoleg.cryptography:cryptography-core:0.4.0")
        }
        commonTest.dependencies {
            implementation(kotlin("test"))
        }
        jvmMain.dependencies {
            implementation("dev.whyoleg.cryptography:cryptography-provider-jdk:0.4.0")
        }
    }
}

tasks.withType<Test>().configureEach {
    testLogging {
        events("passed", "failed", "skipped")
        showStandardStreams = true
    }
}
