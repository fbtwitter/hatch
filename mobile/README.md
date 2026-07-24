# Hatch — mobile

Kotlin Multiplatform companion clients. See `docs/adr/0001` (strategy), `0003` (why this
lives in the same repository as the WinUI app) and `0006` (why the shared module holds no
state).

## Layout

```
shared/       commonMain — crypto, wire contract, Supabase client. Pure rules, no state.
androidApp/   Compose UI (Android). Written; not yet enabled — see below.
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

## Enabling the Android app

The Android module is complete but not included in the build, because configuring it
without an Android SDK fails and would take the shared module's tests down with it.

1. Install the Android SDK (Android Studio, or `cmdline-tools` + `sdkmanager`).
2. Uncomment `include(":androidApp")` in `settings.gradle.kts`.
3. Add `androidTarget()` to `shared/build.gradle.kts` alongside `jvm()`, with the
   `com.android.library` plugin and an `android { namespace; compileSdk }` block.
4. Create `local.properties` (gitignored) with your Supabase credentials:

```properties
sdk.dir=C\:\\Users\\<you>\\AppData\\Local\\Android\\Sdk
supabase.url=https://<project-ref>.supabase.co
supabase.key=<publishable key>
```

The URL may include the trailing `/rest/v1/` as `windows/Services/Secrets.cs` stores it —
`SyncClient.normalizeUrl` strips it, since supabase-kt appends that path itself.

5. Build:

```powershell
./gradlew :androidApp:assembleDebug
# APK: androidApp/build/outputs/apk/debug/androidApp-debug.apk
```

## Scope of the Android app today

Read-only proof-of-life: sign in, enter passphrase, pull, display. No writes, no
notifications, no offline store — those are later scope entries. The passphrase is held in
memory only and re-prompted every launch; ADR-0005 specifies Keystore-backed storage of the
derived key for the real companion.
