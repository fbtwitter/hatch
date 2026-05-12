# Privacy Policy — Hatch

Three rules. Non-negotiable. No exceptions.

---

## The Promise

1. **No account** — Hatch installs and runs with zero sign-in. No Microsoft account, no OAuth, no optional login — ever.

2. **No cloud** — All data lives in `%LocalAppData%\Hatch\` on your machine. Nothing is written to any remote server.

3. **No telemetry** — No analytics SDK, no crash reporter, no usage pings, no error collection. Fully functional with the network cable unplugged.

---

## Where Your Data Lives

| Data     | File            | Location                      | Leaves device? | Encrypted? |
|----------|-----------------|-------------------------------|---|---|
| Tasks    | `tasks.json`    | `%LocalAppData%\Hatch\`      | ❌ Never | — (NTFS permissions) |
| Settings | `settings.json` | `%LocalAppData%\Hatch\`      | ❌ Never | — (NTFS permissions) |

**Expansion:** `%LocalAppData%` expands to `C:\Users\<YourUsername>\AppData\Local\` on Windows 11.

**Access:** Only your user account and Windows Defender (if enabled) can read these files. No cloud sync, no recovery services, no telemetry backends.

---

## What's Stored

### tasks.json
```json
{
  "tasks": [
    {
      "id": "uuid-here",
      "title": "Buy milk",
      "isCompleted": false,
      "createdAt": "2026-05-12T10:30:00Z",
      "dueDate": "2026-05-13T00:00:00Z",
      "isStarred": true,
      "isInMyDay": false,
      "listId": "default-list-uuid",
      "notes": "Store: Costco"
    }
  ],
  "lists": [
    {
      "id": "default-list-uuid",
      "name": "My List",
      "accentColor": "#FF5500",
      "isPinned": true,
      "sortOrder": 0
    }
  ],
  "schemaVersion": 1
}
```

Nothing hidden. It's plain JSON — open it in Notepad.

### settings.json
```json
{
  "theme": "System",
  "accentColor": "#0078D4",
  "mascotX": 1234,
  "mascotY": 567,
  "mascotSize": 120,
  "minimizeToTray": true,
  "muteIntro": false,
  "hideUntil": null,
  "activeNavItem": "MyDay",
  "runAtStartup": false,
  "lastUsedListId": "default-list-uuid",
  "tipAutoOpenToday": false,
  "schemaVersion": 1
}
```

No tracking IDs. No analytics keys. No sync tokens.

---

## No Network at All

Hatch does **not**:
- Connect to Microsoft servers
- Check for updates (manual download only)
- Send crash reports
- Report feature usage
- Store anything in Azure, OneDrive, or cloud services
- Require an internet connection to run

**Verification:** Open Windows Firewall → Advanced Settings → Outbound Rules → sort by Application. You'll see zero entries for Hatch.

---

## File Access

- **Package manifest:** `Package.appxmanifest` has **no** `internetClient` or `internetClientServer` capabilities
- **Dependencies:** All NuGet packages are vetted for zero HTTP-capable transitive dependencies
- **CI check:** Build fails automatically if any outbound connection is detected

---

## Deleting Your Data

1. **Delete tasks & settings** — Right-click Settings → "Delete all my data" (deletes `%LocalAppData%\Hatch\`)
2. **Uninstall Hatch** — Settings → Apps → Installed apps → Hatch → Uninstall
3. **Manual cleanup** — Delete folder: `%LocalAppData%\Hatch\`

No recovery, no archives, no backups elsewhere. Data is gone.

---

## Import / Export

Want to back up your tasks or switch devices?

- **Export to JSON** — Settings → Export my data → saves `tasks.json` + `settings.json` to Desktop
- **Manual backup** — Copy `%LocalAppData%\Hatch\` to external drive or cloud storage of your choice
- **No vendor lock-in** — Both files are plain JSON. Use them however you like.

---

## Why No Account?

Most productivity apps require an account because they profit from:
- Advertising (knowing what you work on)
- Data analytics (selling insights)
- Lock-in (can't leave without losing data)

Hatch doesn't. It's a tool, not a service. You own your data.

---

## Compliance

Hatch meets:
- **GDPR** — No personal data collection, processing, or transfers
- **CCPA** — No personal data collected or sold
- **Children's Online Privacy** — N/A (no accounts, no data collection)

It's boring on paper because there's nothing to comply with.

---

## Questions?

Email: [support@hatch.local](mailto:support@hatch.local) (local file reference — no actual email service)

Or check the source code — it's all here on GitHub.
