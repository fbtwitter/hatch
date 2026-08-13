# Changelog

All notable changes to Hatch will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

---

## [0.21.4] - 2026-08-13

### Added
- Releases now publish to the Microsoft Store automatically as part of the tag-triggered
  release pipeline, alongside the existing GitHub release and App Installer feed

---

## [0.21.3] - 2026-08-13

### Fixed
- Navigation badge counts flashed repeatedly during normal use while signed into sync —
  every debounced push after a task edit was reloading the whole task list and
  recomputing every badge, even when nothing had actually changed

---

## [0.21.2] - 2026-08-13

### Fixed
- GitHub sign-in always failed with a `bad_oauth_state` error — a newer transitive
  dependency (gotrue-csharp) was appending an extra query parameter that broke Supabase's
  own OAuth state tracking
- Sign-in with GitHub button no longer allows overlapping sign-in attempts while one is
  already in progress

---

## [0.21.1] - 2026-08-13

### Fixed
- Completed-tasks group `Expander` content was sitting flush against its border with no
  padding

### Added
- AssetGen can now render Store listing submission images (300/150/71px) to
  `docs/store-assets`, separate from the app's own packaged assets

---

## [0.21.0] - 2026-08-13

### Added
- Settings → Sync: change your sync passphrase without losing existing synced data —
  decrypts with the current one and re-encrypts with the new one. Other signed-in devices
  detect the change automatically on their next sync and re-prompt for the new passphrase
- Settings → Mascot: pick a default page for the main window to open to from the mascot
  (My Day, Important, Planned, All Tasks, Summary, or any custom list), instead of always
  resuming wherever it was last left. A pinned custom list that's later deleted falls back
  to Summary automatically

### Changed
- Settings page descriptions now show as a single line with an ellipsis, with the full
  text available on hover, instead of wrapping across multiple lines
- Several Settings rows (App Theme, Window Backdrop, Mascot messages, Default page, Lottie
  Animation File, Open data folder, Export tasks) now match the compact icon/text/control
  layout used elsewhere on the page, and no longer reflow their controls below the header
  at narrow widths
- Main window's minimum width increased from 480px to 560px

### Fixed
- Sync sessions were sometimes silently dropped, forcing an unnecessary re-sign-in — a
  rotated refresh token from GoTrue's background auto-refresh was never persisted to the
  Credential Locker, and a transient network failure at launch was treated the same as a
  genuinely dead token
- `redeem_mfa_recovery_code` had no throttle of its own (Supabase has no per-RPC rate
  limit); a stolen password alone was enough to attempt unlimited guesses against a user's
  recovery codes. Now locks out further attempts for 15 minutes after 5 failures
- Backfilled the baseline database schema and row-level-security policy into migration
  history — they predated migration tracking entirely, so a schema rebuilt from scratch
  would not have reproduced them

---

## [0.20.3] - 2026-08-13

### Fixed
- Sideload install/auto-update via `Hatch.appinstaller` failed with "Error in parsing the
  app package" — the release workflow's MSIX bundling step never pinned a bundle version,
  so `MakeAppx` auto-generated a timestamp-based one that didn't match what the
  `.appinstaller` manifest declared. Direct `.msixbundle` downloads were unaffected

---

## [0.20.2] - 2026-08-13

### Fixed
- Task selection indicator now works reliably everywhere — a custom row template had
  silently dropped native selection visuals, the main list and Suggested cards land on the
  same native ListView highlight instead of a hand-rolled one, jumping in from Search or the
  Summary page no longer occasionally leaves no row highlighted, and Planned/Important rows
  line up with the rest of the list

---

## [0.20.1] - 2026-08-12

### Changed
- Release package no longer bundles ONNX Runtime and DirectML — Hatch has no AI/ML
  feature, and the Windows App SDK metapackage was pulling both in transitively at
  roughly 120 MB across x64/arm64/arm64ec, unused. Confirmed via loaded-module
  inspection that neither was ever loaded at runtime, so this is an install-size
  reduction only

---

## [0.20.0] - 2026-08-12

### Changed
- Every page caps its content to one consistent max width, and back navigation is gone
- Page scrollbars now sit at the window edge instead of the edge of the capped content,
  so they track the window the way the rest of Windows does

### Fixed
- The task list no longer drifts right of its header on a wide or maximised window
- Planned's "Today" and "Later" group headers line up with the rows beneath them
- The Settings title lines up with the cards below it

---

## [0.19.1] - 2026-08-10

### Fixed
- An empty My Day no longer reports "0 / 0" at 0% in success green — it now shows a
  plain count and "Nothing planned yet" until the day has something in it

