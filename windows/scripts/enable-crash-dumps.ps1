# Run from an elevated PowerShell window to capture native Hatch crashes.
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}

$werKey = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\hatch.exe'
New-Item -Path $werKey -Force | Out-Null
New-ItemProperty -Path $werKey -Name DumpType -PropertyType DWord -Value 1 -Force | Out-Null
New-ItemProperty -Path $werKey -Name DumpCount -PropertyType DWord -Value 5 -Force | Out-Null

Write-Output 'Native Hatch crash dumps will be saved to %LocalAppData%\CrashDumps for the crashing user.'
