# Roadmap — Hatch

The path to v1.0 and beyond. This is a living document — dates and scope shift with feedback and learnings.

---

## What's Shipped

| Version | Shipped | Highlights |
|---------|---------|-----------|
| **v0.11.1** | Jun 15, 2026 | Fix CS8602 null-dereference warning in SyncService |
| **v0.11.0** | Jun 15, 2026 | My Day daily reset + suggestions, InfoBadge live updates, nav icon refresh, transparent taskbar icon, list name on new tasks |
| **v0.10.0** | Jun 13, 2026 | List name chip in task rows, nav InfoBadge for open tasks, rename flyout in compact pane, bubble dismiss on unfocus, mascot tap shows main window, always-on-top ignores windowed fullscreen |
| **v0.9.2** | Jun 11, 2026 | Upgrade Windows App SDK to 2.2.0 |
| **v0.9.1** | Jun 8, 2026  | Fix sync stale data — "Sync now" always pulls latest from server; surface pull errors |
| **v0.9.0** | Jun 8, 2026  | Task Tags — chip row per task, add/remove in details pane, filter by tag; project renamed to hatch |
| **v0.8.1** | Jun 8, 2026  | Mascot transparency fix — `WS_EX_NOREDIRECTIONBITMAP` + DWM frame extension |
| **v0.8.0** | Jun 5, 2026  | Focus Mode — compact always-on-top popup above mascot; mark done / exit; fade+slide animation |
| **v0.7.0** | Jun 5, 2026  | Due-date toast notifications with mark-complete action and `hatch://opentask` deep-link |
| **v0.6.2** | Jun 4, 2026  | Bidirectional sync — auto-pull on startup, pull-then-push on "Sync now" |
| **v0.6.1** | Jun 4, 2026  | Single-instance redirect; OAuth callback delivered to running instance |
| **v0.6.0** | Jun 4, 2026  | Optional Supabase sync, first-run onboarding privacy screen, `hatch://` URI scheme |
| **v0.5.1** | Jun 4, 2026  | Windows 10 compatibility: OS-guarded backdrops, DWM attribute guards, `OsVersionHelper` |
| **v0.5.0** | Jun 3, 2026  | Keyboard shortcuts: Delete task, Ctrl+D due date, Ctrl+M My Day, ↑/↓ navigation, Ctrl+Enter exit notes |
| **v0.4.1** | Jun 3, 2026  | List reordering via Move up / Move down context menu; SymbolIcon on all list menu items |
| **v0.4.0** | May 22, 2026 | Details pane with inline title edit, notes, My Day toggle, due date; native ListView selection |
| **v0.3.1** | May 15, 2026 | Completed task grouping with collapsible Expander, 250ms animated move, undo snackbar |
| **v0.3.0** | May 12, 2026 | Contextual tip engine, adaptive silence, actionable tips, dynamic bubble sizing |
| **v0.2.6** | May 12, 2026 | Run at startup toggle, mascot auto-launch on OS boot, responsive UI |
| **v0.2.5** | May 12, 2026 | Memory optimization (<50MB idle), CPU reduction (80% less fullscreen polling) |
| **v0.2.4** | May 2026     | Manual mascot resizing with presets + custom slider |
| **v0.2.3** | May 11, 2026 | Theme-aware colors, 2x high-DPI icons, dark/light mode polish |
| **v0.2.2** | May 2026     | Mascot mute, hide-for-an-hour, first-run onboarding |
| **v0.2.1** | May 10, 2026 | Quick-add bubble, due-date presets, confirmation |
| **v0.2.0** | May 9, 2026  | NavigationView rail, smart lists (My Day, Important, Planned) |
| **v0.1.0** | May 7, 2026  | Always-on-top mascot, drag/reposition, idle animation |
| **v0.0.1** | May 1, 2026  | Initial release: tasks, settings, system tray |

---

## v1.0 — In Progress

**Target:** Q3 2026