---

## [0.19.0] - 2026-08-10

### Added
- **Automatic updates for sideloaded installs.** Releases now publish a
  `.appinstaller` to GitHub Pages; installing from it registers Hatch with Windows
  App Installer, which checks daily and updates in the background. Windows performs
  the checks — the app itself still makes no outbound calls outside opt-in sync.
  Existing manual installs must be reinstalled once from the link to join the channel
- **Summary tiles rebuilt around today.** My Day now shows completed/total with a
  percentage, Due today surfaces what's actually due now, and Starred counts what's
  starred and still open. Overdue stays, with its wording switching between
  "Nothing slipping" and "Catch up when you can". Every tile opens the list it
  describes

### Changed
- All desktop dependencies moved to their latest stable releases, including MSTest
  and the .NET test SDK (both a major version behind) and Supabase
- `logo.svg` moved to a root `assets/` folder so the Windows app, the mobile
  companion and the README all share one source file

---

## [0.18.0] - 2026-08-10

### Added
- **Mascot engagement** — a daily inspiration line (stable for the whole day, not re-rolled
  on every open), a suggestion when undated tasks pile up, and an invitation to capture a
  thought when everything is clear. Built-in lines are original and unattributed; add your
  own in Settings → Mascot, where they join the same pool
- **Mascot messages** setting — Quiet / Balanced / Chatty. Reminders about overdue tasks
  appear at every level

### Changed
- Completed tasks show only what still matters: checkbox, title and list name. Due date,
  repeat, priority, tags, the star and the Focus/Snooze menu items are dropped once a task
  is done
- Important excludes completed tasks entirely, matching Planned — a star never clears
  itself, so its Completed group grew without bound

### Fixed
- Undo now covers deletion, not just completion, and appears when completing a task from
  Planned or Important (both were previously silent)
- About page reported v0.14.0 on a v0.17.0 build; the version is now read at runtime
- Settings changed moments before quitting could be lost
- Cold start no longer blocks the UI thread reading settings.json

---

## [0.17.0] - 2026-07-28

### Added
- **Sync protocol v2 — deletions now sync.** Deleting a task or list writes a tombstone
  (`IsDeleted`) instead of dropping the record, so a delete on one device sticks everywhere
  instead of reappearing after the next merge. No envelope change and no server migration;
  older clients read v2 payloads fine, they just revive tombstones until updated
  (`docs/adr/0007`)
- Android companion is now a full authoring client: task detail sheet (title, notes, due
  date, Important, My Day, priority, repeat, list, tags), swipe-to-delete with undo,
  navigation drawer with the smart lists and custom-list management, and search across every
  list
- Android pulls on foreground and every 5 minutes while open, syncs hourly in the background
  via WorkManager once signed in, and raises on-device due-date reminders (`docs/adr/0002`)
- Completing a repeating task on Android spawns its next occurrence, matching Windows

### Fixed
- A family of `ToLocalTime().Date` timezone bugs on Windows affecting due-date display,
  reminders, and stats (found while auditing both clients against the sync protocol)
- `CreatedAt` compared as text instead of an instant, misordering newest-first sorts across
  Windows/Android clock offsets

---

## [0.16.0] - 2026-07-24

### Added
- End-to-end encrypted sync (`HATCHE2E.v1` envelope, PBKDF2-HMAC-SHA256 600k + AES-256-GCM)
  — the server never sees plaintext task data
- PKCE OAuth for GitHub sign-in on Windows, replacing the implicit flow (which would hand a
  hijacked `hatch://` redirect live access and refresh tokens)
- Verified passphrase entry — a passphrase that can't open the existing encrypted row is
  rejected at entry instead of silently stored
- Kotlin Multiplatform Android companion (`mobile/`) — local-first task list, opens straight
  to your tasks with no sign-in required; encrypted two-way sync sharing the same merge
  logic as Windows
- Two-factor authentication (TOTP) — enrolment with QR code + setup key on Windows, sign-in
  challenge on both Windows and Android, `aal2` enforced in Supabase RLS as a restrictive
  policy for accounts with a verified factor
- Recovery paths for both sync secrets: ten bcrypt-hashed MFA recovery codes (redeeming
  turns two-factor off — the only way an account with a lost authenticator regains access),
  and a saveable Sync Recovery Kit for the passphrase, which cannot be recovered by
  construction
- First database migrations committed to the repo (`supabase/migrations/`)

### Changed
- Windows App SDK upgraded 2.2.0 → 2.3.1
- Repository restructured — the WinUI app moved from the root into `windows/`, alongside
  the new `mobile/` companion
