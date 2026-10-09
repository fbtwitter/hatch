[CmdletBinding()]
param(
    [string]$ArtifactRoot = (Join-Path $env:TEMP "Hatch.UiRegression"),
    [ValidateSet('default', 'proactive-action', 'proactive-no-action', 'oversized-quick-add', 'proactive-collapse-reopen', 'workflows-light', 'workflows-dark', 'onboarding')]
    [string[]]$Profile
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$windowsRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $windowsRoot "hatch.csproj"
$testProject = Join-Path $windowsRoot "Hatch.Tests\Hatch.Tests.csproj"
$appExe = Join-Path $windowsRoot "bin\x64\Release\net10.0-windows10.0.19041.0\hatch.exe"

New-Item -ItemType Directory -Path $ArtifactRoot -Force | Out-Null
$ArtifactRoot = (Resolve-Path -LiteralPath $ArtifactRoot).Path
$runId = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$runRoot = Join-Path $ArtifactRoot $runId
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

Write-Host "Building self-contained Release x64 app for UI tests..."
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& dotnet build $appProject --configuration Release -p:Platform=x64 -p:WindowsPackageType=None -p:EnableMsixTooling=false -p:GenerateAppxPackageOnBuild=false -p:WindowsAppSDKSelfContained=true -p:WindowsAppSDKDeploymentManagerInitialize=false -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true
$appBuildExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousErrorActionPreference
if ($appBuildExitCode -ne 0) {
    throw "Self-contained app build failed with exit code $appBuildExitCode."
}

if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Self-contained UI test app was not produced at $appExe."
}

Write-Host "Building FlaUI test project..."
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& dotnet build $testProject --configuration Debug -p:NuGetAudit=false
$buildExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousErrorActionPreference
if ($buildExitCode -ne 0) {
    throw "FlaUI test project build failed with exit code $buildExitCode."
}

$profiles = @(
    [pscustomobject]@{ Name = "default"; Filter = "FullyQualifiedName!~TaskWorkflowTests&FullyQualifiedName!~OnboardingTests"; Variables = @{} },
    [pscustomobject]@{
        Name = "proactive-action"
        Filter = "FullyQualifiedName~CompanionLayoutTests.TipSurfaces_MatchWidthAndVerticalSpacing"
        Variables = @{ HATCH_TEST_PROACTIVE = "1" }
    },
    [pscustomobject]@{
        Name = "proactive-no-action"
        Filter = "FullyQualifiedName~CompanionLayoutTests.TipSurfaces_MatchWidthAndVerticalSpacing"
        Variables = @{ HATCH_TEST_PROACTIVE = "1"; HATCH_TEST_NO_ACTION_TIP = "1" }
    },
    [pscustomobject]@{
        Name = "oversized-quick-add"
        Filter = "FullyQualifiedName~RuntimeMeasurementTests.OversizedTip_RemainsScrollableWithinWorkArea"
        Variables = @{ HATCH_TEST_LONG_TIP = "1" }
    },
    [pscustomobject]@{
        Name = "proactive-collapse-reopen"
        Filter = "FullyQualifiedName~RuntimeMeasurementTests.OversizedTip_RemainsScrollableWithinWorkArea"
        Variables = @{ HATCH_TEST_PROACTIVE = "1"; HATCH_TEST_LONG_TIP = "1" }
    },
    [pscustomobject]@{
        Name = "workflows-light"
        Filter = "FullyQualifiedName~TaskWorkflowTests"
        Variables = @{ HATCH_TEST_THEME = "1" }
    },
    [pscustomobject]@{
        Name = "workflows-dark"
        Filter = "FullyQualifiedName~TaskWorkflowTests"
        Variables = @{ HATCH_TEST_THEME = "2" }
    },
    [pscustomobject]@{
        Name = "onboarding"
        Filter = "FullyQualifiedName~OnboardingTests"
        Variables = @{ HATCH_TEST_ONBOARDING = "1" }
    }
)
if ($Profile) { $profiles = @($profiles | Where-Object { $_.Name -in $Profile }) }

