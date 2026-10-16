# Hatch roadmap

Hatch is working toward v1.0. Dates and priorities are not committed. See the
[Changelog](../CHANGELOG.md) for shipped history and unreleased changes, and
[GitHub releases](https://github.com/fbtwitter/hatch/releases/latest) for the latest published build.

## Current focus

Polish task capture, task visibility, details, and Quick Tips before setting a v1.0 release
date. Measure startup, task opening, and memory use on representative Windows hardware;
the previous sub-50 MB idle target was disproven and is not a release gate. See
[Performance](PERFORMANCE.md).

| Task-flow update | Status |
|---|---|
| Mascot breathing room on scaled laptop displays | Included in v0.32.2; proportional padding accommodates hover and wiggle while preserving saved window bounds |
| Full-height task details with relative timestamps, themed Delete/Undo and immediate Escape dismissal | Included in v0.32.0; focused Light/Dark footer/Undo checks and core notification checks pass; native hover, pane height and immediate Escape reviewed in the preview |
| Task-details due-date presets matching mobile's two-by-two layout | Included in v0.32.0; focused Light/Dark checks pass for matching picker height, selection, clearing and persistence |
| Consistent task-page gaps while scrolling and balanced My Day Suggestions spacing | Included in v0.32.0; focused Light/Dark geometry checks pass across compact/wide layouts, both input positions and all-done state |
| Configurable Quick Add capture: close after saving or keep open for another task | Included in v0.32.0; core/storage checks pass; the preference sits under Mascot beneath Quick-Add Hotkey |
| My Day suggestions in a separate flyout with a five-task shortlist and today-first ordering | Included in v0.32.0; sorting and focused workflow checks cover expansion, refilling and closing the popup when empty |
| Expressive mascot with native animation states and refreshed app icon | Included in v0.31.0; draft-aware thinking, local capture/completion reactions, focus pause/resume, idle wake and dragging; Debug x64 and native x64/ARM64 publishes pass; live state review and ARM64 hardware coverage remain limited |
| Native AOT release executables for x64 and ARM64 | Included in v0.30.0; v0.30.1 corrects widget executable registration and validates packaged executable references; unit/integration and JIT/native UI regressions cover generated contracts and task workflows; live authentication, Windows integration hosts and ARM64 hardware remain unverified |
| Planned native calendar stability, responsive date selection and title-bar theme consistency | Included in v0.30.0; native calendar/date/Undo checks and Light/Dark frame checks pass; user confirmed date switching and title-bar appearance |
| Recover a lost native Always on Top state without taking focus | Included in v0.29.1; three recovery repeats and normal/maximized overlap checks pass; saved off/hidden preferences respected; idle resource comparison remains close to baseline; intermittent user-reported occlusion was not reproduced |
| Planned Agenda and Calendar views using existing due dates | Included in v0.29.0; draft dates, completion, recurrence, Undo and Overdue checks pass; compact/wide layouts reviewed in Light, Dark, Contrast and 125%/200% text |
| Native checkbox pressed feedback and completion animation time across task pages | Included in v0.28.0; user accepted appearance; Debug x64 build passes; reduced-motion behaviour remains unverified; earlier checkbox checks predate this refinement |
| Undo 4px above the Bottom task form, with scroll clearance for both controls | Included in v0.28.0; native size updates and solid theme background; Debug x64 build passes; user accepted spacing |
| Priority carried by native Fluent checkbox outlines, with accessible labels and Contrast text fallback | Task rows included in v0.28.0 and task details in v0.29.0; Medium details outline visually confirmed in Dark; 22 earlier focused Light/Dark checks cover rows; other details interactions and alternate DPI/text scaling remain unverified |
| Task-row metadata icon alignment | Included in v0.28.0; Debug x64 build and 18 focused Light/Dark geometry checks pass; alternate DPI/text scaling and Contrast remain unverified |
| Single-line task form with due dates, Important, page-aware creation, and fixed Top or floating Bottom placement | Included in v0.28.0 |
| Consistent themes and 24px page spacing, safe date presets, Completed previews across task pages, and adaptive window branding | Included in v0.28.0 |
| Equal 16px gaps around the Top task form across task pages | Included in v0.28.0; Debug x64 build passes |
| Consistent 16px page-header and section gaps across task pages, Settings, Summary, Search, and onboarding | Included in v0.28.0; Debug x64 build passes |
| Native outlined due-date button matching Important in the task form | Included in v0.28.0; Debug x64 build passes |
| Window-centered search with a clean idle surface and full rounded accent focus outline | Included in v0.28.0; Debug x64 build passes |
| Centered, solid native search field with rounded corners and reliable keyboard exit | Included in v0.28.0; 13 focused UI checks pass |
| Consistent mascot hiding and restoration across fullscreen, settings, restart, and display changes | Included in v0.28.0; eight focused regression checks pass and user confirmed YouTube fullscreen hide/return; slideshow and exclusive-game checks remain unverified |

## Later candidates

| Idea | Scope |
|---|---|
| Task dependencies | Blocking relationships between tasks; separate from the flat steps already shipped |
| Mascot skins and animation packs | Optional visual choices |
| Time tracking | Per-task timer and history |
| Theme polish | Contrast and animation improvements across light and dark themes |
| iOS companion | Separate client over the existing encrypted sync contract |

These are ideas, not commitments. Feedback and implementation cost determine whether they
move into a release.

## Outside current scope

- Mandatory accounts
- Multi-user collaboration or team integration
- A web version
- A custom theme system for v1.0

## How priorities are chosen

Prefer changes that make everyday task capture and management clearer. Keep Hatch useful
offline, require explicit sign-in for sync, and avoid analytics or automatic diagnostic
uploads. Measure performance changes instead of relying on unverified memory targets.

Suggest or discuss work in [GitHub issues](https://github.com/fbtwitter/hatch/issues).
