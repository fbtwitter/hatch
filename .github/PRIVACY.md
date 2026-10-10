# Privacy implementation notes

The user-facing policy is [PRIVACY.md](../PRIVACY.md). These notes
describe the app behavior that policy must reflect.

- Tasks and settings are written to `%LocalAppData%\Hatch\`; optional sync uploads an encrypted
  task and list payload, not the settings file.
- Sync requires explicit sign-in and then runs automatically until sign-out. Sync tokens and
  the passphrase are stored in Windows Credential Locker.
- Hatch has no analytics, telemetry, or remote crash-reporting service.
- Startup and unhandled exceptions may write diagnostic text to
  `%LocalAppData%\Hatch\crash.log`; it may contain task data in exception details and is not
  sent automatically. Developers may separately enable local Windows crash dumps.
- The app uses the `internetClient` capability for optional sync and update checks. It does
  not request `internetClientServer`.
- A manual update check runs when the user requests it. Windows App Installer manages
  background update checks for installations registered through the App Installer feed.
- Store review and GitHub issue links open only after the user selects those actions.
- Settings has sign-out, but no in-app control to erase all local files or the remote sync row.
  The privacy policy must describe manual local deletion and remote deletion requests accurately.

Keep product claims aligned with these implementation details. Do not add analytics,
automatic diagnostics uploads, or network requests unrelated to sync and update delivery.
