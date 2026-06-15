# Architecture — Hatch

---

## Tech Stack

| Layer         | Technology |
|--------------|------------|
| Framework     | Windows App SDK 2.2+, WinUI 3, .NET 10, C# 13 |
| UI            | XAML only — `Microsoft.UI.Xaml.*` exclusively |
| Packaging     | MSIX, single-project |
| Persistence   | `System.Text.Json` → `%LocalAppData%\Hatch\` |
| Animation     | CommunityToolkit.WinUI.Lottie (`AnimatedVisualPlayer`) |
| Windowing     | `Microsoft.UI.Windowing` |
| Always-on-top | `SetWindowPos` via P/Invoke on HWND |
| Toasts        | `ToastNotificationManager` (due-date reminders) |
| CI            | GitHub Actions — release triggered on `v*.*.*` tag |

Target: `net10.0-windows10.0.19041.0`

---

## Data Model

### TodoItem
| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | |
| `Title` | `string` | |
| `IsCompleted` | `bool` | |
| `CreatedAt` | `DateTime` | |
| `DueDate` | `DateTimeOffset?` | ISO format |
| `IsStarred` | `bool` | |
| `IsInMyDay` | `bool` | Setter does not touch `MyDayDate` — callers set it explicitly |
| `MyDayDate` | `DateOnly?` | Last date added to My Day; persisted |
| `ListId` | `Guid` | Reference to `TaskList` |
| `Notes` | `string?` | |
| `Tags` | `List<string>` | |
| `ListName` | `string?` | `[JsonIgnore]` — set after load by `RefreshListNames()`, and immediately on `AddTask()` / quick-add |
| `HasListName` | `bool` | Computed |
| `HasListNameToDateSeparator` | `bool` | Computed |
| `HasMetaSeparator` | `bool` | Computed — `(DueDate != null \|\| ListName != null) && Tags.Count > 0` |
| `ShowAddDateHint` | `bool` | Computed — `!IsCompleted && DueDate == null` |

`ResetMyDayForNewDay()` — internal; clears `IsInMyDay` without touching `MyDayDate`.

### TaskList
| Field | Type |
|-------|------|
| `Id` | `Guid` |
| `Name` | `string` |
| `AccentColor` | `string` (OKLCH hex) |
| `IsPinned` | `bool` |
| `SortOrder` | `int` |
| `CustomIcon` | `string?` |

### AppSettings
Key fields: `Theme`, `Backdrop`, `MinimizeToTray`, `MascotX/Y/Size`, `MuteAnimation`, `LockMascotPosition`, `LottieFilePath`, `MascotAlwaysOnTop`, `HideWhenFullscreen`, `HideUntilTicks`, `ActiveNavItem` (default `"myday"`), `LastUsedListId`, `FirstRunComplete`, `RunAtStartup`, `HotkeyModifiers`, `HotkeyVirtualKey`, tip engine fields (`LastTipShowDate`, `ConsecutiveTipDismissals`, `TipAutoOpenCooldownUntil`, `LastMeaningfulTipTime`, `LastUserActivityTime`), optional sync fields (`SyncAccessToken?`, `SyncRefreshToken?`, `SyncUserEmail?`, `LastSyncedAt?`).

---

## Folder Structure

```
Models/         Plain data types — no logic beyond computed properties
ViewModels/     One per View; owns ObservableCollection, ICommand, business logic
  MainViewModel.cs      Tasks, lists, suggestions, commands, BadgeVersion
  MascotViewModel.cs    Drag, position, animation, context menu
  FocusModeViewModel.cs Single active task; hosted as Popup in MascotWindow
  SettingsViewModel.cs  Theme, backdrop, tray toggle
  RelayCommand.cs       Minimal ICommand wrapper (sync + async variants)
