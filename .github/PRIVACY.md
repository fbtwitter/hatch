# Privacy Policy — Hatch

Three rules. Non-negotiable. No exceptions.

---

## The Promise

1. **No mandatory account** — Hatch installs and runs with zero sign-in. Optional Supabase sync (Settings → Sync) is available if you want it; it requires no account by default.

2. **No silent cloud** — All data lives in `%LocalAppData%\Hatch\` on your machine by default. If you enable optional sync, only you initiate it — nothing is sent automatically until you sign in.

3. **No telemetry** — No analytics SDK, no crash reporter, no usage pings, no error collection. Fully functional with the network cable unplugged.

---

## Where Your Data Lives

| Data     | File            | Location                      | Leaves device? |
|----------|-----------------|-------------------------------|----------------|
| Tasks    | `tasks.json`    | `%LocalAppData%\Hatch\`      | Only if sync is enabled |
| Settings | `settings.json` | `%LocalAppData%\Hatch\`      | Never |
| Sync tokens | `settings.json` (embedded) | `%LocalAppData%\Hatch\` | Only to authenticate Supabase |

**`%LocalAppData%`** expands to `C:\Users\<YourUsername>\AppData\Local\` on Windows.

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
      "myDayDate": null,
      "listId": "default-list-uuid",
      "notes": "Store: Costco",
      "tags": []
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
  "schemaVersion": 2
}
```

Nothing hidden. It's plain JSON — open it in Notepad.

### settings.json

```json
{
  "theme": "System",
  "backdrop": "Mica",
  "mascotX": 1234,
  "mascotY": 567,
  "mascotSize": 120,
  "minimizeToTray": true,
  "muteAnimation": false,
  "activeNavItem": "myday",
  "runAtStartup": false,
  "firstRunComplete": true,
  "syncUserEmail": null,
  "syncAccessToken": null,
  "syncRefreshToken": null,
  "lastSyncedAt": null,
  "schemaVersion": 2
}
```

`syncUserEmail`, `syncAccessToken`, and `syncRefreshToken` are `null` if you have never signed in to sync. If you sign in, they are stored locally on your device only.

---

## Network Activity

**By default: none.** Hatch does not:
- Connect to Microsoft servers
- Check for updates
- Send crash reports
- Report feature usage

**Optional Supabase sync** (off by default) connects to a Supabase instance when you explicitly sign in via Settings → Sync. If you never enable it, no network traffic occurs.

**Verification:** Open Windows Firewall → Advanced Settings → Outbound Rules → sort by Application. Hatch has no outbound rules unless you enabled sync.

---

## File Access

- **Package manifest:** `Package.appxmanifest` does not request `internetClient` or `internetClientServer` capabilities for general use
- **CI check:** Build fails automatically if any unintended outbound connection is detected

---

## Deleting Your Data

1. **Tasks & settings** — Settings → "Delete all my data" (deletes `%LocalAppData%\Hatch\`)
2. **Sync account** — Settings → Sync → Sign out (removes local tokens; your data on the sync server can be deleted from the provider's dashboard)
3. **Uninstall** — Settings → Apps → Hatch → Uninstall
4. **Manual cleanup** — Delete folder: `%LocalAppData%\Hatch\`

No local recovery, no local archives. If sync is not enabled, data is gone immediately.

---

## Import / Export

- **Export to JSON** — Settings → Export my data → saves `tasks.json` + `settings.json` to Desktop
- **Manual backup** — Copy `%LocalAppData%\Hatch\` to external drive or cloud storage of your choice
- **No vendor lock-in** — Both files are plain JSON

---

## Why No Mandatory Account?

Most productivity apps require an account because they profit from:
- Advertising (knowing what you work on)
- Data analytics (selling insights)
- Lock-in (can't leave without losing data)

Hatch doesn't. It's a tool, not a service. You own your data.

---

## Compliance

When sync is **disabled** (default):
- **GDPR** — No personal data collection, processing, or transfers
- **CCPA** — No personal data collected or sold
- **Children's Online Privacy** — N/A (no accounts, no data collection)

When sync is **enabled by the user**:
- Your email address and authentication tokens are stored locally in `settings.json`
- Tasks are uploaded to the Supabase project you authenticated against
- You can revoke access and delete remote data from the Supabase dashboard

---

## Questions?

Check the source code — it's all on GitHub.
