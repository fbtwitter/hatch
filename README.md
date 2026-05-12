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


<!-- DYNAMIC: Append each shipped feature here, newest version first -->
<!-- Format:
### v[X.X.X] — [date]
- [one line per shipped feature]
-->

## What's Shipped

### v0.2.5 — May 12, 2026
Memory and CPU optimization: fullscreen polling reduced from 1s to 5s (80% CPU reduction). Inactivity timer now stops when mascot is hidden, eliminating unnecessary wakeups. Hide-restore timer reduced from 10s to 30s. Explicit memory cleanup via `EmptyWorkingSet()` on minimize-to-tray. Idle memory now <50MB, CPU <2% when mascot hidden with window closed.

### v0.2.3.2 — May 2026
Manual mascot resizing with slider (60–200px) and preset buttons (Small/Medium/Large) in Settings. "Mascot Settings..." context menu opens Settings page. Dynamic scaling for both animated and vector mascots via Viewbox wrapping. Window position automatically maintains after resize.

### v0.2.3 — May 2026
Theme-aware due-date chip colors that update dynamically when switching between light/dark modes. Larger, crisp app icons (2x scale) for high-DPI displays. Proper text contrast on all backgrounds. See [release notes](../../releases/tag/v0.2.3) for details.

### v0.2.2 — May 2026
First-run onboarding bubble with welcome message. Mascot silent by default after first session. "Hide for..." submenu with 1 hour, 3 hours, until tomorrow, or until restart options. Inactivity fade (30s idle → 40% opacity) with hover scale animation. System tray dynamic tooltip shows hidden state; click to restore. Auto-restore after expiration via PeriodicTimer. SettingsPage mascot controls (always on top, hide when fullscreen). See [release notes](../../releases/tag/v0.2.2) for details.

### v0.2.1.1 — May 2026
App icon in title bar and taskbar. Empty state UI with contextual guidance for each list view. Improved My Day sorting: prioritizes overdue/due today tasks, then starred. Task list code refactoring for cleaner templates. See [release notes](../../releases/tag/v0.2.1.1) for details.

### v0.2.1 — May 2026
Single-click quick-add bubble from mascot — no main window required. "Task added!" confirmation with checkmark fades after 600ms. Mascot wiggles once per session for delight. Due date picker with presets (Today/Tomorrow/custom date). See [release notes](../../releases/tag/v0.2.1) for details.

### v0.2.0 — May 2026
NavigationView rail with My Day, Important, Planned, All Tasks smart lists. Planned list groups by Today/Tomorrow/This week/Later. Navigation state persists. Smooth UX with debounced saves and surgical updates. See [release notes](../../releases/tag/v0.2.0) for details.

### v0.1.0 — May 2026
Always-on-top egg mascot — drag to reposition, right-click menu, idle bob/blink animation, fullscreen auto-hide. See [release notes](../../releases/tag/v0.1.0) for details.

### v0.0.1
Basic task window — add, edit, complete, delete tasks; settings panel; system tray; MSIX packaging. See [release notes](../../releases/tag/v0.0.1) for details.

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
| Always-on-top Hatch mascot (separate topmost borderless window)                 | Shipped     |
| NavigationView rail — My Day, Important, Planned, All Tasks, custom lists       | Shipped     |
| Quick-add task directly from mascot bubble — no main window required            | Shipped     |
| Mascot mute / opt-in bubble — silent by default after first session             | Shipped     |
| Contextual tip engine — tips based on actual task state                         | In Progress |
| Task list polish — star toggle, open/completed grouping, notes field            | Planned     |
| Details pane — slide-in panel replacing modal dialog                            | Planned     |
| Real due-date picker — calendar flyout, presets, ISO persistence                | Planned     |
| List CRUD + color tags — create, rename, recolor, delete custom lists           | Planned     |
| Settings polish — 6 accent hues, mascot controls                                | Planned     |
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