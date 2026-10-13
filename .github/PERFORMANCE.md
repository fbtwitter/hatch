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

### Mascot topmost recovery comparison — 2026-10-12

Matched self-contained, unpackaged Release/x64 builds of `6ac3844` and v0.29.1 ran on
the same PC with empty task data, mascot animation enabled and the main windows hidden.
After a 20-second warmup, each process was sampled every five seconds for one minute.

| Measurement | Baseline | v0.29.1 |
|---|---|---|
| Average private bytes | 107.7 MB | 109.3 MB |
| Average working set | 164.5 MB | 165.7 MB |
| CPU time over about 60 seconds | 16.38 seconds | 16.64 seconds |
| Private-byte growth during sampling | 0.33 MB | 0.53 MB |
| Handle-count change | -5 | 0 |

The change reuses the existing five-second visibility poll and adds no timer or dependency.
A separate helper invoking the native style check 100,000 times allocated zero managed
bytes; that result measures the check alone. This short comparison does not establish
long-term memory stability or performance on other hardware.

### Native AOT focus-session check — 2026-10-13

A packaged Release/x64 native preview ran the existing automated focus-session test
with isolated sample data on the same development PC. After warmup, it opened and
closed Focus Mode 100 times.

| Measurement | Warmed process | After 100 sessions |
|---|---|---|
| Private bytes | 133.3 MiB | 135.5 MiB |
| Working set | 159.6 MiB | 161.8 MiB |
| Handles | 1,296 | 1,506 |

This short automation run does not establish long-term stability or a startup/CPU
improvement. Handle count increased during the run; it needs longer observation before
drawing a conclusion about retention. These figures are not an idle benchmark or a
controlled comparison with earlier releases.

The replacement sync client shares one HTTP connection pool and runs one cancellable
token-refresh loop while signed in. It starts no refresh loop for local-only use, and
preserves the existing sync interval and save debounce.

## Current guidance

- Do not describe the old 50 MB, 100 MB, or 120 MB figures as measured budgets.
- No CI workflow enforces memory or startup-time thresholds.
- CI builds the app and runs unit, integration and desktop UI regression checks.
- When a change adds a service, timer, large collection, or animation, record the expected
  impact and measure it when practical on the same machine and build configuration.
- Compare both working set and private bytes; record the app state and measurement method.
