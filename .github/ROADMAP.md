# Roadmap — Hatch

The path to v1.0 and beyond. This is a living document — dates and scope shift with feedback and learnings.

---

## What's Shipped

| Version | Shipped | Highlights |
|---------|---------|-----------|
| **v0.20.0** | Aug 12, 2026 | Consistent page max width with back navigation removed, scrollbars anchored to the window edge, list/header alignment fixes across Tasks, Planned and Settings |
| **v0.19.1** | Aug 10, 2026 | Empty My Day tile no longer reads as a failed day |
| **v0.19.0** | Aug 10, 2026 | Automatic updates for sideloaded installs (App Installer via GitHub Pages), Summary tiles rebuilt around today (My Day progress, Due today, Overdue, Starred), all desktop dependencies to latest stable |
| **v0.18.0** | Aug 10, 2026 | Mascot engagement (daily inspiration, capture invite, undated-task suggestion), user-editable message pool, Quiet/Balanced/Chatty setting, undo for deletion, leaner completed rows, startup and per-keystroke performance pass |
| **v0.17.0** | Jul 28, 2026 | Sync protocol v2 (tombstone deletes), Android companion authoring parity (task detail editing, lists, search, recurrence), Android background sync + on-device due-date reminders |
| **v0.16.0** | Jul 24, 2026 | End-to-end encrypted sync, PKCE OAuth, Kotlin Multiplatform Android companion, TOTP two-factor auth (`aal2` RLS), MFA + passphrase recovery, Windows App SDK 2.3.1 |
| **v0.15.0** | Jul 14, 2026 | Show Mascot setting, mascot loading ring + entrance fade, mascot-only manual launch, Summary launchpad (tiles/rows navigate), PowerToys-style title-bar search sizing |
| **v0.14.0** | Jul 10, 2026 | Summary page (KPI tiles, Today/Upcoming), Quick Snooze, task export (JSON/CSV/Markdown), dedicated Search page, non-destructive "Merge both" sync conflict option |
| **v0.13.0** | Jul 10, 2026 | Task Search, Recurring Tasks, Priority Tiers, Proactive Tip Popup |
| **v0.12.10** | Jun 19, 2026 | Expose `install-cert.cer` in GitHub releases; Store listed as primary install path |
| **v0.12.9** | Jun 19, 2026 | Fix package DisplayName to match Partner Center reservation |
| **v0.12.8** | Jun 19, 2026 | Store submission package identity, signing cert CN, msixbundle-only releases |
| **v0.12.7** | Jun 17, 2026 | Fully automatic sync (debounced push, 5-min pull), conflict-choice dialog on fresh sign-in |
| **v0.12.6** | Jun 17, 2026 | Fix list-name chip appearing without reload on new tasks in smart lists |
| **v0.12.4–v0.12.5** | Jun 17, 2026 | Fix onboarding page content clipping on small displays |
| **v0.12.3** | Jun 17, 2026 | Fix unsigned MSIX bundle install error |
| **v0.12.2** | Jun 17, 2026 | Fix signing certificate CN/Publisher |
| **v0.12.1** | Jun 17, 2026 | Bundle Windows App SDK runtime in MSIX |
| **v0.12.0** | Jun 17, 2026 | Fix transparent mascot window white-border/flash on SDR displays |
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
| Task search — title/notes/tags, dedicated Search page | ✅ Shipped (v0.13–v0.14) |
| Recurring tasks — Daily/Weekdays/Weekly/Monthly | ✅ Shipped (v0.13) |
| Priority tiers — None/Low/Medium/High, Important sorts by priority | ✅ Shipped (v0.13) |
| Proactive tip popup — opt-in, once/day | ✅ Shipped (v0.13, TeachingTip rework v0.15) |
| Summary page — KPI tiles, Today/Upcoming, launchpad navigation | ✅ Shipped (v0.14–v0.15) |
| Task export — JSON, CSV, Markdown | ✅ Shipped (v0.14) |
| Fully automatic sync — debounced push, periodic pull, conflict dialog with merge option | ✅ Shipped (v0.12.7, v0.14) |
| End-to-end encrypted sync — `HATCHE2E.v1` envelope, PBKDF2 600k + AES-256-GCM | ✅ Shipped (v0.16) |
| PKCE OAuth sign-in on Windows | ✅ Shipped (v0.16) |
| Two-factor authentication — TOTP enrolment/challenge, `aal2` enforced in RLS | ✅ Shipped (v0.16) |
| Recovery — MFA recovery codes, sync passphrase recovery kit | ✅ Shipped (v0.16) |
| Android companion (Kotlin Multiplatform) — local-first, encrypted two-way sync | ✅ Shipped (v0.16) |
| Sync protocol v2 — deletions propagate via tombstones | ✅ Shipped (v0.17) |
| Android authoring parity — task detail editing, lists, search, recurrence | ✅ Shipped (v0.17) |
| Android background sync + on-device due-date reminders | ✅ Shipped (v0.17) |

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
| Dark mode polish | Fine-tune colors, contrast, theme-aware Lottie animations | Low |
| iOS companion | SwiftUI client over the existing E2E Supabase sync, mirroring the shipped Kotlin Multiplatform Android companion — see docs/adr/0001-cross-platform-strategy.md | Very High |

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
Jun–Jul 2026  v0.12–v0.17 shipped (Store submission, sync automation, search, recurring
              tasks, priority tiers, Summary launchpad, E2E encryption, TOTP MFA, Android
              companion, MFA/passphrase recovery, sync protocol v2 tombstone deletes,
              Android authoring parity)
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
