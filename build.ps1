#!/usr/bin/env pwsh
<#
The one entry point for every gate in docs/QUALITY-GATES.md that runs locally.
Fails fast: the first non-zero exit code stops the script.
#>
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
Invoke-Step "build" { dotnet build --no-restore -warnaserror }
Invoke-Step "format" { dotnet format --no-restore --verify-no-changes }
Invoke-Step "test" { dotnet test --no-restore --no-build -- --coverlet --coverlet-output-format cobertura }
Invoke-Step "check-coverage" {
    dotnet run --no-restore --no-build --project (Join-Path $repoRoot "tools/check-coverage") -- `
        --test-results (Join-Path $repoRoot "TestResults") `
        --src (Join-Path $repoRoot "src")
}

Write-Host "All gates green." -ForegroundColor Green
