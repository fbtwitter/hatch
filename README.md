<div align="center">

<img src="logo.svg" width="140" height="140" alt="Hatch Logo">

# Hatch: Always-On To-Do

Your tasks, always one click away.

![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-0078D4?style=flat-square&logo=windows11)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=.net)
![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4?style=flat-square)
![MIT License](https://img.shields.io/badge/License-MIT-green?style=flat-square)
![v0.17.0](https://img.shields.io/badge/Version-v0.17.0-blue?style=flat-square)
[![Sponsor](https://img.shields.io/badge/Sponsor-%E2%9D%A4-ea4aaa?style=flat-square&logo=github-sponsors)](https://github.com/sponsors/fbtwitter)

</div>

> A friendly always-on-top mascot lives on your desktop — capture a task in under 4 seconds, from anywhere.  
> Built with WinUI 3 for a native Windows feel. No account. No cloud. No telemetry.

---

## Features at a Glance

**Always-on-top mascot** — Click from anywhere, add a task in seconds, never leave your current app  
**Smart lists** — My Day, Important, Planned, All Tasks, unlimited custom lists  
**My Day daily reset** — Resets each morning; incomplete tasks surface as suggestions to re-add  
**Focus Mode** — Always-on-top overlay above the mascot; one task, two buttons, everything else gone  
**Due dates & reminders** — Calendar flyout with quick presets; toast notifications with Mark Complete action  
**Tags & filtering** — Free-form tags per task; click any chip to filter instantly across lists  
**Contextual tips** — Nudges based on your actual task state, never random  
**Optional encrypted sync** — Off until you sign in; tasks are encrypted on your device with a passphrase the server never sees  
**Android companion** — Your list on your phone, same encryption; opens straight to your tasks, no sign-in required  
**Two-factor authentication** — Optional TOTP on your sync account, with recovery codes  
**Feels right at home** — WinUI 3, Mica (Win11), Acrylic (Win10), Segoe UI Variable, system accent color  
**Private by design** — No mandatory account, no telemetry, no silent cloud  

---

## Screenshots

<table>
  <tr>
    <td><img src="docs/screenshots/01-quick-add.png" alt="Quick-add bubble"/></td>
    <td><img src="docs/screenshots/02-my-day.png" alt="My Day list"/></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/03-planned.png" alt="Planned list"/></td>
    <td><img src="docs/screenshots/04-focus-mode.png" alt="Focus Mode"/></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/05-settings.png" alt="Settings"/></td>
    <td></td>
  </tr>
</table>

---

## Installation

### Microsoft Store *(recommended)*

No certificate setup needed — get it directly from the Store.

### Direct download *(sideload, with automatic updates)*

1. Download `install-cert.cer` from [Releases](../../releases) → double-click → **Install Certificate** → **Local Machine** → **Trusted People** → **Finish** *(first time only)*
2. Open **<https://fbtwitter.github.io/hatch/Hatch.appinstaller>** and choose **Install** — Windows picks the right architecture automatically

Installing this way registers Hatch with Windows App Installer, which checks daily for
new versions and updates in the background. Windows performs those checks — Hatch itself
still makes no outbound calls outside the opt-in sync path.

Subsequent releases don't need the certificate step again.

<details>
<summary>One-off install without automatic updates</summary>

Download `Hatch_x.x.x.x_bundle.msixbundle` from [Releases](../../releases) and
double-click it. Installs done this way will not update themselves — including any
install made before v0.18.0, which must be reinstalled once via the link above to
join the update channel.
</details>

### Building from Source

**Requirements:** Windows 10 (build 17763+) or Windows 11, Visual Studio 2022 with Windows App SDK workload (or .NET 10 SDK + Windows App SDK)

```powershell
git clone https://github.com/fbtwitter/hatch.git
cd hatch/windows
dotnet build              # Debug
dotnet build -c Release   # Release
dotnet run                # Run (Windows only)
```

Placeholder assets generate automatically. See [Contributing Guide](.github/CONTRIBUTING.md#building-from-source) for details.

---

## Privacy Guarantee

**Three rules. Non-negotiable.**

**No mandatory account** — Installs and runs with zero sign-in (optional sync available)  
**No silent cloud** — All data in `%LocalAppData%\Hatch\` by default — nothing leaves without your consent  
**No telemetry** — No analytics, no crash reports, no usage pings  

Fully functional with the network cable unplugged. Export or delete all your data in one click from Settings.

[Full privacy details →](.github/PRIVACY.md)

---

## FAQ

**Q: Does Hatch sync with Microsoft To Do?**  
A: No Microsoft To Do integration. Hatch has optional Supabase sync (Settings → Sync) if you want cross-device access, but it's off by default and never required.

**Q: Can I use Hatch on Windows 10?**  
A: Yes. Hatch requires Windows 10 build 17763 (1809) or later. Mica backdrop is Windows 11-only; Windows 10 2004+ gets Acrylic instead.

**Q: The mascot has a faint shadow around it — how do I remove it?**  
A: Disable **Show shadows under windows** in Windows performance settings: search for *"Adjust the appearance and performance of Windows"* → uncheck **Show shadows under windows**. This cannot be suppressed per-app via any public Windows API.

**Q: Will there be an Android/iOS version?**  
A: Android companion is here — same encrypted sync, opens straight to your tasks with no
sign-in required. iOS is a possible future companion; no timeline yet.

**Q: How do I back up my tasks?**  
A: Settings → Export my data. Two JSON files, yours to keep.

**Q: What if I want to delete everything?**  
A: Settings → Delete all my data. One click. Gone. No recovery.

---

[Changelog](CHANGELOG.md) · [Roadmap](.github/ROADMAP.md) · [Architecture](.github/ARCHITECTURE.md) · [Performance](.github/PERFORMANCE.md) · [Contributing](.github/CONTRIBUTING.md)
