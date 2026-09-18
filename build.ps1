#!/usr/bin/env pwsh
<#
The one entry point for every gate in docs/QUALITY-GATES.md that runs locally.
Fails fast: the first non-zero exit code stops the script.

-Integration also runs tests/Equiv.Tests.Integration (needs VS Build Tools + the .NET
Framework 4.8 targeting pack; Windows only, see README).

-SonarBuild is for sonar.yml only. dotnet-sonarscanner begin injects SonarAnalyzer.CSharp
into the compile for its duration; that analyzer's diagnostics on pre-existing code would
otherwise hard-fail -warnaserror, and dotnet format would try to apply its fixes and trip
--verify-no-changes, both before Sonar's own (new-code-only) quality gate ever runs. This
switch drops -warnaserror from the build step and skips the format step, which ci.yml's
`gates` job already enforces unconditionally.
#>
param(
    [switch]$Integration,
    [switch]$SonarBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Script
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Script
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$Name failed (exit $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
}

Invoke-Step "restore" { dotnet restore --locked-mode }
Invoke-Step "build" {
    if ($SonarBuild) {
        dotnet build --no-restore
    } else {
        dotnet build --no-restore -warnaserror
    }
}

if (-not $SonarBuild) {
    Invoke-Step "format" { dotnet format --no-restore --verify-no-changes }
}

Invoke-Step "test" {
    $testResultsDir = Join-Path $repoRoot "TestResults"
    if (Test-Path $testResultsDir) {
        Remove-Item -Path $testResultsDir -Recurse -Force
    }

    $testProjects = Get-ChildItem -Path $repoRoot -Filter "*.csproj" -Recurse |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
            $_.FullName -notmatch '[\\/]src[\\/]' -and
            ($_.FullName -match '[\\/]tests[\\/]' -or $_.Name -like '*.Tests.csproj')
        }

    foreach ($project in $testProjects) {
        if (-not $Integration -and $project.BaseName -eq "Equiv.Tests.Integration") {
            Write-Host "  (skipping $($project.BaseName); pass -Integration to run)" -ForegroundColor DarkGray
            continue
        }

        Write-Host "  -- $($project.BaseName)" -ForegroundColor DarkCyan
        dotnet test $project.FullName --no-restore --no-build -- --coverlet --coverlet-output-format cobertura --coverlet-output-format opencover
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}

Invoke-Step "check-coverage" {
    $summaryPath = Join-Path $repoRoot "TestResults/coverage-summary.txt"
    dotnet run --no-restore --no-build --project (Join-Path $repoRoot "tools/check-coverage") -- `
        --test-results (Join-Path $repoRoot "TestResults") `
        --src (Join-Path $repoRoot "src") | Tee-Object -FilePath $summaryPath
}

Write-Host "All gates green." -ForegroundColor Green
