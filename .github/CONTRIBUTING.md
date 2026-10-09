# Contributing to Hatch

Thanks for considering a contribution. Hatch is a WinUI 3 desktop app for Windows 10
(build 17763+) and Windows 11.

## Set up and build

Follow the [IDE-free build and run instructions](../README.md#build-and-run-without-an-ide).
They use the x64 Debug configuration and list the required SDK and runtime. Run the commands
from the repository root.

Build the Debug x64 app and run the full regression scan:

```powershell
.\windows\scripts\run-regression.ps1
```

The scan runs unit tests, real-file persistence and simulated sync integration tests,
then eight FlaUI profiles, including Light/Dark task workflows and onboarding.
It gives each profile separate app data and measurement output. Run it in an interactive
Windows desktop session; the FlaUI runner builds a self-contained unpackaged Release x64
app and automates its real windows.

See the [coverage map and remaining gaps](../docs/testing.md). New features should add
behavioral regression cases and update that map.

The Kotlin Multiplatform companion app is maintained in the
[hatch-mobile repository](https://github.com/fbtwitter/hatch-mobile).

## Project structure

See [Architecture](ARCHITECTURE.md) for the current system overview and
[docs/README.md](../docs/README.md) for product and protocol references.

## Before opening a pull request

1. Create a branch named `feature/<name>`, `fix/<name>`, or `chore/<name>`.
2. Describe the change and its scope in the issue or pull request.
3. Follow the existing WinUI and MVVM patterns:
   - Use `Microsoft.UI.Xaml.*` controls and theme resources.
   - Keep presentation state and commands in ViewModels.
   - Keep I/O asynchronous and UI updates on the dispatcher.
   - Keep `TaskStorageService` as the sole writer to `tasks.json`.
4. Run the full regression scan (build, unit, integration and UI). Check additional relevant
   UI behavior, including theme and multi-monitor behavior when applicable.
5. Use a conventional commit message (`feat:`, `fix:`, `chore:`, `refactor:`, or
   `docs:`). Keep each commit to one logical change and omit AI attribution.
6. Push the branch and open a pull request.

## Privacy and performance

Hatch stores tasks locally by default. Sync is optional and requires sign-in; do not add
telemetry or unrelated automatic network requests. See the
[privacy implementation notes](PRIVACY.md).

Memory and startup measurements are documented in [Performance](PERFORMANCE.md). The old
sub-50 MB idle target was disproven, and CI has no memory-performance gate. For new services,
timers, or large collections, describe the expected impact and measure it when practical.

## CI and releases

Pull requests, pushes to `main`, weekly runs and manual dispatch build the app and run
the regression scan on a Windows runner. Runs upload TRX results, screenshots,
and synthetic fixture data as artifacts; the `build-and-test` job fails if any profile
fails. Branch protection must require that job's status to block merges. A
`v*.*.*` tag triggers MSIX packaging, a GitHub release, and an App Installer feed update.
Microsoft Store submission is manual.

Before pushing a release tag, move the relevant `Unreleased` changes into a dated
`CHANGELOG.md` section headed `## [X.Y.Z] - YYYY-MM-DD` (or `X.Y.Z.W` for a revision),
with at least one change bullet. The release workflow checks that the section matches the
tag before building. The GitHub release page displays that entry, a link to the commits
since the previous version, and installation instructions. For each separate
Microsoft Store submission, write its **What's new** text from the changes actually included
in that Store build; see [Store listing notes](../docs/store-listing.md#whats-new).

For broader project links, see the [repository README](../README.md).
