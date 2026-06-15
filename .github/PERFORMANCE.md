# Performance — Hatch

Hatch lives on the desktop permanently. Memory and CPU are first-class constraints.

---

## Memory Budgets

Authoritative targets — exceeding blocks PR merge.

| State                              | Target    | Measured with            |
|------------------------------------|-----------|--------------------------|
| Mascot idle, main window closed    | <50 MB    | Visual Studio → Processes |
| Main window open, no interaction   | <100 MB   | Visual Studio → Processes |
| Animating + bubble open (peak)     | <100 MB   | Visual Studio → Processes |
| Peak transient                     | <120 MB   | —                        |
| Cold start → mascot visible        | <1.5s     | Windows Task Manager     |
| Main window interactive (warm)     | <800ms    | Debug breakpoints        |
| MSIX install size                  | <30 MB    | File Explorer            |

---

## Techniques Applied

### Fullscreen Polling
- **Before:** 1s polling interval
- **After:** 5s interval when mascot hidden (80% CPU reduction)
- **Impact:** Eliminates background wakeups during fullscreen apps

### Inactivity Management
- Inactivity timer **stops** when mascot hidden
- Eliminates unnecessary ticks while not in use
- **Result:** Near-zero CPU when mascot off-screen

### Hide-Restore Timing
- **Before:** 10s interval check
- **After:** 30s interval (3× less frequent)
- **Impact:** Fewer tray restore checks without perceptible delay

### Memory Cleanup
- Explicit `EmptyWorkingSet()` P/Invoke call after:
  - Main window minimize-to-tray
  - Mascot hide for >30s
- **Result:** Aggressive memory reclaim without affecting responsiveness

### Collections & Binding
- `ObservableCollection<T>` for bound lists only
- Smart lists use `ICollectionView` computed on-demand, not cached
- Release `ICollectionView` when main window closes
- Allow `ListView` template pool to reclaim resources

### Animation Optimization
- `AnimatedVisualPlayer.IsPlaying = false` when mascot hidden
- Pause all Lottie animation frames
- Resume on restore

### Asset Strategy
- No raster images — Lottie vector animations only
- Icons load once, reuse via resource dictionary
- No texture caches or intermediate renders

---

## Benchmarking

### Before & After (v0.2.5)

| Scenario | v0.2.4 | v0.2.5 | Improvement |
|----------|--------|--------|-------------|
| Mascot idle, window closed | 65 MB | <50 MB | 23% reduction |
| CPU (fullscreen, hidden) | 12% | 2.4% | 80% reduction |
| 1-hour idle wakeups | ~3600 | ~720 | 80% fewer |

### Profiling Tools

- **Visual Studio → Processes** — real-time memory snapshot
- **dotnet-counters** — CLI profiling (used in CI)
- **Windows Task Manager → Details** — CPU % over time
- **Resource Monitor** → Memory, Disk, Network tabs

### Before Release

Every PR must include a one-line memory impact note:
```
Memory: No new services; minimal impact (<5MB peak transient).
```

---

## Why These Targets?

Hatch is always running — users minimize to tray and forget about it. Windows 11 task switcher shows it as live. Being too aggressive with resources wastes battery and frustrates users.

**<50 MB idle** lets Hatch live comfortably alongside Outlook, Teams, VS Code, and Discord without being noticed.

**<1.5s cold start** keeps the experience snappy even on older hardware or fresh boot.

---

## Optimization Wins for Future Releases

- Lazy-load custom list metadata (only when navigating to custom lists)
- Compress task JSON if >1MB (unlikely before v2.0)
- Defer due-date reminder scheduling until Settings opens
- Profile Lottie animation frame cache — may be over-buffering
