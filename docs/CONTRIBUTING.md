# Contributing — Hatch

Thank you for considering contributing to Hatch! This guide explains how to set up, build, and submit changes.

---

## Prerequisites

- **Windows 11** (build 22000+)
- **Visual Studio 2022** with Windows App SDK workload, OR
- **.NET 10 SDK** + **Windows App SDK** installed separately
- **Git**

### Install Windows App SDK

```powershell
# Via Visual Studio Installer (recommended)
# Workload: ".NET Desktop development" → includes Windows App SDK

# Or standalone
winget install Microsoft.WindowsAppSDK
```

---

## Building from Source

### 1. Clone & Navigate

```powershell
git clone https://github.com/fbtwitter/hatch.git
cd hatch
```

### 2. Build

```powershell
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Run (Debug only; requires Windows App SDK)
dotnet run
```

Placeholder assets are generated automatically on first build.

---

## Project Structure

See [Architecture](ARCHITECTURE.md) for a complete folder layout.

**Key directories:**
- `Models/` — Data types (TodoItem, TaskList, AppSettings)
- `ViewModels/` — UI logic, commands, collections
- `Views/` — XAML + code-behind (MainWindow, MascotWindow, etc.)
- `Services/` — Storage, notifications, tips
- `Converters/` — Value converters for binding
- `NativeMethods.cs` — P/Invoke declarations

**One type per file. File name = class name.**

---

## Development Workflow

1. **Create a branch**
   ```powershell
   git checkout -b feature/your-feature-name
   ```
   Naming: `feature/<name>`, `fix/<name>`, `chore/<name>`

2. **Make changes** following [Coding Standards](../context/coding-standards.md)
   - No hard-coded colors (use `ThemeResource` tokens)
   - No logic in code-behind (ViewModels only)
   - All async I/O is async (no `Task.Run`)
   - Include memory impact notes for services/timers

3. **Build & test**
   ```powershell
   dotnet build
   ```
   Build must pass. No warnings elevated to errors (yet).

4. **Test manually**
   - Run the app (`dotnet run`)
   - Test the feature in a real window
   - Test dark/light mode switching
   - Test multi-monitor positioning if applicable

5. **Commit**
   ```powershell
   git add <files>
   git commit -m "feat: brief description of change"
   ```
   **Conventional commits:** `feat:`, `fix:`, `chore:`, `refactor:`, `docs:`
   - One logical change per commit
   - No AI attribution lines
   - Precise, specific messages (CI auto-generates release notes)

6. **Push & create PR**
   ```powershell
   git push origin feature/your-feature-name
   # Then open PR on GitHub
   ```

---

## Code Review Checklist

Before submitting a PR, verify:

- ✅ Build passes (`dotnet build`)
- ✅ WinUI 3 namespaces only (no `Windows.UI.Xaml.*` or WPF)
- ✅ No outbound network calls or HTTP-capable dependencies
- ✅ MVVM discipline:
  - No logic in code-behind
  - All property changes raise `PropertyChanged`
  - Commands use `ICommand` (RelayCommand)
- ✅ All UI updates on dispatcher thread (no `Task.Run`)
- ✅ `TaskStorageService.SaveAsync()` called after every task mutation
- ✅ No raster assets (Lottie vector only)
- ✅ Memory impact within budgets (see [Performance](PERFORMANCE.md))
- ✅ Comments only for non-obvious WHY (not WHAT)
- ✅ No `#nullable disable` pragmas
- ✅ Tests pass (if applicable)

---

## Common Tasks

### Add a New Feature

1. Update `context/current-feature.md` with scope & goals
2. Implement following [Coding Standards](../context/coding-standards.md)
3. Update docs if behavior changes
4. Commit with conventional message
5. Move feature to History in `context/current-feature.md`

### Add a Service

Services handle async I/O, persistence, or business logic.

**Template:**
```csharp
// Services/MyService.cs
namespace Hatch.Services;

public sealed class MyService
{
    public async Task DoSomethingAsync(CancellationToken cancellationToken = default)
    {
        // Async work here
        await Task.Delay(100, cancellationToken);
    }
}
```

**Add memory impact note to PR description:**
```
Memory: New MyService adds ~2MB peak (loaded on startup, cached).
```

### Modify XAML Styles

All colors must use `ThemeResource` tokens:
```xaml
<!-- ✅ Correct -->
<Rectangle Fill="{ThemeResource CardBackgroundFillColorDefaultBrush}" />

<!-- ❌ Wrong -->
<Rectangle Fill="#FF5500" />
```

Theme-aware brushes automatically adjust on dark/light switch.

### Update Persistence

If adding a field to `TodoItem` or `AppSettings`:

1. Update `Models/TodoItem.cs` or `Models/AppSettings.cs`
2. Update `Services/TaskStorageService.cs` migration logic (if breaking)
3. Increment `schemaVersion` in `tasks.json` / `settings.json`
4. Document migration path in [Architecture](ARCHITECTURE.md)

**Example migration:**
```csharp
if (data.SchemaVersion < 2)
{
    foreach (var task in data.Tasks)
    {
        task.NewField ??= DefaultValue;  // Set default for old tasks
    }
}
```

---

## Performance & Memory

Every PR must include a one-line memory impact assessment:

```
Memory: No new services. Minimal overhead (<5MB peak transient).
```

If you add:
- A new service → measure startup + idle memory
- A timer/PeriodicTimer → measure CPU with it running idle
- A large collection → measure with 100+ items loaded

See [Performance](PERFORMANCE.md) for tools and budgets.

---

## Git Conventions

- **Branch naming:** `feature/name`, `fix/name`, `chore/name`
- **Commits:** Conventional format (`feat:`, `fix:`, etc.)
- **No force-push** to main
- **No merge commits** — rebase or squash
- **No WIP commits** in PR

---

## CI/CD

- **Trigger:** Push to branch → runs tests, build check
- **Release:** Tag `v*.*.*` → builds MSIX, runs memory profiling, creates GitHub release
- **Memory gate:** Idle memory check must pass (<50 MB) before release

---

## Questions?

- Check [Architecture](ARCHITECTURE.md) for design decisions
- Check [Coding Standards](../context/coding-standards.md) for language/MVVM rules
- Check [Performance](PERFORMANCE.md) for memory budgets
- Open an issue on GitHub

Thank you for contributing! 🥚
