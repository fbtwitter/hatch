# Clipster — A Fluent To-Do App for Windows 11

A native WinUI 3 desktop tasks app with a friendly always-on-top paperclip mascot. No account. No cloud. No telemetry. Your tasks stay on your device — always.

**Stack:** WinUI 3 · C# · .NET 9 · Windows App SDK 2.0  
**Target:** Windows 11 (build 17763+) · x64, x86, arm64

---

## Why Clipster?

| | Microsoft To Do | Clipster |
|---|---|---|
| Account required | Yes — Microsoft account | No account, ever |
| Interaction model | Open app → find list → add task | One click on Clipster → type → Enter |
| Task capture speed | 3+ clicks | Target ≤ 4 seconds |
| Windows 11 design | Pre-Fluent 2 | Native WinUI 3: Mica, acrylic, Segoe UI Variable |
| Privacy | Telemetry, syncs to Microsoft servers | JSON on local disk only — nothing leaves your device |
| Personality | Neutral, corporate | Named mascot with idle animation and contextual tips |

---

## Installing

Download the latest `.msix` from [Releases](../../releases).

**First time only — install the signing certificate so Windows trusts the package:**
1. Download `install-cert.cer` from the release
2. Double-click it → **Install Certificate** → **Local Machine** → **Trusted People** → Finish

Then double-click the `.msix` to install. Subsequent releases won't need the certificate step again.

---

## Building from Source

**Prerequisites**
- Windows 11
- Visual Studio 2022 with **Windows App SDK** workload, or the .NET 9 SDK + Windows App SDK installed separately

```powershell
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Run
dotnet run
```

Placeholder assets are generated automatically on first build if missing. MSIX packaging and code signing are handled by CI only.

---

## Privacy

Three rules, non-negotiable:

1. **No account.** Installs and runs with zero sign-in. No Microsoft account, no OAuth, no optional login — ever.
2. **No cloud.** All data lives in `%LocalAppData%\TodoWinUI3\` on your machine. Nothing is written to any remote server.
3. **No telemetry.** No analytics SDK, no crash reporter, no usage pings. Fully functional with the network cable unplugged.

| Data | File | Location | Leaves device? |
|---|---|---|---|
| Tasks | `tasks.json` | `%LocalAppData%\TodoWinUI3\` | Never |
| Settings | `settings.json` | `%LocalAppData%\TodoWinUI3\` | Never |

---

## Project Structure

```
Models/          TodoItem (Id, Title, IsCompleted, CreatedAt)
ViewModels/      MainViewModel, RelayCommand
Views/           MainPage.xaml, SettingsPage.xaml
Services/        TaskStorageService — reads/writes tasks.json
Converters/      BoolToStrikethrough, BoolToOpacity, BoolToVisibility, DateTimeToString
scripts/         create-assets.ps1, setup-cert.ps1
.github/
  workflows/     release.yml — builds and publishes MSIX on version tag push
```

---

## What's Shipped (v1)

- Full WinUI 3 task window — Mica backdrop, acrylic title bar, 8px corner radii
- Add, edit, complete, and delete tasks
- Tasks sorted newest-first; open/completed grouping
- Persistent storage via `tasks.json` in local app data
- Settings page — light/dark theme, system tray behavior
- System tray icon — minimize to tray, restore on click
- MSIX packaging with self-signed certificate via GitHub Actions CI

---

## Roadmap

### v1.1 — In Progress

| Feature | Status |
|---|---|
| Always-on-top Clipster mascot (separate topmost borderless window) | Planned |
| Quick-add task directly from mascot bubble — no main window required | Planned |
| Contextual tip engine — tips based on actual task state (overdue count, empty My Day, etc.) | Planned |
| Real due-date picker — calendar flyout, presets, ISO persistence | Planned |
| Mascot mute / opt-in bubble — silent by default after first session | Planned |
| Right-click mascot menu — Quick add · Open tasks · Hide for an hour · Settings | Planned |
| List CRUD + color tags — create, rename, recolor, delete custom lists | Planned |
| Privacy & no-account first-run onboarding screen | Planned |
| Appreciation purchase ($5, one-time) — cosmetic-only, nothing gated | Planned |

### Next

| Feature | Notes |
|---|---|
| Keyboard shortcuts | `Win+Shift+T` to summon, `Ctrl+N` new task, `Ctrl+D` complete |
| Reminders + toast notifications | Requires real due dates (v1.1) |
| Focus mode | Clipster + single pinned task — everything else hidden |

### Later

| Feature | Notes |
|---|---|
| Optional cloud sync | Deliberate privacy choice to defer; local JSON stays the default |
| Task dependencies | Subtask nesting and task blocking — power-user differentiator |
| Custom mascot skins / extra animation packs | Appreciation-tier cosmetics |
| Time tracking | Built-in per-task timer |

---

## Performance Budget

Clipster lives on the desktop permanently — memory footprint is a first-class constraint.

| State | Working Set |
|---|---|
| Mascot idle, main window closed | < 50 MB |
| Mascot idle, main window open | < 100 MB |
| Peak transient (quick-add + list load) | < 120 MB, returns to idle within 5s |
| Cold start to mascot visible | < 1.5s |
| MSIX install size | < 30 MB |

---

## License

MIT
