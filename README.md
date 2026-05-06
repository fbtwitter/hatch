# Hatch — A Fluent To-Do App for Windows 11

A native WinUI 3 desktop tasks app with a friendly always-on-top paperclip mascot.
No account. No cloud. No telemetry. Your tasks stay on your device — always.

**Stack:** WinUI 3 · C# · .NET 9 · Windows App SDK 2.0+
**Target:** Windows 11 (build 22000+) · x64, x86, arm64

---

## Why Hatch?

|                     | Microsoft To Do                      | Hatch                                              |
|---------------------|--------------------------------------|----------------------------------------------------|
| Account required    | Yes — Microsoft account              | No account, ever                                   |
| Interaction model   | Open app → find list → add task      | One click on Hatch → type → Enter                  |
| Task capture speed  | 3+ clicks                            | Target ≤ 4 seconds                                 |
| Windows 11 design   | Pre-Fluent 2                         | Native WinUI 3: Mica, acrylic, Segoe UI Variable   |
| Privacy             | Telemetry, syncs to Microsoft servers| JSON on local disk only — nothing leaves your device|
| Personality         | Neutral, corporate                   | Named mascot with idle animation and contextual tips|

---

## Installing

Download the latest `.msix` from [Releases](../../releases).

**First time only — install the signing certificate so Windows trusts the package:**

1. Download `install-cert.cer` from the release
2. Double-click it → **Install Certificate** → **Local Machine** → **Trusted People** → Finish

Then double-click the `.msix` to install. Subsequent releases won't need the certificate step again.

---

## Building from Source

**Prerequisites:** Windows 11, Visual Studio 2022 with the Windows App SDK workload,
or the .NET 9 SDK + Windows App SDK installed separately.

```powershell
dotnet build              # Debug
dotnet build -c Release   # Release
dotnet run                # Run
```

Placeholder assets are generated automatically on first build if missing.
MSIX packaging and code signing are handled by CI only.

---

## Privacy

Three rules, non-negotiable:

1. **No account.** Installs and runs with zero sign-in. No Microsoft account, no OAuth,
   no optional login — ever.
2. **No cloud.** All data lives in `%LocalAppData%\Hatch\` on your machine.
   Nothing is written to any remote server.
3. **No telemetry.** No analytics SDK, no crash reporter, no usage pings.
   Fully functional with the network cable unplugged.

| Data     | File            | Location                      | Leaves device? |
|----------|-----------------|-------------------------------|----------------|
| Tasks    | `tasks.json`    | `%LocalAppData%\Hatch\`  | Never          |
| Settings | `settings.json` | `%LocalAppData%\Hatch\`  | Never          |

---

<!-- DYNAMIC: Update Project Structure when new types or folders are added -->

## Project Structure

```
Models/       TodoItem (Id, Title, IsCompleted, CreatedAt)
ViewModels/   MainViewModel, RelayCommand
Views/        MainPage.xaml, SettingsPage.xaml
Services/     TaskStorageService — reads/writes tasks.json
Converters/   BoolToStrikethrough, BoolToOpacity, BoolToVisibility, DateTimeToString
scripts/      create-assets.ps1, setup-cert.ps1
.github/
  workflows/  release.yml — builds and publishes MSIX on version tag push
```

---

<!-- DYNAMIC: Append each shipped feature here, newest version first -->
<!-- Format:
### v[X.X.X] — [date]
- [one line per shipped feature]
-->

## What's Shipped

### v0.0.1
- WinUI 3 task window (520×640) with backdrop picker (Mica, Mica Alt, Desktop Acrylic, None)
- Add, edit (dialog), complete, and delete tasks; card layout with 8px corner radii; newest-first sort
- Persistent storage — `tasks.json` and `settings.json` under `%LocalAppData%\Hatch\`
- Settings page — light/dark/system theme toggle, backdrop picker, minimize-to-tray toggle
- System tray icon — minimize to tray, restore on click, exit
- MSIX packaging with self-signed certificate via GitHub Actions CI

---

<!-- DYNAMIC: Update Roadmap table as features ship
  - Move rows from Planned → In Progress when work begins
  - Remove rows from v1.0 table and move to What's Shipped when done
  - Add rows to Next or Later as new features are approved
-->

## Roadmap

### v1.0 — In Progress

| Feature                                                                         | Status      |
|---------------------------------------------------------------------------------|-------------|
| Always-on-top Hatch mascot (separate topmost borderless window)                 | In Progress |
| Quick-add task directly from mascot bubble — no main window required            | Planned     |
| Contextual tip engine — tips based on actual task state                         | Planned     |
| NavigationView rail — My Day, Important, Planned, All Tasks, custom lists       | Planned     |
| Task list polish — star toggle, open/completed grouping, notes field            | Planned     |
| Details pane — slide-in panel replacing modal dialog                            | Planned     |
| Real due-date picker — calendar flyout, presets, ISO persistence                | Planned     |
| List CRUD + color tags — create, rename, recolor, delete custom lists           | Planned     |
| Settings polish — 6 accent hues, mascot controls                                | Planned     |
| Mascot mute / opt-in bubble — silent by default after first session             | Planned     |
| Privacy & no-account first-run onboarding screen                                | Planned     |
| Appreciation purchase ($5, one-time) — cosmetic-only, nothing gated             | Planned     |

### Next

| Feature               | Notes                                                       |
|-----------------------|-------------------------------------------------------------|
| Keyboard shortcuts    | `Win+Shift+T` to summon, `Ctrl+N` new task, `Ctrl+D` complete |
| Toast notifications   | Requires real due dates (v1.0)                              |
| Focus mode            | Hatch + single pinned task — everything else hidden         |

### Later

| Feature                              | Notes                                             |
|--------------------------------------|---------------------------------------------------|
| Optional cloud sync                  | Deliberate privacy choice to defer; local JSON stays default |
| Task dependencies                    | Subtask nesting — power-user differentiator       |
| Custom mascot skins / animation packs| Appreciation-tier cosmetics                       |
| Time tracking                        | Built-in per-task timer                           |

---

## Performance Budget

Hatch lives on the desktop permanently — memory footprint is a first-class constraint.

| State                                       | Target                              |
|---------------------------------------------|-------------------------------------|
| Mascot idle, main window closed             | < 50 MB                             |
| Mascot idle, main window open               | < 100 MB                            |
| Peak transient (quick-add + list load)      | < 120 MB, returns to idle within 5s |
| Cold start to mascot visible                | < 1.5s                              |
| MSIX install size                           | < 30 MB                             |

---

## License

MIT