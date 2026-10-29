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
Task, settings, calendar cache and sync API JSON use generated serializer contexts. WinRT-facing types
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

`CalendarService` provides optional Microsoft Graph and Google Calendar paths after an
explicit account connection. Both use read-only OAuth with PKCE and the system browser;
Google's Desktop flow receives its callback on a temporary IPv4 loopback socket. Refresh
tokens use provider-specific Credential Locker resources; access tokens stay in memory.
Accounts and projected events are cached in `calendar.json`, outside the task/sync contract.
Microsoft's primary calendar uses production delta updates; its other owned calendars and
Google's owned calendars use bounded snapshots. Refresh runs on foreground activation and
at five-minute intervals while the process runs. Release builds can receive Google OAuth
client configuration from GitHub Actions secrets; public access depends on Google verification.

`CalendarNotificationSchedulerService` reconciles deterministic calendar occurrence tags
with Windows' scheduled toast queue. Task reminders clean up only task/snooze tags. Cached
calendar reminders can outlive the process; unsynced provider changes can make them stale.
Already passed ten-minute triggers do not create catch-up notifications, and all-day events
stay silent. Planned is the shared destination for calendar events and task deadlines,
defaulting to today's Calendar/day view. Meetings and Tasks due retain distinct controls
and data; Agenda groups upcoming meetings and task deadlines, including overdue tasks.
Meeting rows reveal source and links on selection; only the next/ongoing linked meeting
offers a direct Join action. Earlier meetings are collapsed and muted. Settings -> Calendars
groups import, connection management, freshness and reminder limitations; a Calendar settings
button opens that category directly from Planned. Its management panel is created only
while the category is selected. Legacy calendar
activation links route to Planned; the separate Calendar sidebar entry is removed.

Manual `.ics` import uses the native desktop file picker and a bounded BCL-only RFC5545
reader. Imported sources use a distinct `CalendarProvider.Ics` cache entry, without tokens,
provider refresh or network access. The original file is read only; only display fields
and safe links are projected into the existing calendar cache and reminder pipeline.
Snapshots cover the same date window at import time. Explicit replacement preserves the
source identity and replaces its events; duplicate file names require replacement instead
of creating another source. Unsupported recurrence/timezone forms reject the whole import
without replacing existing data. Debug Microsoft OAuth uses `hatch-dev://` so an installed
Store build cannot receive its callback; Release retains `hatch://`.

## Windows and release boundaries

`MainWindow` owns task-window presentation and coordinates with the separate
`MascotWindow`, which remains topmost. The package requests `internetClient` for optional
sync, connected calendar access and update checks; it does not request `internetClientServer`.

A `v*.*.*` tag builds and signs the MSIX bundle, publishes a GitHub release, and updates the
App Installer feed. Microsoft Store submission is currently manual.
