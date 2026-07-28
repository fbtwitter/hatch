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

## Scope of the Android app today

A full authoring client (ADR-0007), not a viewer:

- Local task list that works with no account and no network — add, complete, edit, delete.
- Task detail sheet: title, notes, due date, Important, My Day, priority, repeat, list,
  tags. Swipe a row to delete, with undo.
- Navigation drawer with the same smart lists as the WinUI nav rail — My Day, Important,
  Planned, All Tasks — plus custom lists (create, rename, pin, delete).
- Search across every list and both completion states.
- Completing a repeating task spawns its next occurrence.
- Opt-in sync: email/password or GitHub PKCE, TOTP two-factor with recovery codes, E2E
  encryption with the derived key held in the Keystore (ADR-0005), and record-level
  last-write-wins merge shared with Windows.
- Pull on foreground, every 5 minutes while open, and by pull-to-refresh on the task list
  when signed in; hourly background sync via WorkManager once signed in.
- Due-date reminders scheduled on-device from the last decrypted pull (ADR-0002).

Not built, and not planned for the companion: the mascot, the global capture hotkey, the tip
engine and the Summary page. Those are what still make Windows the primary client — see
ADR-0007, which is about permission, not reach.
