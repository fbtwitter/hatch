---
status: accepted
date: 2026-07-21
---

# `commonMain` holds rules, not state — the UI layers are not shared

The Kotlin shared module contains the portable rules only — crypto, wire serialization,
merge, recurrence, local storage I/O — exposed as `suspend` functions taking and returning
plain data. It holds no application state and exposes no `StateFlow`. Android owns its
Compose `ViewModel`; iOS owns its SwiftUI `ObservableObject`. This is a deliberate deviation
from the usual Kotlin Multiplatform advice to share as much as possible, and a future
engineer who finds two similar ViewModels should not "fix" it by hoisting state into
`commonMain`.

## Why

- **It keeps "Portable core" meaning one thing.** CONTEXT.md defines the portable core as
  pure, platform-free rule modules, and the C# reference is exactly that — `Hatch.Core`
  contains `SyncCrypto`, `SyncWire`, `SyncMerge`, `RecurrenceHelper`, and the mirrored task
  rules, with no ViewModels. `Hatch.Tests.Unit` references that plain .NET project. A
  stateful Kotlin core would have no C# counterpart to mirror, so ADR-0001's drift argument
  would stop being checkable precisely where it matters most.
- **It removes the Swift interop problem rather than solving it.** Kotlin/Native exports an
  Objective-C framework: `suspend` becomes completion handlers and `Flow` does not map at
  all, which is why SKIE and KMP-NativeCoroutines exist. With no streams crossing the
  boundary and a surface of roughly eight functions, plain `async`/`await` bridging suffices
  and no compiler plugin is required — worth a great deal on the toolchain we are least able
  to debug (see the iOS deferral in ADR-0001).
- **It matches the scope.** A companion client creates a task and toggles four fields. That
  needs correct rules and a native UI, not shared state management.

## Consequences

- The Android and iOS UI layers duplicate their state and list-shaping logic. Accepted: the
  duplicated part is thin and platform-flavoured (`StateFlow` + Compose versus `@Published`
  + SwiftUI), while the part that must not diverge — the pure rules — stays fully shared.
- Adding a shared state layer later is additive and remains possible. Retracting one after
  two UIs depend on it is not, which is why the restrictive choice is the reversible one.
