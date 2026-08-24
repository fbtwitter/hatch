# Hatch — mobile

Kotlin Multiplatform companion clients. See `docs/adr/0001` (strategy), `0003` (why this
lives in the same repository as the WinUI app) and `0006` (why the shared module holds no
state).

## Layout

```
shared/       commonMain — crypto, wire contract, Supabase client. Pure rules, no state.
androidApp/   Compose UI (Android). Built and included in the Gradle build.
iosApp/       SwiftUI. Not started; needs a Mac (ADR-0001 delivery status).
```

## Running the tests

```powershell
./gradlew :shared:jvmTest
```

These are the executable half of the wire contract. They reproduce the envelope test vector
in `docs/sync-protocol.md` §3 byte-for-byte and parse
`windows/Hatch.Tests.Unit/Fixtures/tasks-golden.json` **in place** — the fixture is
deliberately never copied here, so the C# and Kotlin implementations cannot drift apart.

## Building the Android app

`:androidApp` is in `settings.gradle.kts` and `shared` declares `androidTarget()`, so the
only setup left is the SDK and your credentials.

1. Install the Android SDK (Android Studio, or `cmdline-tools` + `sdkmanager`).
2. Create `local.properties` (gitignored) with your Supabase credentials:

```properties
sdk.dir=C\:\\Users\\<you>\\AppData\\Local\\Android\\Sdk
supabase.url=https://<project-ref>.supabase.co
supabase.key=<publishable key>
```

The URL may include the trailing `/rest/v1/` as `windows/Services/Secrets.cs` stores it —
`SyncClient.normalizeUrl` strips it, since supabase-kt appends that path itself.

3. Build:

```powershell
./gradlew :androidApp:assembleDebug
# APK: androidApp/build/outputs/apk/debug/androidApp-debug.apk
```

**Run Gradle from PowerShell, not Git Bash.** Git Bash exports `JAVA_HOME` to an sdkman path
that does not resolve on this machine, and Gradle's toolchain detection then fails to find
the JDK 17 it needs — reported as "Cannot find a Java installation … matching
{languageVersion=17}", which looks like a missing JDK rather than a shell-environment problem.

## Release builds

```powershell
./gradlew :androidApp:assembleRelease
# APK: androidApp/build/outputs/apk/release/androidApp-release-unsigned.apk
```

Release builds are minified and resource-shrunk by R8 (~2.7 MB vs ~12 MB unminified).
`proguard-rules.pro` is near-empty on purpose: the libraries ship their own keep rules, and
new rules are added only from R8's missing-rules output. The obfuscation map lands in
`androidApp/build/outputs/mapping/release/mapping.txt` — keep it alongside any APK you hand
out, or a crash stack from that build cannot be read.

The APK is unsigned. For on-device testing, sign with the debug keystore:

```powershell
apksigner sign --ks $env:USERPROFILE\.android\debug.keystore --ks-pass pass:android `
  --key-pass pass:android --ks-key-alias androiddebugkey <apk>
```

Two warning families during `assembleRelease` are cosmetic, share one root cause — the AGP
8.13.2 toolchain (bundled lint and R8) predates Kotlin 2.4 — and will disappear with the
compileSdk 37 toolchain upgrade:

- `e: … kotlin_module … metadata is 2.4.0, expected version is 2.2.0` from
  `lintVitalAnalyzeRelease` (lint's embedded Kotlin 2.2 frontend reading 2.4.10 binaries).
- `WARNING: R8: An error occurred when parsing kotlin metadata` — R8 cannot rewrite
  `kotlin.Metadata` annotations it cannot parse. Harmless here: the app ships no
  kotlin-reflect and every serializer is compile-time generated.


## Baseline profile and benchmarks

`:baselineprofile` is a `com.android.test` module holding the profile generator and two
Macrobenchmarks. The generated profile is **committed** — `androidApp/src/release/generated/
baselineProfiles/` — because generating it needs a device, and nothing in CI has one.

Regenerate it after a significant change to startup or navigation. A stale profile degrades
quietly rather than breaking anything, so this is not part of an ordinary build:

```powershell
./gradlew :androidApp:generateBaselineProfile
```

Then run the benchmarks to check the change was worth it:

```powershell
# cold start, with the profile and with no AOT at all
./gradlew :baselineprofile:connectedBenchmarkReleaseAndroidTest `
  "-Pandroid.testInstrumentationRunnerArguments.class=dev.hatch.baselineprofile.StartupBenchmark"

# frame timing across a bottom-nav tab drag
./gradlew :baselineprofile:connectedBenchmarkReleaseAndroidTest `
  "-Pandroid.testInstrumentationRunnerArguments.class=dev.hatch.baselineprofile.TabSwipeBenchmark"

