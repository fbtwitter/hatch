# Architecture — Hatch

Ground truth for implementation decisions. Do not infer beyond what is specified here.

---

## Tech Stack

| Layer         | Technology                                             |
|--------------|--------------------------------------------------------|
| Framework     | Windows App SDK 2.0+, WinUI 3, .NET 9, C# 12           |
| UI            | XAML only — `Microsoft.UI.Xaml.*` exclusively          |
| Packaging     | MSIX, single-project                                   |
| Persistence   | `System.Text.Json` → `%LocalAppData%\Hatch\`      |
| Animation     | CommunityToolkit.WinUI.Lottie (`AnimatedVisualPlayer`) |
| Windowing     | `Microsoft.UI.Windowing`                               |
| Always-on-top | `SetWindowPos` via P/Invoke on HWND                    |
| Toasts        | `ToastNotificationManager` (due-date reminders)        |
| CI            | GitHub Actions — release triggered on `v*.*.*` tag     |

Target: `net9.0-windows10.0.19041.0`

---

## Data Model

### TodoItem
```csharp
Id           // Guid
Title        // string
IsCompleted  // bool
CreatedAt    // DateTime
DueDate      // DateTimeOffset? (ISO format)
IsStarred    // bool
IsInMyDay    // bool
ListId       // Guid (reference to TaskList)
Notes        // string?
```

### TaskList
```csharp
Id           // Guid
Name         // string
AccentColor  // string (OKLCH hex)
IsPinned     // bool
SortOrder    // int
```

### AppSettings
```csharp
Theme              // enum: Light, Dark, System
AccentColor        // string (hex)
MascotX, MascotY   // int (position)
MascotSize         // int (60-200px)
MinimizeToTray     // bool
MuteIntro          // bool (intro bubble shown?)
HideUntil          // DateTime? (hide-for-an-hour expiry)
ActiveNavItem      // string (nav pane selection)
RunAtStartup       // bool
LastUsedListId     // Guid? (for quick-add bubble)
TipAutoOpenToday   // bool (one auto-open per day)
SchemaVersion      // int
```

---

## Folder Structure

```
Hatch/
├── Models/              Plain data types; no logic beyond properties
│   ├── TodoItem.cs
│   ├── TaskList.cs
│   └── AppSettings.cs
├── ViewModels/          One per View; owns collections, commands, logic
│   ├── MainViewModel.cs
│   ├── MascotViewModel.cs
│   ├── SettingsViewModel.cs
│   └── RelayCommand.cs  (ICommand wrapper)
├── Views/               .xaml + .xaml.cs pairs; code-behind is thin
│   ├── MainWindow.xaml
│   ├── MainPage.xaml
│   ├── SettingsPage.xaml
│   ├── MascotWindow.xaml
│   └── QuickAddBubble.xaml
├── Services/            Async I/O, business logic, notifications
│   ├── TaskStorageService.cs
│   ├── SettingsService.cs
│   ├── TipEngine.cs
│   ├── NotificationService.cs
│   └── StartupRegistryService.cs
├── Converters/          One IValueConverter per file
│   ├── BoolToOpacityConverter.cs
│   ├── BoolToStrikeConverter.cs
│   ├── BoolToVisibilityConverter.cs
│   └── DateTimeToStringConverter.cs
├── scripts/             PowerShell build helpers
├── NativeMethods.cs     P/Invoke declarations
└── App.xaml             Application root
```

**One type per file. File name matches type name exactly.**

---

## MVVM Pattern

- **ViewModels** implement `INotifyPropertyChanged` (base class or source generator)
- All bindable properties use `SetProperty()` to raise `PropertyChanged`
- Commands are `ICommand`; use `RelayCommand` for sync, async variant for awaitable
- **No business logic in Views or code-behind** — ever
- Code-behind event handlers (drag, pointer events) call a single ViewModel method and return

---

## Services & Async

- All file I/O is async (`ReadAsync` / `WriteAsync` via `System.Text.Json`)
- **`TaskStorageService` is the sole writer** to `tasks.json` — no exceptions
- Writes are debounced: dirty flag + 500ms idle before saving
- JSON loaded once at startup — no polling
- **No `Task.Run`** — use `DispatcherQueue.TryEnqueue` for UI updates, `PeriodicTimer` for ticks
- `CancellationToken` passed through async chains; handle `OperationCanceledException` at boundaries

---

## Collections & Sorting

- Bound lists: `ObservableCollection<T>`
- Tasks sorted newest-first: insert at index 0
- Smart lists (My Day, Planned, Important): filtered `ICollectionView` on demand, not separate collections
- Custom task lists: lazy-loaded on first navigation

---

## Windowing & Theming

### MainWindow (Tasks)
- Sizing: `AppWindow.Resize(new SizeInt32(w, h))`
- Backdrop: `SystemBackdrop = new MicaBackdrop()`
- Theme: `RequestedTheme` on root element only

### MascotWindow
- Borderless, topmost (`WS_EX_TOPMOST`), transparent
- `ExtendsContentIntoTitleBar = true`
- Hit-area: `SetWindowRgn` (ellipse region so desktop stays clickable)
- Position: persisted to `LocalSettings`, clamped to work area (multi-monitor safe)

### Colors
- `ThemeResource` tokens only — e.g. `CardBackgroundFillColorDefaultBrush`
- Accent override: `Application.Current.Resources["SystemAccentColor"]` set at startup
- No manual color forking — rely on `RequestedTheme`

---

## Memory Techniques

All apply by default (see [Performance](PERFORMANCE.md) for budgets):

- No raster assets — Lottie vector only
- `AnimatedVisualPlayer.IsPlaying = false` when mascot hidden
- Release `ICollectionView` on main window hide; allow `ListView` template pool reclaim
- `EmptyWorkingSet` P/Invoke after minimize-to-tray
- Every PR adding a service, timer, or collection: include one-line memory impact note

---

## Persistence (Fixed)

| File            | Contents                                                |
|----------------|---------------------------------------------------------|
| `tasks.json`   | Array of `TodoItem` + array of `TaskList` + schema version |
| `settings.json`| Theme, accent, mascot position/size, mute state, nav item, startup toggle |

**Path:** `%LocalAppData%\Hatch\` — created on first save  
**Format:** Human-editable JSON  
**Migration:** v1→v2: set default `ListId`, `IsStarred=false`, `DueDate=null`; back up old file before writing

---

## Hard Constraints

- `Microsoft.UI.Xaml.*` only — never UWP or WPF equivalents
- No `Task.Run` — use `DispatcherQueue.TryEnqueue` and `PeriodicTimer`
- No hard-coded hex colors — `ThemeResource` tokens only
- No logic in XAML code-behind — ViewModels only
- No raster image assets — Lottie vector only
- No `#nullable disable` pragmas
- `TaskStorageService` is sole writer to `tasks.json` — no exceptions

---

## Language & Naming

- **C# 12, .NET 9**
- **Nullable reference types enabled** — annotate all types
- **Implicit usings enabled** — don't repeat BCL usings
- **File-scoped namespaces** on all new files

| Target                          | Convention    |
|---------------------------------|---------------|
| Types & public members          | PascalCase    |
| Private fields                  | `_camelCase`  |
| Local variables / parameters    | camelCase     |
| Constants / static readonly     | PascalCase    |
| XAML `x:Name`                   | PascalCase    |
| Async methods                   | Suffix `Async`|

---

## Comments

**Default: no comments.**

Write one only if the WHY is non-obvious:
- A WinUI 3 workaround
- A hidden OS constraint
- A subtle invariant that would surprise a reader

Never write what code *does* — identifiers do that. Never write multi-line comment blocks.
