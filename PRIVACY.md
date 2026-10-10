# Privacy Policy — Hatch: Always-On To-Do

**Publisher:** Reza Fauzi Augusdi  
**Last updated:** October 8, 2026
**Contact:** https://github.com/fbtwitter/hatch/issues

---

## The Short Version

Hatch does not run analytics or sell personal data. By default, your tasks stay on your
device. If you opt into sync, task and list data is encrypted on your device before it is
uploaded. Sync is optional and is not required to use Hatch.

---

## What We Store and Where

Hatch saves these JSON files on your device:

- **tasks.json** — your tasks and custom lists
- **settings.json** — your theme preference, mascot position, and app preferences

Both files are stored in `%LocalAppData%\Hatch\` on your Windows device. If you enable sync,
Hatch encrypts task and list data on your device and uploads the encrypted payload. The
`settings.json` file is not uploaded. Sync session tokens and your sync passphrase are stored
in Windows Credential Locker; older settings files that contain tokens are migrated when
loaded.

---

## Optional Sync

If you choose to enable sync (Settings → Sync), Hatch uses Supabase for account authentication
and storage of your encrypted task and list data. Your email address and account metadata are
handled by that service. Sync:

- Is **off by default** and requires explicit sign-in to activate
- Uploads an encrypted task and list payload; Supabase cannot read the task content without
  your sync passphrase
- Runs automatically after sign-in until you sign out
- Can be stopped by signing out in Settings

Signing out clears Hatch's locally stored sync credentials. It does not delete the encrypted
remote copy.

## Local Diagnostics

If Hatch fails during startup or encounters an unhandled exception, it may write diagnostic
details to `%LocalAppData%\Hatch\crash.log`. This file stays on your device and is not sent
automatically. Exception details may contain task data. During development, you can optionally
enable Windows crash dumps for failures that this log cannot capture. Dumps stay on your device
and may also contain task data.

---

## Analytics and Telemetry

**None.** Hatch contains no analytics SDK, no remote crash reporter, no feature-flag system, and no usage pings of any kind. We have no way to know how you use the app.

---

## Internet Access

Hatch uses internet access for optional sync, manual update checks, and links you choose to
open. Sync requires sign-in and then runs automatically. A manual update check runs only when
you press its button.
Installations registered through the App Installer feed can receive background update checks
managed by Windows App Installer. These checks do not upload your tasks.

---

## Data Sharing

Hatch does not sell data or send it to advertisers or analytics providers. If you enable sync,
Supabase processes your account information and stores the encrypted task and list payload.

---

## Data Deletion

- **Local files:** Close Hatch, then delete `%LocalAppData%\Hatch\` to remove tasks, settings,
  and local crash logs. Uninstalling Hatch may leave this folder behind.
- **Local sync credentials:** Signing out in Settings clears Hatch's saved session tokens and
  sync passphrase from Windows Credential Locker.
- **Remote sync data:** Signing out or deleting local files does not delete the encrypted
  payload stored by Supabase. Hatch currently has no in-app remote deletion control. Open a
  [support issue](https://github.com/fbtwitter/hatch/issues) to request deletion; do not post
  passwords, recovery codes, or task content publicly.

---

## Children's Privacy

Hatch does not knowingly collect data from children. The app contains no social features, no user-generated public content, and no communication features.

---

## Changes to This Policy

If this policy changes, the updated version will be published at this URL with a new "Last updated" date. We will not make changes that reduce your privacy rights without clear notice.

---

## Contact

For questions or concerns about this privacy policy, please open an issue at:  
https://github.com/fbtwitter/hatch/issues