# results: baselineprofile/build/outputs/connected_android_test_additional_output/
#          benchmarkRelease/connected/<device>/*-benchmarkData.json
```

> **Benchmarking can erase the app's data on the device it runs against.**
> These tasks install and swap between the `debug`, `nonMinifiedRelease` and `benchmarkRelease`
> variants. Matching signing keys are not enough to guarantee an in-place update — a cycle of
> them uninstalled and reinstalled the app on 2026-08-21, which deleted `tasks.json` along with
> the sync tokens and the Keystore-wrapped sync key. Verify with `adb shell dumpsys package
> dev.hatch.android | grep InstallTime`: if `firstInstallTime` equals `lastUpdateTime`, the data
> directory was recreated.
>
> So: never benchmark against a device holding the only copy of anything. Sign in and push
> first, or export from Settings, and treat re-signing in and pulling as part of the routine
> afterwards.

Both run on a connected phone. No emulator or root is needed — profile generation dropped the
root requirement at API 33, and Macrobenchmark refuses a debuggable build, so the plugin's
`benchmarkRelease` and `nonMinifiedRelease` variants are the only ones it will touch. Those two
are debug-signed purely so they can be installed; `release` itself stays unsigned.

Two things to know before trusting a run:

- **Stop the Gradle daemon between runs if a task fails on a locked file.** UTP leaves
  `utp.N.log.lck` and a per-test logcat behind and Windows keeps them open, which shows up as
  `FileSystemException ... being used by another process` *after* the tests have already
  passed. `./gradlew --stop`, delete `baselineprofile/build/outputs/androidTest-results`, rerun.
- **`CompilationMode.None()` is the control, not `Partial(Disable)`.** The latter needs warmup
  iterations, and warmup JIT-compiles the app against the run being measured — which made the
  "unprofiled" start measure *faster* than the profiled one.

## Scope of the Android app today

A full authoring client (ADR-0007), not a viewer:

- Local task list that works with no account and no network — add, complete, edit, delete.
- Task detail sheet: title, notes, due date (with the four Windows presets), Important,
  My Day, priority, repeat, list, tags. Swipe a row to delete, with undo.
- A bottom navigation bar as the navigation spine — My Day, Lists, Summary, Settings. The
  Lists tab browses the same smart lists as the WinUI nav rail (All Tasks, Important,
  Planned) plus custom lists (create, rename, pin, delete), and opens each as its own
  destination, so every screen in the app highlights exactly one tab.
- My Day suggestions: everything still open that today has not claimed, one tap to add.
- Planned grouped by Overdue / Today / Tomorrow / This week / Later, as on Windows.
- Tap a tag chip on a row to filter the list by that tag.
- Search across every list and both completion states.
- Export a copy as JSON, CSV or Markdown through the system document picker.
- Hatch's own palette (seeded from `#0078D4`, the app icon's blue) on every Android version,
  with light/dark/system and an optional Material You switch in Settings → Appearance. Colour
  carries meaning rather than decoration: overdue is the error tone, starred is gold, and a
  task's priority tints its checkbox.
- Completing a repeating task spawns its next occurrence.
- Opt-in sync: email/password or GitHub PKCE, TOTP two-factor with recovery codes, E2E
  encryption with the derived key held in the Keystore (ADR-0005), and record-level
  last-write-wins merge shared with Windows.
- Pull on foreground, every 5 minutes while open, and by pull-to-refresh on the task list
  when signed in; hourly background sync via WorkManager once signed in.
- Due-date reminders scheduled on-device from the last decrypted pull (ADR-0002).

Not built, and not planned for the companion: the mascot, the global capture hotkey and the
tip engine. Those are desktop paradigms and are what still make Windows the primary client —
see ADR-0007, which is about permission, not reach. (The Summary page was in this list until
it shipped as a tab.)

Still Windows-only, but portable in principle: MFA *enrolment* (the phone can answer a
challenge and redeem a recovery code, but not set two-factor up), and the use-local /
use-server / merge conflict-resolution dialog on a fresh sign-in.
