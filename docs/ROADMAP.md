# Roadmap — Hatch

The path to v1.0 and beyond. This is a living document — dates and scope shift with feedback and learnings.

---

## v1.0 — In Progress

**Target:** Q3 2026

Core productivity feature set: local task management, smart lists, quick-add, settings, privacy-first onboarding.

| Feature | Status | Notes |
|---------|--------|-------|
| Always-on-top mascot (topmost borderless window) | ✅ Shipped (v0.1) | Drag, reposition, right-click menu, fullscreen hide |
| NavigationView rail (My Day, Important, Planned, All Tasks) | ✅ Shipped (v0.2) | Smart filters, persistent nav state |
| Quick-add bubble (no main window required) | ✅ Shipped (v0.2.1) | One-click mascot, due-date presets, confirmation |
| Mascot mute / hide-for-an-hour | ✅ Shipped (v0.2.2) | Silent by default, system tray, auto-restore |
| Mascot resizing (Small/Medium/Large + custom slider) | ✅ Shipped (v0.2.3.2) | Persisted to settings, position maintained |
| Theme-aware colors & high-DPI icons | ✅ Shipped (v0.2.3) | Proper contrast in light/dark, 2x assets |
| Memory & CPU optimization | ✅ Shipped (v0.2.5) | <50MB idle, <2% CPU when hidden |
| Run at startup toggle | ✅ Shipped (v0.2.6) | Registry-based, silent launch |
| **Contextual tip engine** | 🔄 In Progress | Priority: overdue ≥1 → My Day empty → ≥5 today → first open → no tasks |
| Task list polish | 📋 Planned | Star toggle, open/completed grouping, notes field |
| Details pane | 📋 Planned | Slide-in side panel (replaces modal), inline edit |
| Real due-date picker | 📋 Planned | CalendarDatePicker flyout, presets (today, tomorrow, next week, weekend) |
| List CRUD + colors | 📋 Planned | Create, rename, recolor (8 OKLCH hues), delete, pin/unpin from nav rail |
| Settings polish | 📋 Planned | 6 accent hues, expanded mascot controls (always on top, hide when fullscreen) |
| Privacy & onboarding | 📋 Planned | First-run screen: no account / cloud / telemetry messaging, one-click JSON export |
| Appreciation purchase | 📋 Planned | $5 one-time via Windows Store; cosmetics only (skins, expressions, sounds, bubble themes) |

---

## Next — After v1.0

**Target:** Q4 2026

Keyboard shortcuts, toast notifications, focus mode.

| Feature | Notes |
|---------|-------|
| Keyboard shortcuts | `Win+Shift+T` summon, `Ctrl+N` new task, `Ctrl+D` complete, `Ctrl+S` star |
| Toast notifications | Due-date reminders (requires real due-date picker from v1.0) |
| Focus mode | Hatch + single pinned task; everything else hidden |
| Offline sync (multi-device prep) | Local sync marker for future cloud opt-in (not implemented, just infrastructure) |

---

## Later — 2027+

Larger scope, lower priority. Depends on user feedback post-v1.0.

| Feature | Notes | Complexity |
|---------|-------|------------|
| Optional cloud sync | Deliberate privacy choice to defer; local JSON stays default. OAuth + conflict resolution. | High |
| Task dependencies | Subtask nesting, dependency chain visualization | High |
| Custom mascot skins | Cosmetic pack: Bumblebee, Cat, Paperclip variants; animated packs. Appreciation-tier. | Medium |
| Custom animation packs | Idle bob/blink variants, wink, wave, thumbs-up reactions | Medium |
| Time tracking | Per-task timer, daily log, weekly summary | Medium |
| Recurring tasks | Daily, weekly, monthly repeat; smart due-date roll-forward | Medium |
| Labels / multi-tagging | Tags per task, filter by tag, tag cloud | Low |
| Dark mode polish | Fine-tune colors, contrast, theme-aware Lottie animations | Low |
| Mobile companion (iOS/Android) | Read-only companion app showing today's tasks; displays via local network (no cloud). | Very High |

---

## Not Planned

Features explicitly **not** on the roadmap:

- ❌ **Cloud sync** (local JSON only — privacy choice)
- ❌ **Collaboration** (single-user app)
- ❌ **Teams integration** (scope creep, privacy risk)
- ❌ **Reminders for all due dates** (only important/overdue today in v1.0)
- ❌ **Calendar view** (out of scope for v1.0)
- ❌ **Subtasks** (depends on dependencies feature; later if at all)
- ❌ **Custom themes** (system light/dark only)
- ❌ **Web version** (desktop-first product)

---

## How We Decide

1. **Scope** — Keep v1.0 tight. Feature must fit in one week sprint.
2. **Privacy** — Every feature is evaluated: Does it require a network call? Does it collect usage data? If yes, it's deferred or redesigned.
3. **Performance** — New features stay within memory/CPU budgets (see [Performance](PERFORMANCE.md)).
4. **User feedback** — Post-v1.0 we'll ask: What's missing? What's annoying? What's awesome?

---

## Appreciation Purchase Path

One-time $5 cosmetics unlock (v1.0 late):

**Tier 1: Cosmetics**
- 3 mascot skins (Bee, Cat, Simple Paperclip)
- 5 bubble themes (glass, gradient, colorful)
- 10 idle animations
- 3 sound packs

**Tier 2: Productivity (future)**
- Focus mode (if added)
- Custom themes (if added)

**Promise:** All *functionality* stays free. Cosmetics only.

---

## Estimated Timeline

```
May 2026     v0.2.6 shipped (Run at Startup)
             ↓
Jun 2026     v0.3 — Contextual tips + task polish
             ↓
Jul 2026     v0.4 — Details pane + real date picker
             ↓
Aug 2026     v0.5 — List CRUD + settings polish
             ↓
Sep 2026     v1.0 — Privacy onboarding + appreciation purchase
             ↓
Oct–Dec 2026 v1.1+ — Shortcuts, toast notifications, focus mode
```

Dates are aspirational — user feedback and blockers may shift them.

---

## Feedback

Want to suggest a feature? Have concerns?

- Open an issue: [GitHub Issues](https://github.com/fbtwitter/hatch/issues)
- Check existing issues first (may already be discussed)
- Be specific: What problem does it solve? For whom?

Thank you! 🥚
