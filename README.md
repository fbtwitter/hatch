<div align="center">
  <img src="assets/logo.svg" width="112" alt="Hatch logo">
  <h1>Hatch</h1>
  <p>A local-first Windows to-do app with an always-on-top mascot for quick task capture.</p>
  <p>
    <img src="https://img.shields.io/badge/Windows-10%2F11-0078D4?style=flat-square&logo=windows11" alt="Windows 10 and 11">
    <img src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=.net" alt=".NET 10">
    <img src="https://img.shields.io/badge/WinUI-3-0078D4?style=flat-square" alt="WinUI 3">
    <img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="MIT License">
  </p>
  <p>
    <a href="https://apps.microsoft.com/detail/9PKTQFG9S3K8"><img src="https://img.shields.io/badge/Microsoft_Store-Get_it-0078D4?style=flat-square&logo=microsoft&logoColor=white" alt="Get Hatch from Microsoft Store"></a>
    <a href="https://fbtwitter.github.io/hatch/Hatch.appinstaller"><img src="https://img.shields.io/badge/App_Installer-Download-0078D4?style=flat-square&logo=windows11&logoColor=white" alt="Install Hatch with App Installer"></a>
    <a href="https://github.com/fbtwitter/hatch/releases"><img src="https://img.shields.io/badge/GitHub_Releases-Download-181717?style=flat-square&logo=github&logoColor=white" alt="Download a Hatch release from GitHub"></a>
  </p>
</div>

![Hatch My Day with a daily plan, task priorities, and completed tasks](docs/screenshots/02-my-day.png)

Capture a task when it comes to mind. Choose what matters today, and keep upcoming deadlines in view.

## Features

- **Quick capture:** add tasks from the desktop mascot, with optional due dates and an Important mark.
- **Your daily plan:** choose tasks for My Day and see your progress as you complete them.
- **Upcoming deadlines:** browse Planned as an agenda or calendar, see tasks due on a selected day, and jump to overdue tasks.
- **Details when you need them:** organize tasks with custom lists, notes, tags, priorities, recurring tasks, and checklists.
- Use Focus Mode and the Windows 11 My Day widget.
- Keep tasks on your device or enable end-to-end encrypted sync with the [Android companion](https://github.com/fbtwitter/hatch-mobile).

## A closer look

### Capture from your desktop

Click the mascot to open Quick Add. Give the task a title, choose its list, and add a due date or mark it Important before saving.

<p align="center">
  <img src="docs/screenshots/08-mascot-quick-add.png" width="552" alt="Hatch desktop mascot beside its open Quick Add bubble, with a sample task, list, due date, and Important button">
</p>

### See what's due

Planned opens as an agenda. Switch to Calendar to browse deadlines by day; choosing a date also sets the due date for a fresh task draft. An Overdue shortcut keeps missed deadlines within reach.

![Hatch Planned calendar with due-date markers and tasks for the selected day](docs/screenshots/06-planned-calendar.png)

### Keep the next step clear

Break a bigger task into steps and keep useful notes alongside it. Set a due date and priority, or use the separate Important star to keep it within reach.

![Hatch task details with notes, a checklist, due date, and a priority-coloured completion checkbox](docs/screenshots/07-task-details.png)

*The task-list examples use sample tasks.* [Browse all screenshots](docs/screenshots/).

## Install

**Microsoft Store:** [Install Hatch](https://apps.microsoft.com/detail/9PKTQFG9S3K8). No certificate setup is needed. Store releases may lag behind GitHub releases.

**GitHub release:** For background updates, install the signing certificate from the [latest release](https://github.com/fbtwitter/hatch/releases/latest) once, then open the [App Installer feed](https://fbtwitter.github.io/hatch/Hatch.appinstaller) and select **Install**. Install the certificate to **Local Machine > Trusted People**.

## Build and run without an IDE

### Requirements

- Windows 10 (build 17763+) or Windows 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- [Windows App SDK 2.6.4 runtime](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads) for the unpackaged Debug build.
- Internet access for the first NuGet restore.

For a new PC, follow Microsoft's [command-line WinUI setup guide](https://learn.microsoft.com/en-us/windows/apps/get-started/start-here?tabs=stable).

### Commands

Open PowerShell in the repository root:

1. Restore dependencies:

   ```powershell
   dotnet restore .\windows\hatch.csproj -p:Platform=x64
   ```

2. Build the x64 Debug app:

   ```powershell
   dotnet build .\windows\hatch.csproj -c Debug --no-restore -p:Platform=x64
   ```

3. Launch Hatch:

   ```powershell
   dotnet run --project .\windows\hatch.csproj -c Debug --no-build -p:Platform=x64 --no-launch-profile
   ```

Hatch starts with the desktop mascot. Use its **Show Main Window** action to open the task pages.

See the [contributing guide](.github/CONTRIBUTING.md) for development and testing instructions.

## Privacy

Tasks are stored in `%LocalAppData%\Hatch` by default. No account or internet connection is required to use Hatch. Optional sync is off until you sign in, and the app includes no analytics or remote crash reporting.

Read the [privacy policy](PRIVACY.md).

## Troubleshooting

### Hatch crashes

1. Press **Win + R**, enter `%LocalAppData%\Hatch`, and open `crash.log`.
2. Check the latest entries for startup or unhandled exceptions. This log is available in both Debug and installed builds.

Some native crashes are not recorded in the log. For development diagnostics, use the optional steps below.

<details>
<summary>Capture a crash dump</summary>

1. Open **PowerShell as administrator** and navigate to the repository root.
2. Enable local crash dumps:

   ```powershell
   .\windows\scripts\enable-crash-dumps.ps1
   ```

3. Reproduce the crash, then open `%LocalAppData%\CrashDumps` for the Windows user running Hatch.

To stop collecting dumps, remove this registry key using **Registry Editor as administrator**:

```text
HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\hatch.exe
```

</details>

Logs and dumps stay on your device and may contain task data. Review them before sharing.

### Remove the mascot shadow

1. Search Windows for **Adjust the appearance and performance of Windows**.
2. Clear **Show shadows under windows** and select **Apply**.

This setting affects all windows; Windows has no per-app shadow setting.

## Project links

[Screenshots](docs/screenshots/) · [Changelog](CHANGELOG.md) · [Roadmap](.github/ROADMAP.md) · [Architecture](.github/ARCHITECTURE.md) · [License](LICENSE)
