# Performance — Hatch

Performance should be measured on representative Windows hardware. The former sub-50 MB
idle target was disproven by measurement and is not a release gate.

## Measurements

Release/x64 measurements from 2026-08-12:

| State | Result |
|---|---|
| Mascot idle, main window closed | About 210 MB working set |
| Cold start to mascot visible | About 0.64 seconds average across three runs |
| Main window open, animation, and peak transient | Not measured |
| Task-details open/close latency on a low-end PC | Not measured |

The idle working set included roughly 180 MB from GPU vendor driver modules on the test
system. Those pages are largely shared with other hardware-accelerated Windows apps, so
working set is not the same as memory uniquely attributable to Hatch.

## Current guidance

- Do not describe the old 50 MB, 100 MB, or 120 MB figures as measured budgets.
- No CI workflow enforces memory or startup-time thresholds.
- CI builds the app and runs the pure-logic unit tests.
- When a change adds a service, timer, large collection, or animation, record the expected
  impact and measure it when practical on the same machine and build configuration.
- Compare both working set and private bytes; record the app state and measurement method.
