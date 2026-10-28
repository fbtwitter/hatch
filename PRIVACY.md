# Privacy Policy — Hatch: Always-On To-Do

**Publisher:** Reza Fauzi Augusdi  
**Last updated:** October 26, 2026
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
- **calendar.json** — connected calendar accounts and cached event display fields, when
  you use the optional calendar integration

These files are stored in `%LocalAppData%\Hatch\` on your Windows device. If you enable sync,
Hatch encrypts task and list data on your device and uploads the encrypted payload. The
`settings.json` file is not uploaded. Sync session tokens and your sync passphrase are stored
in Windows Credential Locker; older settings files that contain tokens are migrated when
loaded.

The calendar cache is local and is not included in task sync. Calendar refresh tokens are
stored separately in Windows Credential Locker; calendar access tokens stay in memory.

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

## Optional Calendar Connections

In builds that support calendar connection, you can connect Microsoft or Google accounts
from Settings -> Calendars. Hatch requests read-only calendar access and contacts the selected
provider directly to read your owned calendars and event details, including meeting links. Hatch saves only
the event identity, calendar, title, times and links needed for its agenda. Calendar events
are separate from tasks and are not automatically added to My Day.

Hatch schedules local Windows reminders ten minutes before timed events. The notification
can contain the event title, calendar name, and links to open the event or meeting. All-day
events do not produce calendar reminders. Already scheduled reminders can appear after
Hatch exits, based on the last saved calendar state.

After connection, Hatch refreshes calendar data when opened or activated and every five
minutes while it runs. Failed refreshes leave the last saved events available. Disconnect
removes the account's local calendar cache, saved refresh token and pending calendar
reminders. It does not delete provider calendar events or revoke app consent. Consent
can be revoked through your Microsoft, Google or organization account settings.

Google Calendar data is used only for the agenda, local reminders and event links. Hatch
follows the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy),
including its Limited Use requirements. Calendar data is not uploaded to Hatch task sync,
sold, used for advertising, or used to train AI models.

## Manual Calendar Imports

You can import an iCalendar (.ics) file without signing in. Hatch reads the file locally
and saves its file name, calendar name, event identities, titles, times and safe event or
meeting links in `calendar.json`. It does not modify the original file, upload its contents,
or save the original file path, descriptions, attendee lists or attachments. Imported
events stay separate from tasks, My Day and task sync.

Imports are snapshots covering the previous seven days and next thirty days at import
time. They do not refresh automatically. Replacing an imported file replaces that source's
saved events and updates its local reminders. Removing the imported calendar removes its
saved events and pending reminders from Hatch.

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

Hatch uses internet access for optional sync, optional calendar access, manual
update checks, and links you choose to open. Sync and calendar access require explicit
sign-in or connection and then refresh automatically. A manual update check runs only when
you press its button.
Installations registered through the App Installer feed can receive background update checks
managed by Windows App Installer. These checks do not upload your tasks.

---

## Data Sharing

Hatch does not sell data or send it to advertisers or analytics providers. If you enable sync,
Supabase processes your account information and stores the encrypted task and list payload.
If you connect a Microsoft or Google calendar account, that provider processes authentication
and calendar API requests under its own service policies. Hatch does not upload your tasks
to Microsoft or Google.

---

## Data Deletion

- **Local files:** Close Hatch, then delete `%LocalAppData%\Hatch\` to remove tasks, settings,
  calendar cache and local crash logs. Uninstalling Hatch may leave this folder behind.
- **Local sync credentials:** Signing out in Settings clears Hatch's saved session tokens and
  sync passphrase from Windows Credential Locker.
- **Local calendar credentials:** Disconnecting an account in Settings -> Calendars removes its
  cached events and saved refresh token from Windows Credential Locker.
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
