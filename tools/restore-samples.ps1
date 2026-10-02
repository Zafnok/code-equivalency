#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Restores every project under samples/ (Windows only: needs VS Build Tools, see README).

.DESCRIPTION
    samples/** is outside Equiv.slnx (M1-001: isolated from root build settings), so the
    repo-root restore never touches it. Every sample until M2-005 referenced only the BCL,
    so nothing needed restoring before MSBuildWorkspace (which evaluates/builds but never
    restores) opened it. webapi-basic is the first with real PackageReferences: the modern
    (SDK-style) side restores fine with `dotnet restore`, but the legacy (non-SDK) side's
    PackageReference support needs full MSBuild.exe (VS Build Tools) -- `dotnet
    restore`/`dotnet build` writes an empty obj/*.nuget.g.targets for a non-SDK project and
    silently never resolves the package into a <Reference>.

    Called by build.ps1 -Integration and by dependabot-fixup.yml (tools/licence-check only
    checks or regenerates THIRD-PARTY-NOTICES.md once samples/ is restored).
#>
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) {
    Write-Error "MSBuild.exe not found via vswhere; cannot restore samples/**/legacy"
    exit 1
}

Get-ChildItem -Path (Join-Path $repoRoot "samples") -Filter "*.csproj" -Recurse | ForEach-Object {
    if ($_.FullName -match '[\\/]legacy[\\/]') {
        & $msbuild $_.FullName -t:Restore -v:minimal -nologo
    } else {
        dotnet restore $_.FullName
    }
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

exit 0
