# Contributing to Hatch

Thanks for considering a contribution. Hatch is a WinUI 3 desktop app for Windows 10
(build 17763+) and Windows 11.

## Set up and build

Follow the [IDE-free build and run instructions](../README.md#build-and-run-without-an-ide).
They use the x64 Debug configuration and list the required SDK and runtime. Run the commands
from the repository root.

For the pure-logic unit tests:

```powershell
dotnet test .\windows\Hatch.Tests.Unit\Hatch.Tests.Unit.csproj -c Debug
```

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
4. Build the app and run the unit tests. Check relevant UI behavior, including theme and
   multi-monitor behavior when applicable.
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

Pull requests and pushes to `main` build the app and run the pure-logic unit tests. A
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
