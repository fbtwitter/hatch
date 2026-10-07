# Hatch roadmap

Hatch is working toward v1.0. Dates and priorities are not committed. See the
[Changelog](../CHANGELOG.md) for shipped history and unreleased changes, and
[GitHub releases](https://github.com/fbtwitter/hatch/releases/latest) for the latest published build.

## Current focus

Polish task capture, task visibility, details, and Quick Tips before setting a v1.0 release
date. Measure startup, task opening, and memory use on representative Windows hardware;
the previous sub-50 MB idle target was disproven and is not a release gate. See
[Performance](PERFORMANCE.md).

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