- Release workflow validates the injected `SUPABASE_URL` secret shape and gained a
  `workflow_dispatch` trigger for dry runs that build and sign but don't publish
- Sync's read/merge/staleness/MFA-challenge decision logic extracted from `SyncService`
  into a pure `SyncDecisions` class (and recovery-kit text into `RecoveryKit`), so the unit
  test project can cover it directly without a WinUI reference — 79 new tests

---

## [0.15.0] - 2026-07-14

### Added
- Show Mascot setting (Settings → Mascot) — turn the floating mascot off entirely;
  quick-add hotkey and tray keep working
- Mascot shows a loading ring while a Lottie file decodes and fades in on first reveal

### Changed
- Manual launch now opens just the mascot; the main window only opens at launch when
  there is nothing else to show (first run, Show Mascot off, or an active "Hide for…"
  window)
- Proactive tips ("Show Tips Automatically") now appear as a proper tip balloon above
  the mascot instead of a repurposed quick-add bubble; clicking a tip's action button
  no longer counts as a dismissal toward tip cooldown
- Summary page is now a launchpad: the Overdue tile opens Planned, the Open tile opens
  All Tasks, and Today/Upcoming rows jump straight to the tapped task with the details
  pane open. Tiles and rows are real buttons (keyboard- and screen-reader-reachable),
  the Overdue tile drops its red badge when the count is zero, and its strings are
  localized like the rest of the app
- Title-bar search box sizes like PowerToys' (fills available space up to 360px,
  shrinks freely on narrow windows)

---

## [0.14.0] - 2026-07-10

### Added
- Summary page (top-level nav item): Overdue/Completed Today/Open KPI tiles, a "Today"
  section (daily-agenda glance), and a strictly-future "Upcoming" list
- Quick Snooze — task row ⋯ menu → Tomorrow / Next week re-date without reopening the
  calendar picker
- Export tasks — Settings → Data → JSON, CSV, or Markdown (Markdown groups by list with
  checkboxes, ready to paste into a status update)
- Dedicated Search page — task search moved from the task-list header into the window's
  title bar (global, accessible from every page); results render on their own page
  instead of overlaid inside a task list; typing debounced 250ms; clearing a search
  returns to whichever page you were on before (Summary, Settings, or a specific list)
- Non-destructive "Merge both" option for sync conflict resolution, alongside the
  existing "keep this device" / "use account data" choices

### Changed
- `MainViewModel` split from a single 993-line file into six focused partial classes
- CI now builds and runs a new pure-logic unit test suite on every push/PR, not just on
  release tags

### Removed
- Summary page's "Computed locally..." subtitle and the "All done!" congratulatory
  message shown when a list's Open group was empty

---

## [0.13.0] - 2026-07-10

### Added
- Task Search — `AutoSuggestBox` filters all tasks by title/notes/tags across every
  list and completion state; flat results view with a "No matching tasks" empty state;
  `Ctrl+F` focuses the box, `Escape` clears the query
- Recurring Tasks — `TodoItem.Recurrence` (None/Daily/Weekdays/Weekly/Monthly);
  completing a recurring task spawns the next occurrence with the due date advanced;
  undo removes the spawned occurrence too
- Priority Tiers — `TodoItem.Priority` (None/Low/Medium/High); details pane combo;
  color-coded chip on the task row; the Important smart list sorts by priority first
- Proactive Tip Popup — opt-in Settings toggle pops the contextual tip up above the
  mascot once/day without a click, reusing the existing quick-add bubble in a tip-only
  mode

---

## [0.12.10] - 2026-06-19

### Added
- `install-cert.cer` exposed in the GitHub release for sideload users; release notes
  updated to reference the Store as the primary install path

---

## [0.12.9] - 2026-06-19

### Fixed
- Package DisplayName corrected to match exact Partner Center reserved name: "Hatch: Always-On To-Do"

---

## [0.12.8] - 2026-06-19

### Changed
- Package identity updated to match Microsoft Store Partner Center (Name, Publisher, DisplayName)
- Signing certificate regenerated with Store publisher CN for sideload compatibility
- GitHub releases now ship msixbundle only — Windows selects the right architecture automatically
- README refreshed: title, features, and installation aligned with Store listing

---

## [0.12.7] - 2026-06-17

### Added
- Sync is now fully automatic: push to server 3s after each local save (debounced);
  pull from server every 5 minutes via `PeriodicTimer`
- On fresh sign-in, if both local and server have tasks, a `ContentDialog` shows task
  count, list count, and last-updated date for each side so the user can choose which
  to keep; if the account is new/empty, local data is pushed immediately

