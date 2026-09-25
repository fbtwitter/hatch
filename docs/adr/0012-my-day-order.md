# ADR-0012 — Manual My Day order

- **Status:** Accepted
- **Deciders:** Hatch mobile and desktop
- **Repos affected:** hatch-mobile and hatch
- **Builds on:** ADR-0009 (separate mobile repository), ADR-0010 (task fields are shared sync data)

## Context

My Day used newest-first ordering on both clients. Mobile added long-press drag reordering,
stored as `TodoItem.MyDayOrder`; desktop now carries the same field and uses WinUI's native
ListView reorder interaction.

## Decision

- Order My Day tasks by `MyDayOrder` ascending, then `CreatedAt` descending, then completion
  state. A missing `MyDayOrder` is equivalent to `0`, so tasks that have never been reordered
  keep their existing newest-first order.
- Reorder open My Day tasks only. Completed tasks and Suggested rows are outside the drag list.
- On a committed reorder, renumber the visible open task set from `0` and stamp each changed
  task's `UpdatedAt`; normal whole-record last-write-wins sync carries the order.
- Omit `MyDayOrder` when it is `0`. This keeps payloads for untouched tasks unchanged and
  requires no data migration; the sync envelope and storage schema stay unchanged.

## Consequences

Protocol v4 readers understand `MyDayOrder`. An older v3 client ignores this optional field
and can drop it if that client edits and pushes the same task. This is the existing
whole-record compatibility limit; both clients must understand the field to preserve manual
order through cross-device edits.