$profileVariables = @(
    "HATCH_UI_TEST",
    "HATCH_UI_TEST_DATA_DIR",
    "HATCH_MEASUREMENTS_DIR",
    "HATCH_APP_EXE",
    "HATCH_TEST_MUTE",
    "HATCH_TEST_THEME",
    "HATCH_TEST_ONBOARDING",
    "HATCH_TEST_PROACTIVE",
    "HATCH_TEST_NO_ACTION_TIP",
    "HATCH_TEST_LONG_TIP",
    "HATCH_TEST_MASCOT_X",
    "HATCH_TEST_MASCOT_Y",
    "HATCH_GCDUMP_TOOL"
)
$originalEnvironment = @{}
foreach ($name in $profileVariables) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

$results = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($testProfile in $profiles) {
        foreach ($name in $profileVariables) {
            [Environment]::SetEnvironmentVariable($name, $null, "Process")
        }

        $profileRoot = Join-Path $runRoot $testProfile.Name
        $dataDirectory = Join-Path $profileRoot "data"
        $measurementDirectory = Join-Path $profileRoot "measurements"
        New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
        New-Item -ItemType Directory -Path $measurementDirectory -Force | Out-Null

        $env:HATCH_UI_TEST_DATA_DIR = $dataDirectory
        $env:HATCH_MEASUREMENTS_DIR = $measurementDirectory
        $env:HATCH_APP_EXE = $appExe
        $env:HATCH_TEST_MUTE = "1"
        foreach ($name in $testProfile.Variables.Keys) {
            [Environment]::SetEnvironmentVariable($name, $testProfile.Variables[$name], "Process")
        }

        Write-Host "Running UI profile: $($testProfile.Name)"
        $arguments = @(
            "test", $testProject,
            "--configuration", "Debug",
            "--no-build",
            "--no-restore",
            "--blame-hang", "--blame-hang-timeout", "10m",
            "--logger", "trx;LogFileName=$($testProfile.Name).trx",
            "--results-directory", $profileRoot
        )
        if ($testProfile.Filter) {
            $arguments += @("--filter", $testProfile.Filter)
        }

        $ErrorActionPreference = "Continue"
        & dotnet @arguments
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = "Stop"

        # dotnet test can return success when a filter matched no tests. Require
        # real passing results, and permit skips only for the two conditional
        # tip tests exercised in separate profiles.
        $passed = 0
        $skipped = 0
        $reportError = $null
        try {
            [xml]$trx = Get-Content -LiteralPath (Join-Path $profileRoot "$($testProfile.Name).trx") -Raw
            $testResults = @($trx.TestRun.Results.UnitTestResult)
            $passed = @($testResults | Where-Object { $_.outcome -eq 'Passed' }).Count
            $skipped = @($testResults | Where-Object { $_.outcome -eq 'NotExecuted' }).Count
            $failed = @($testResults | Where-Object { $_.outcome -notin @('Passed', 'NotExecuted') }).Count
            $minimumPassed = if ($testProfile.Name -eq 'default') { 9 } elseif ($testProfile.Name -like 'workflows-*') { 8 } else { 1 }
            $allowedSkipped = if ($testProfile.Name -eq 'default') { 2 } else { 0 }
            if ($passed -lt $minimumPassed -or $skipped -gt $allowedSkipped -or $failed -gt 0) {
                throw "Expected at least $minimumPassed passes, at most $allowedSkipped skips, and no other outcomes; got $passed passes, $skipped skips, $failed failures."
            }
        }
        catch {
            $reportError = $_.Exception.Message
            $exitCode = 1
        }

        $results.Add([pscustomobject]@{
            Name = $testProfile.Name
            ExitCode = $exitCode
            Passed = $passed
            Skipped = $skipped
            ReportError = $reportError
            ArtifactDirectory = $profileRoot
        })
        if ($exitCode -ne 0) {
            $failures.Add("$($testProfile.Name) (exit code $exitCode)")
        }
    }
}
finally {
    foreach ($name in $profileVariables) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], "Process")
    }
    $ErrorActionPreference = $previousErrorActionPreference
}

$summary = [pscustomobject]@{
    RunId = $runId
    Profiles = $results
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot "summary.json") -Encoding utf8
Write-Host "UI regression artifacts: $runRoot"

if ($failures.Count -gt 0) {
    throw "UI regression profiles failed: $($failures -join ', ')"
}

Write-Host "All $($profiles.Count) UI regression profiles passed."
