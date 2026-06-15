<div align="center">

<img src="logo.svg" width="140" height="140" alt="Hatch Logo">

# Hatch — Frictionless Task Capture for Windows

Fast. Private. Local-first.

![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-0078D4?style=flat-square&logo=windows11)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=.net)
![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4?style=flat-square)
![MIT License](https://img.shields.io/badge/License-MIT-green?style=flat-square)
![v0.11.1](https://img.shields.io/badge/Version-v0.11.1-blue?style=flat-square)

</div>

> A native WinUI 3 desktop app with a friendly always-on-top eggclip mascot.  
> Capture tasks in ≤4 seconds from anywhere. No account. No cloud. No telemetry.

---

## Features at a Glance

**Always-on-top mascot** — Click from anywhere, add task in seconds  
**Smart lists** — My Day, Important, Planned, All Tasks, custom lists  
**My Day daily reset** — My Day clears each morning; incomplete tasks surface as suggestions to re-add  
**Contextual tips** — Tips based on your actual task state (v0.3+)  
**Local storage** — `%LocalAppData%` by default; optional Supabase sync available  
**Windows native** — Mica (Win11), Acrylic (Win10 2004+), Segoe UI Variable, dark/light mode  
**Private by design** — No mandatory account, no telemetry, no silent cloud  
**Appreciation cosmetics** — Optional $5 one-time purchase (skins, sounds, themes)  

---

## Installation

### Windows Package (Recommended)

1. Download the latest `.msix` from [Releases](../../releases)
2. **First time only** — Download `install-cert.cer`, double-click, select **Install Certificate** → **Local Machine** → **Trusted People** → **Finish**
3. Double-click the `.msix` file
4. Hatch starts automatically

Subsequent releases don't need the cert step.

### Building from Source

**Requirements:** Windows 10 (build 17763+) or Windows 11, Visual Studio 2022 with Windows App SDK workload (or .NET 10 SDK + Windows App SDK)

```powershell
git clone https://github.com/fbtwitter/hatch.git
cd hatch
dotnet build              # Debug
dotnet build -c Release   # Release
dotnet run                # Run (Windows only)
```

Placeholder assets generate automatically. See [Contributing Guide](.github/CONTRIBUTING.md#building-from-source) for details.

---

## Documentation

| Document | Purpose |
|----------|---------|
| **[Changelog](CHANGELOG.md)** | Detailed release history, what shipped in each version |
| **[Roadmap](.github/ROADMAP.md)** | v1.0 planned features, Next, Later, decision-making process |
| **[Architecture](.github/ARCHITECTURE.md)** | Tech stack, folder structure, data model, MVVM patterns |
| **[Privacy](.github/PRIVACY.md)** | Data guarantee, storage locations, no-network promise |
| **[Performance](.github/PERFORMANCE.md)** | Memory budgets, optimization techniques, benchmarks |
| **[Contributing](.github/CONTRIBUTING.md)** | How to build, code standards, submitting PRs |

---

## Privacy Guarantee

**Three rules. Non-negotiable.**

**No mandatory account** — Installs and runs with zero sign-in (optional sync available)  
**No silent cloud** — All data in `%LocalAppData%\Hatch\` by default — nothing leaves without your consent  
**No telemetry** — No analytics, no crash reports, no usage pings  

Fully functional with the network cable unplugged.

**Data:** Two files, plain JSON, human-readable.  
| File | Contents |
|------|----------|
| `tasks.json` | Your tasks + custom lists |
| `settings.json` | Theme, mascot position, preferences, optional sync tokens |

[Full privacy details →](.github/PRIVACY.md)

---

## Performance

Hatch lives on your desktop permanently. Memory is a first-class constraint.

| Scenario | Target |
|----------|--------|
| Mascot idle, window closed | <50 MB |
| Main window open | <100 MB |
| Cold start → visible | <1.5s |
| MSIX install size | <30 MB |

**Optimizations:** 5s fullscreen polling, 30s hide-restore timer, aggressive memory cleanup, Lottie animations pause when hidden.

[Performance details & benchmarks →](.github/PERFORMANCE.md)

---

## What's Shipped

Latest version: **v0.11.1** (June 15, 2026) — [full version history →](.github/ROADMAP.md#whats-shipped)

---

## Roadmap

One remaining v1.0 item: appreciation purchase ($5, cosmetics only).

[Full roadmap →](.github/ROADMAP.md)

---

## Contributing

Want to help? We'd love it!

1. Read [Coding Standards](context/coding-standards.md) (non-negotiable)
2. Check [Architecture](.github/ARCHITECTURE.md) for design decisions
3. See [Contributing Guide](.github/CONTRIBUTING.md) for build steps, PR process, code review checklist

**Branch naming:** `feature/name`, `fix/name`, `chore/name`  
**Commits:** Conventional format (`feat:`, `fix:`, etc.)  
**Memory:** Every PR must note memory impact

---

## FAQ

**Q: Does Hatch sync with Microsoft To Do?**  
A: No Microsoft To Do integration. Hatch has optional Supabase sync (Settings → Sync) if you want cross-device access, but it's off by default and never required.

**Q: Can I use Hatch on Windows 10?**  
A: Yes. Hatch requires Windows 10 build 17763 (1809) or later. Mica backdrop is Windows 11-only; Windows 10 2004+ gets acrylic instead.

**Q: Will there be an Android/iOS version?**  
A: Possibly as a read-only companion app (2027+). Desktop-first for now.

**Q: How do I back up my tasks?**  
A: Settings → Export my data. Two JSON files, yours to keep.

**Q: What if I want to delete everything?**  
A: Settings → Delete all my data. One click. Gone. No recovery.

---

## License

MIT License — See [LICENSE](LICENSE) file.

---

<div align="center">

<img src="logo.svg" width="100" height="100" alt="Hatch Logo">

**Built for Windows. No account. No cloud. No compromise.**

[Back to top](#hatch--frictionless-task-capture-for-windows)

</div>