Views/          .xaml + .xaml.cs pairs; code-behind is thin
  MainWindow.xaml          620×640; NavigationView adaptive
  MainPage.xaml            Nav rail + task list + details pane
  TaskListPage.xaml        Task list body (hosted inside MainPage)
  OnboardingPage.xaml      First-run privacy screen
  SettingsPage.xaml
  MascotWindow.xaml        Borderless, topmost, transparent, 120×120 region mask
  QuickAddBubbleWindow.xaml Floating quick-add + tip bubble
Services/
  TaskStorageService.cs    Load-once; sole writer to tasks.json
  SettingsService.cs       Loads and saves settings.json
  SyncService.cs           Optional Supabase sync (email/password + GitHub OAuth)
  TipEngine.cs             Pure function: task snapshot → highest-priority Tip
  SystemTrayService.cs     Tray icon, context menu, hide/restore
  StartupRegistryService.cs HKCU Run key for run-at-startup
Converters/     One IValueConverter per file
Helpers/        OsVersionHelper, DueDatePresets, Strings, DisplayHelper
scripts/        PowerShell build helpers
  AssetGen/     .NET console tool — renders logo.svg into PNG + ICO assets
                (incl. Square44x44Logo targetsize + altform-unplated variants)
NativeMethods.cs  P/Invoke: SetWindowPos, GetCursorPos, SetWindowRgn, monitor APIs
Hatch.Tests/    MSTest unit tests — pure-logic layers only (no WinUI dependency)
```

---

## MVVM Pattern

- ViewModels implement `INotifyPropertyChanged` via base class or source generator
- All bindable properties raise `PropertyChanged` via `SetProperty()` or manual guard + invoke
- Commands: `RelayCommand` (sync), async variant for awaitable actions
- No business logic in Views or code-behind — ever
- Code-behind event handlers (drag, pointer) call a single ViewModel method and return

---

## Key ViewModel Details — MainViewModel

- `ActiveTasks` — `ObservableCollection<TodoItem>` filtered by the active nav item
- `MySuggestions` — all incomplete non-My-Day tasks; refreshed by `RefreshSuggestions()`
- `HasSuggestions`, `SuggestionsVisible`, `ShowEmptyState` — computed visibility properties
- `BadgeVersion` — int incremented on complete, star, due date, IsInMyDay, add, delete, reload; triggers `MainPage` to re-evaluate nav `InfoBadge` counts
- `RefreshListNames()` — populates `TodoItem.ListName` for all tasks after load or rename
- `ReloadAsync()` — clears and re-loads all collections (called on sync pull)

---

## Services & Async

- All file I/O is async (`System.Text.Json`)
- `TaskStorageService` is the **sole writer** to `tasks.json` — no exceptions
- Writes are debounced: dirty flag + 500 ms idle
- JSON loaded once at startup — no polling
- No `Task.Run` — use `DispatcherQueue.TryEnqueue` for UI updates, `PeriodicTimer` for ticks
- Pass `CancellationToken` through async chains; handle `OperationCanceledException` at boundaries

---

## Persistence

| File | Contents |
|------|----------|
| `tasks.json` | `List<TodoItem>` + `List<TaskList>` + schema version |
| `settings.json` | Theme, backdrop, mascot position/size, nav state, sync tokens |

Path: `%LocalAppData%\Hatch\` — created on first save; missing on first load is not an error.

---

## Hard Constraints

- `Microsoft.UI.Xaml.*` only — never UWP or WPF equivalents
- No `Task.Run` — `DispatcherQueue.TryEnqueue` and `PeriodicTimer` only
- No hard-coded hex colors — `ThemeResource` tokens only
- No logic in XAML code-behind
- No raster image assets — Lottie vector only
- No `#nullable disable` pragmas
- `TaskStorageService` is sole writer to `tasks.json`
- No outbound network calls except optional Supabase sync

---

## Naming

| Target | Convention |
|--------|------------|
| Types & public members | PascalCase |
| Private fields | `_camelCase` |
| Local variables / parameters | camelCase |
| Constants / static readonly | PascalCase |
| XAML `x:Name` | PascalCase |
| Async methods | Suffix `Async` |