| Feature | Status |
|---------|--------|
| Always-on-top mascot — drag, reposition, fullscreen hide | ✅ Shipped (v0.1) |
| NavigationView rail — My Day, Important, Planned, All Tasks, custom lists | ✅ Shipped (v0.2) |
| Quick-add bubble — one-click mascot, due-date presets, list selector | ✅ Shipped (v0.2.1) |
| Mascot mute / hide-for-an-hour / run at startup | ✅ Shipped (v0.2.x) |
| Memory & CPU optimization (<50MB idle) | ✅ Shipped (v0.2.5) |
| Contextual tip engine — priority-based, adaptive silence | ✅ Shipped (v0.3) |
| Open/completed grouping, undo snackbar, collapsible completed group | ✅ Shipped (v0.3.1) |
| Details pane — inline edit, notes, My Day toggle, due date | ✅ Shipped (v0.4) |
| Real due-date picker — CalendarDatePicker flyout, presets | ✅ Shipped (v0.4) |
| List CRUD — create, rename, recolor, pin, delete, custom icon | ✅ Shipped (v0.4.x) |
| Keyboard shortcuts — Delete, Ctrl+D, Ctrl+M, ↑/↓, Ctrl+Enter | ✅ Shipped (v0.5) |
| Windows 10 compatibility — OS-guarded backdrops, DWM guards | ✅ Shipped (v0.5.1) |
| Optional Supabase sync + privacy-first onboarding screen | ✅ Shipped (v0.6) |
| Due-date toast notifications + mark-complete action | ✅ Shipped (v0.7) |
| Focus Mode — compact always-on-top overlay | ✅ Shipped (v0.8) |
| Task Tags — chips, filter, details pane | ✅ Shipped (v0.9) |
| Nav InfoBadge, list name chip, UX polish | ✅ Shipped (v0.10) |
| My Day daily reset + suggestions | ✅ Shipped (v0.11) |

---

## Next — After v1.0

**Target:** Q4 2026

---

## Later — 2027+

Larger scope, lower priority. Depends on user feedback post-v1.0.

| Feature | Notes | Complexity |
|---------|-------|------------|
| Task dependencies | Subtask nesting, dependency chain visualization | High |
| Custom mascot skins | Cosmetic pack: Bumblebee, Cat, Paperclip variants; animated packs. Free, like everything else. | Medium |
| Custom animation packs | Idle bob/blink variants, wink, wave, thumbs-up reactions | Medium |
| Time tracking | Per-task timer, daily log, weekly summary | Medium |
| Recurring tasks | Daily, weekly, monthly repeat; smart due-date roll-forward | Medium |
| Dark mode polish | Fine-tune colors, contrast, theme-aware Lottie animations | Low |
| Mobile companion (iOS/Android) | Companion client (see CONTEXT.md): Kotlin Multiplatform core, Compose on Android, SwiftUI on iOS, over the existing E2E Supabase sync — see docs/adr/0001-cross-platform-strategy.md. Capture + triage + tick off; never owns the data model. | Very High |

---

## Not Planned

Features explicitly **not** on the roadmap:

- ❌ **Mandatory account** (optional sync exists, but Hatch never requires sign-in)
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

## Monetization

None. Hatch is free in full — there is no purchase, no tier, no cosmetic paywall.
Skins, bubble themes, idle animations and sound packs ship free when they ship.

---

## Estimated Timeline

```
May–Jun 2026  v0.1–v0.11 shipped (mascot → tags → sync → My Day reset)
              ↓
Q3 2026       v1.0 — final polish
              ↓
Q4 2026       v1.1+ — post-v1.0 features (see Next above)
```

Dates are aspirational — user feedback and blockers may shift them.

---

## Feedback

Want to suggest a feature? Have concerns?

- Open an issue: [GitHub Issues](https://github.com/fbtwitter/hatch/issues)
- Check existing issues first (may already be discussed)
- Be specific: What problem does it solve? For whom?

Thank you! 🥚
