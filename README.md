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

![Hatch My Day screen](docs/screenshots/02-my-day.png)

## Features

- Capture tasks from the always-on-top desktop mascot.
- Organize work with My Day, custom lists, tags, due dates, recurrence, and checklists.
- Use Focus Mode and the Windows 11 My Day widget.
- Keep tasks on your device or enable end-to-end encrypted sync with the [Android companion](https://github.com/fbtwitter/hatch-mobile).

## Install

**Microsoft Store:** [Install Hatch](https://apps.microsoft.com/detail/9PKTQFG9S3K8). No certificate setup is needed. Store releases may lag behind GitHub releases.

**GitHub release:** For background updates, install the signing certificate from the [latest release](https://github.com/fbtwitter/hatch/releases/latest) once, then open the [App Installer feed](https://fbtwitter.github.io/hatch/Hatch.appinstaller) and select **Install**. Install the certificate to **Local Machine > Trusted People**.

## Build and run without an IDE

On Windows 10 (build 17763+) or Windows 11, install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and the [Windows App SDK 2.5.1 runtime](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads). The Debug build is unpackaged and needs that runtime installed. You also need internet access for the first NuGet restore. Visual Studio or JetBrains Rider does not need to be open.

Open PowerShell in the repository root and run:

```powershell
dotnet restore .\windows\hatch.csproj -p:Platform=x64
dotnet build .\windows\hatch.csproj -c Debug --no-restore -p:Platform=x64
dotnet run --project .\windows\hatch.csproj -c Debug --no-build -p:Platform=x64 --no-launch-profile
```

The `dotnet build` command builds Hatch without opening it; only `dotnet run` launches the app. Use the full word `Debug` for the configuration. If you are setting up a new PC, Microsoft's [command-line WinUI setup guide](https://learn.microsoft.com/en-us/windows/apps/get-started/start-here?tabs=stable) covers the required development tools. See the [contributing guide](.github/CONTRIBUTING.md) for more project details.

## Privacy

Tasks are stored in `%LocalAppData%\Hatch` by default. No account or internet connection is required to use Hatch. Optional sync is off until you sign in, and the app includes no analytics or remote crash reporting.

Read the [privacy policy](docs/privacy-policy.md).

## Troubleshooting

**Crash diagnostics:** Hatch writes startup and unhandled exception details to `%LocalAppData%\Hatch\crash.log` in Debug and installed builds. The log stays on your device and may contain task data in exception details. Some native WinUI crashes bypass this log. To capture a local minidump for those crashes during development, run `windows\scripts\enable-crash-dumps.ps1` from an elevated PowerShell window. Windows saves dumps to `%LocalAppData%\CrashDumps` for the crashing user. Dumps may contain task data, so review them before sharing. This opt-in setting applies to `hatch.exe` until you remove its `HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\hatch.exe` registry key.

**Remove the shadow around the mascot:** Search Windows for **Adjust the appearance and performance of Windows**, then clear **Show shadows under windows**. Windows does not offer a per-app setting to disable this shadow.

## Project links

[Changelog](CHANGELOG.md) · [Roadmap](.github/ROADMAP.md) · [Architecture](.github/ARCHITECTURE.md) · [License](LICENSE)
