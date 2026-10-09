[CmdletBinding()]
param(
    [string]$ArtifactRoot = (Join-Path $env:TEMP 'Hatch.Regression'),
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$windowsRoot = Split-Path -Parent $PSScriptRoot
$runRoot = Join-Path $ArtifactRoot ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

if (-not $SkipBuild) {
    & dotnet build (Join-Path $windowsRoot 'hatch.csproj') --configuration Debug -p:Platform=x64 -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true
    if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($suite in @('Unit', 'Integration')) {
    $project = Join-Path $windowsRoot "Hatch.Tests.$suite/Hatch.Tests.$suite.csproj"
    $output = Join-Path $runRoot $suite
    $errorMessage = $null
    $passed = 0
    $skipped = 0
    try {
        & dotnet test $project --configuration Debug -p:NuGetAudit=false --logger "trx;LogFileName=$suite.trx" --results-directory $output --blame-hang --blame-hang-timeout 2m
        if ($LASTEXITCODE -ne 0) { throw "$suite tests failed (exit $LASTEXITCODE)." }
        [xml]$trx = Get-Content -LiteralPath (Join-Path $output "$suite.trx") -Raw
        $testResults = @($trx.TestRun.Results.UnitTestResult)
        $passed = @($testResults | Where-Object { $_.outcome -eq 'Passed' }).Count
        $skipped = @($testResults | Where-Object { $_.outcome -eq 'NotExecuted' }).Count
        $failed = @($testResults | Where-Object { $_.outcome -notin @('Passed', 'NotExecuted') }).Count
        if ($passed -eq 0 -or $skipped -gt 0 -or $failed -gt 0) {
            throw "$suite must contain passing results with no skips or other outcomes; got $passed passes, $skipped skips, $failed failures."
        }
    }
    catch { $errorMessage = $_.Exception.Message }
    $results.Add([pscustomobject]@{ Suite = $suite; Passed = $passed; Skipped = $skipped; Error = $errorMessage })
}

$uiError = $null
$uiPassed = 0
$uiSkipped = 0
$uiArtifactRoot = Join-Path $runRoot 'UI'
try { & (Join-Path $PSScriptRoot 'run-ui-regression.ps1') -ArtifactRoot $uiArtifactRoot }
catch { $uiError = $_.Exception.Message }
try {
    $uiRunRoot = Get-ChildItem -LiteralPath $uiArtifactRoot -Directory |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $uiRunRoot) { throw 'UI regression summary directory was not created.' }
    $uiSummary = Get-Content -LiteralPath (Join-Path $uiRunRoot.FullName 'summary.json') -Raw | ConvertFrom-Json
    $uiPassed = [int](($uiSummary.Profiles | Measure-Object -Property Passed -Sum).Sum)
    $uiSkipped = [int](($uiSummary.Profiles | Measure-Object -Property Skipped -Sum).Sum)
}
catch {
    if (-not $uiError) { $uiError = $_.Exception.Message }
}
$results.Add([pscustomobject]@{ Suite = 'UI'; Passed = $uiPassed; Skipped = $uiSkipped; Error = $uiError })
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Host "Regression results: $runRoot"
$failures = @($results | Where-Object { $_.Error })
if ($failures.Count) { throw ($failures.Error -join [Environment]::NewLine) }
Write-Host 'All regression suites passed.'
