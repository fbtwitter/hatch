# Architecture — Hatch

Hatch is a local-first Windows task manager built with WinUI 3. The always-on-top mascot
provides quick task capture; the main window handles task management and settings.

## Platform

- .NET 10, C# 14, Windows App SDK 2.5.1, WinUI 3.
- Windows 10 build 17763 or later and Windows 11.
- Release MSIX packages contain Native AOT executables for x64 and ARM64.
- Debug uses the regular .NET build path for development.

## Application structure

The Windows app is under `windows/`.

- `Views/` contains XAML pages and windows.
- `ViewModels/` owns presentation state and commands.
- `Services/` handles persistence, notifications, updates, and optional sync.
- `Hatch.Core/` contains portable task and sync logic without a WinUI dependency.
- `Models/` contains app data types.
- `Helpers/` contains platform adapters and shared utilities.

Views bind to ViewModels; services own asynchronous I/O. `TaskStorageService` is the only
writer to `tasks.json`. Settings are stored through `SettingsService`.
Task, settings and sync API JSON use generated serializer contexts. WinRT-facing types
and the widget COM factory use generated interop support.

## Data and sync

Tasks and settings are stored locally under `%LocalAppData%\Hatch\`. Sync is optional and
requires sign-in. Task data is encrypted on the device before upload. Startup and unhandled
exceptions may append to a local `crash.log`; Hatch has no remote crash reporting or analytics.
See the [privacy policy](../PRIVACY.md).

`SupabaseSyncClient` uses `HttpClient` for Hatch's authentication, MFA, recovery-code RPCs
and encrypted `user_data` row. It serializes session changes and refreshes, persists rotated
tokens through Credential Locker, and keeps saved sessions during transient network failures.
The generic Supabase SDK is not used because its serialization paths require reflection.

## Windows and release boundaries

`MainWindow` owns task-window presentation and coordinates with the separate
`MascotWindow`, which remains topmost. The package requests `internetClient` for optional
sync and update checks; it does not request `internetClientServer`.

A `v*.*.*` tag builds and signs the MSIX bundle, publishes a GitHub release, and updates the
App Installer feed. Microsoft Store submission is currently manual.
