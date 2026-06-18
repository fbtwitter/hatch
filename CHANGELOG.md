# Changelog

All notable changes to Hatch will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

---

## [0.12.8] - 2026-06-19

### Changed
- Package identity updated to match Microsoft Store Partner Center (Name, Publisher, DisplayName)
- Signing certificate regenerated with Store publisher CN for sideload compatibility
- GitHub releases now ship msixbundle only — Windows selects the right architecture automatically
- README refreshed: title, features, and installation aligned with Store listing

### Planned
- Contextual tip engine — priority-based tips in quick-add bubble
- Task list polish — star toggle, open/completed grouping, notes field
- Details pane — slide-in panel for task editing
- Real due-date picker — calendar flyout with presets
- List CRUD — create, rename, recolor, delete custom lists
- Settings polish — 6 accent hues, mascot controls
- Privacy & onboarding — first-run screen with no-account messaging
- Appreciation purchase — $5 one-time cosmetics (skins, sounds, themes)

---

## [0.2.6] - 2026-05-12

### Added
- "Start Hatch when Windows starts" toggle in Settings
- Startup registry entry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Hatch`
- Mascot launches silently on OS boot (no main window, no intro bubble)
- Startup detection via `RunAtStartup` setting + empty command args in App.xaml.cs

### Changed
- Responsive title bar: pane toggle button hides only on automatic responsive mode changes
- DisplayModeChanged event ensures correct UI state across window resizes

### Fixed
- Potential null reference when launching at startup

---

## [0.2.5] - 2026-05-12

### Changed
- Fullscreen polling: 1s → 5s interval (80% CPU reduction)
- Inactivity timer: stops when mascot hidden (eliminates idle wakeups)
- Hide-restore timer: 10s → 30s interval (3× less frequent checks)

### Added
- Explicit `EmptyWorkingSet()` P/Invoke call on main window minimize-to-tray for aggressive memory cleanup

### Performance
- Idle memory now <50MB, CPU <2% when mascot hidden with window closed

---

## [0.2.3.2] - 2026-05-12

### Added
- Manual mascot resizing via right-click context menu "Resize Mascot"
- Size presets: Small (80×80), Medium (120×120, default), Large (160×160)
- Custom size slider: 60–200px with live preview

### Changed
- Mascot window position clamped to monitor work area after resize
- Size persisted to `AppSettings.MascotSize`, restored on next launch
- Dynamic scaling for both animated and vector mascots via Viewbox wrapping

---

## [0.2.3] - 2026-05-11

### Added
- Theme-aware color resolution for due-date chips (overdue/today/upcoming)
- Explicit themed brushes with proper contrast in light/dark modes
- Icon assets doubled in size (2x scale) for high-DPI displays: 64px, 88px, 128px, 300px, 620px, 100px
- Transparent background with white SplashScreen (per design spec)

### Changed
- ThemeVersion tracking forces UI refresh on theme change
- GitHub Actions workflow verified to include all assets in MSIX package

### Fixed
- Dynamic color updates when switching between light and dark modes
- Taskbar and icon scaling on high-DPI monitors

---

## [0.2.2] - 2026-05-11

### Added
- First-run onboarding: intro bubble auto-opens once with welcome message
- Right-click menu "Hide for an hour" → hides mascot, shows system tray indicator
- Auto-restore after 60 minutes or manual click on tray
- Inactivity fade: 30s idle → 40% opacity with hover scale animation
- Dynamic tray tooltip showing hidden state

### Changed
- Mascot silent by default after first session
- SettingsPage mascot controls: always on top toggle, hide when fullscreen option

---

## [0.2.1.1] - 2026-05-11

### Added
- App icon in title bar and taskbar (proper scaling across DPI)
- Empty state UI with contextual guidance for each list view

### Changed
- My Day sorting prioritizes overdue/due today tasks, then starred
- Task list template code refactored for cleaner structure

---

## [0.2.1] - 2026-05-10

### Added
- Single-click quick-add bubble from mascot (no main window required)
- "Task added!" confirmation with checkmark icon (fades after 600ms)
- Mascot wiggles once per bubble session for delight
- Due date picker with presets: Today, Tomorrow, Pick a date, No date
- Last-used list saved to settings and restored on next bubble open
- Main window button in bubble header for quick access

### Changed
- Smooth animations and native WinUI 3 feel
- Multi-monitor safe bubble positioning

---

## [0.2.0] - 2026-05-09

### Added
- NavigationView rail with My Day, Important, Planned, All Tasks tabs
  - **My Day**: filters `IsInMyDay=true`, newest-first sort
  - **Important**: filters `IsStarred=true`, newest-first sort
  - **Planned**: filters `DueDate!=null`, grouped by Today/Tomorrow/This week/Later, sorted by date
  - **All Tasks**: no filter, newest-first sort
- Active nav item persists to `settings.json`

### Changed
- Performance: debounced saves (500ms), surgical updates (no full list clears), compact JSON
- Smooth transitions, no jank, proper binding modes (TwoWay for checkbox)

### Fixed
- Access violations from ListView template pooling
- Checked/unchecked sync across filtered views
- Scroll jumps on list updates

---

## [0.1.0] - 2026-05-07

### Added
- Always-on-top Hatch mascot (separate topmost borderless window)
- Drag to reposition via `GetCursorPos`, work-area clamping, save on release
- Right-click context menu: Show Main Window, Reset Position
- Idle bob/blink animation (Storyboard), pauses on fullscreen hide
- Hit-area alignment fix: ellipse fills 120×120 `SetWindowRgn` region

---

## [0.0.1] - 2026-05-01

### Added
- Initial release: Tasks window (520×640) with WinUI 3
- Task list: add, edit, delete, complete, newest-first sort
- Settings panel: light/dark/system theme toggle, backdrop picker, minimize-to-tray toggle
- Persistence: `TodoItem` + `AppSettings` via JSON to `%LocalAppData%\Hatch\`
- System tray minimize-to-tray support
- Card-style tasks (8px radius), circular checkmarks
