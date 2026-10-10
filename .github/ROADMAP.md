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
| Task-row metadata icon alignment | Implemented for the next release; Debug x64 build and 18 focused Light/Dark geometry checks pass; alternate DPI/text scaling and Contrast remain unverified |
| Single-line task form with due dates, Important, page-aware creation, and fixed Top or floating Bottom placement | Implemented for the next release |
| Consistent themes and 24px page spacing, safe date presets, Completed previews across task pages, and adaptive window branding | Implemented for the next release |
| Equal 16px gaps around the Top task form across task pages | Implemented locally; Debug x64 build passes |
| Consistent 16px page-header and section gaps across task pages, Settings, Summary, Search, and onboarding | Implemented locally; Debug x64 build passes |
| Native outlined due-date button matching Important in the task form | Implemented locally; Debug x64 build passes |
| Window-centered search with a clean idle surface and full rounded accent focus outline | Implemented locally; Debug x64 build passes |
| Centered, solid native search field with rounded corners and reliable keyboard exit | Implemented locally; 13 focused UI checks pass |
| Consistent mascot hiding and restoration across fullscreen, settings, restart, and display changes | Implemented locally; eight focused regression checks pass and user confirmed YouTube fullscreen hide/return; slideshow and exclusive-game checks remain unverified |

## Later candidates

| Idea | Scope |
|---|---|
| Task dependencies | Blocking relationships between tasks; separate from the flat steps already shipped |
| Mascot skins and animation packs | Optional visual choices |
| Time tracking | Per-task timer and history |
| Theme polish | Contrast and animation improvements across light and dark themes |
| iOS companion | Separate client over the existing encrypted sync contract; see [ADR-0001](../docs/adr/0001-cross-platform-strategy.md) |

These are ideas, not commitments. Feedback and implementation cost determine whether they
move into a release.

## Outside current scope

- Mandatory accounts
- Multi-user collaboration or team integration
- A web version
- A calendar view or custom theme system for v1.0

## How priorities are chosen

Prefer changes that make everyday task capture and management clearer. Keep Hatch useful
offline, require explicit sign-in for sync, and avoid analytics or automatic diagnostic
uploads. Measure performance changes instead of relying on unverified memory targets.

Suggest or discuss work in [GitHub issues](https://github.com/fbtwitter/hatch/issues).