### Removed
- "Sync now" button — status and last-synced time now update automatically

---

## [0.12.6] - 2026-06-17

### Fixed
- `ListName` ("Task" chip) now set immediately on new tasks created from smart list
  views (My Day, Important, Planned, All Tasks) — previously required a reload to appear

---

## [0.12.4] – [0.12.5] - 2026-06-17

### Fixed
- Onboarding page content clipping — `ScrollViewer` with a viewport-bound `MinHeight`
  prevents text from being cut off on small displays

---

## [0.12.3] - 2026-06-17

### Fixed
- MSIX bundle signed after `MakeAppx` — the bundle was previously unsigned, causing
  install error `0x800B010A`

---

## [0.12.2] - 2026-06-17

### Fixed
- Signing certificate CN and Publisher field updated to "Reza Fauzi Augusdi"

---

## [0.12.1] - 2026-06-17

### Fixed
- Windows App SDK runtime now bundled in the MSIX — resolves `0x8007007E` ("The
  specified module could not be found") on sideload installs without the runtime
  pre-installed

---

## [0.12.0] - 2026-06-17

### Fixed
- Transparent mascot window — eliminated the white border/flash artifact on SDR
  displays; removed HDR-specific branching entirely, so there is no border and no
  redirection bitmap on any display type

---

## [0.11.1] - 2026-06-15

### Fixed
- CS8602 nullable-dereference warnings in `SyncService` (`?.Auth?.` null-conditional
  chain; `await` of a nullable `Task` guarded with `?? Task.CompletedTask`)

---

## [0.11.0] - 2026-06-15

### Added
- My Day daily reset — tasks not completed by end of day are cleared from My Day at
  the next launch; `MyDayDate` (`DateOnly?`) added to `TodoItem`
- Suggestions — all incomplete non-My-Day tasks surface in the My Day view; tappable
  card opens the details pane; compact `+` button adds a suggestion to My Day
- InfoBadge live updates — the nav badge count now updates on star toggle, due date
  change, IsInMyDay toggle, task add, and task delete (previously only on complete/reload)
- Transparent taskbar icon — generated `altform-unplated` assets so Windows no longer
  applies a coloured backing plate

### Changed
- Nav icon refresh — sun (My Day), flag (Important), agenda (Planned), bulleted list
  (All Tasks)
- Default starting page changed to My Day
- Custom list nav tooltips show the list name on hover, updating on rename
- List name is set immediately on task creation (`AddTask()`/quick-add) without
  requiring a reload

---

## [0.10.0] - 2026-06-13

### Added
- List name chip in task rows — "Task" for the default list, custom list names for
  others; shown left of the due-date chip
- Nav InfoBadge — numeric open-task count on My Day, Important, Planned, All Tasks,
  and each custom list
- Rename flyout in the compact pane — opening rename with the nav pane collapsed shows
  a flyout instead of an invisible inline edit

### Changed
- Quick-add bubble now auto-dismisses on window unfocus (skipped when the cursor is
  over the mascot, to avoid a race with the mascot tap)
- A second mascot tap while the bubble is open now shows the main window instead of
  toggling the bubble
- Always-on-top now respects windowed fullscreen — hides only for exclusive D3D
  fullscreen/presentation mode; windowed-fullscreen apps (browsers, media players) no
  longer hide the mascot

---

## [0.9.2] - 2026-06-11

### Changed
- Windows App SDK upgraded to 2.2.0

---

## [0.9.1] - 2026-06-08

### Fixed
- Sync stale-data bug — "Sync now" always pulls the latest from the server; pull
  errors now surface to the user instead of failing silently

---

## [0.9.0] - 2026-06-08

### Added
- Task Tags — free-form string tags per task; chips rendered in the task row (up to 2
  visible + "+N" overflow)
- Details pane tag input (Enter to add), chip row with individual remove buttons
- Tag filter — clicking any chip filters the current list by that tag; a banner shows
  the active filter and clears it

### Changed
- Project renamed: `todo-winui3.csproj`/`.sln` → `hatch.csproj`/`.sln`

---

## [0.8.1] - 2026-06-08

### Fixed
- Mascot window transparency — `WS_EX_NOREDIRECTIONBITMAP` + `DwmExtendFrameIntoClientArea`
  eliminates the SDR white-border artifact around the mascot

---

## [0.8.0] - 2026-06-05

### Added
- Focus Mode — compact always-on-top popup above the mascot; enter via the task ⋯
  menu → "Focus on this"; two icon buttons (mark done, exit focus); fade + slide-up
  entrance animation; renders outside the mascot's 120×120 window via
  `ShouldConstrainToRootBounds="False"`

---

## [0.7.0] - 2026-06-05

### Added
- Due-date toast notifications — two toasts per task (30-min warning at 8:30 AM + due
  time at 9:00 AM); "Mark complete" action button completes the task without opening
  the app; tapping the toast body opens Hatch with the task selected via the
  `hatch://opentask` protocol

### Changed
- Nav rail tooltips (My Day, Important, Planned, All Tasks, New List, Settings) now
  appear to the right

---

## [0.6.2] - 2026-06-04

### Added
- Bidirectional sync — auto-pull on startup (before windows are created), pull-then-push
  on "Sync now"; `SyncService.TasksReceived` fires on a successful pull and
  `MainViewModel.ReloadAsync()` reloads collections in response
- `LastSyncedAt` now updates on pull as well as push

---

## [0.6.1] - 2026-06-04

### Fixed
- Single-instance redirect via `AppInstance.FindOrRegisterForKey("hatch-main")` — the
  OAuth callback no longer opens a second window; protocol activation (`hatch://`) is
  delivered to the already-running instance

---

## [0.6.0] - 2026-06-04

### Added
- Optional Supabase sync — email/password sign-up and GitHub OAuth; manual push/pull;
  tokens persisted in `settings.json`
- `hatch://` custom URI scheme registered for the OAuth callback
- First-run onboarding page — "No account / local data / no telemetry" privacy
  messaging, "Get started" navigates to the main page

### Changed
- Supabase credentials moved to gitignored `Services/Secrets.cs`; CI injects
  `SUPABASE_URL`/`SUPABASE_KEY` from GitHub Actions secrets
- Auto-push on every save removed — sync is manual only at this stage

---

## [0.5.1] - 2026-06-04

### Added
- `OsVersionHelper` — `IsWindows11OrGreater` (build ≥ 22000), `SupportsAcrylic` (build
  ≥ 19041), and backdrop factory helpers

### Changed
- Mica backdrop falls back to `DesktopAcrylicBackdrop` on Windows 10 2004+; no backdrop
  below that
- DWM corner/border attribute calls guarded with `IsWindows11OrGreater` (introduced in
  build 22000)

---

## [0.5.0] - 2026-06-03

### Added
- `Delete` deletes the selected task (guarded — no-op while typing)
- `Ctrl+D` opens the due-date calendar picker in the details pane
- `Ctrl+M` toggles My Day on the selected task
- `↑`/`↓` moves selection through the task list with a live details pane
- `Ctrl+Enter` exits the notes box and moves focus to the title
- `Escape` in the pane returns focus to the page for immediate arrow navigation

---

## [0.4.1] - 2026-06-03

### Added
- List reordering — Move up / Move down in the right-click context menu, disabled at
  section boundaries; cross-section move pins/unpins automatically

### Fixed
- `NavigationView.IsPaneOpen` `COMException` in Auto pane mode

---

## [0.4.0] - 2026-05-22

### Added
- Slide-in details pane (~280px) opens on task tap and stays open when switching tasks
- Inline title edit (auto-focused on open), multiline notes, My Day toggle, due date
  picker, created-at timestamp
- Native `ListView` `SelectionMode="Single"` with a custom template: 3px accent left
  border + subtle fill on the selected row

### Changed
- Window resized to 620×640; `NavigationView` adaptive (compact <720px, expanded ≥720px)

### Fixed
- Phantom selection on the Planned page from deferred `ICollectionView.CurrentItem`
  auto-positioning

---

## [0.3.1] - 2026-05-15

### Added
- Open/completed tasks separated in My Day, Important, All Tasks via a collapsible
  `Expander`, collapsed by default with a count header
- Task moves into the completed group after a 250ms delay (strikethrough/fade
  animates first); 4s undo snackbar reverses a completion
- Congratulatory empty state when all tasks in a list are done

### Fixed
- Nav indicator now syncs when navigating programmatically (e.g. a tip action)

---

## [0.3.0] - 2026-05-12

### Added
- Contextual Tip Engine (`TipEngine.cs`) — priority-based tips: overdue → My Day empty
  → progress milestone → time-based greeting → no tasks → fallback
- Smart fallback suppression (skips filler tips when recently active or a meaningful
  tip was shown < 4 hours ago) and adaptive silence (3 early dismissals → 3-day cooldown)
- Rich `Tip` model — `Severity`, `DismissAfterMs`, `IsMeaningful`, optional actionable
  `TipAction`; hover-pause on the countdown

### Changed
- Bubble window sizes dynamically to content instead of a fixed height

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
