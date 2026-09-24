#Requires -Version 5.1
<#
.SYNOPSIS
    Fills the git-ignored .z3-feed/ local NuGet feed with the official Microsoft.Z3 nupkg.

.DESCRIPTION
    ADR 0030: Microsoft.Z3 is restored from the Z3Prover/z3 GitHub release, not nuget.org
    (which stops at 4.12.2). This script downloads the exact release asset and checks it
    against the SHA-256 pinned beside this script; a mismatch deletes the file and fails
    the build rather than restoring an unverified package. Run before every `dotnet
    restore` (build.ps1 and every CI workflow do this).

    Idempotent: if a file already in .z3-feed/ matches the pinned hash, nothing is
    downloaded.
#>
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packageName = "Microsoft.Z3.5.1.0.nupkg"
$url = "https://github.com/Z3Prover/z3/releases/download/z3-5.1.0/$packageName"
$expectedHash = (Get-Content (Join-Path $PSScriptRoot "$packageName.sha256") -Raw).Trim()

$feedDir = Join-Path $repoRoot ".z3-feed"
$destination = Join-Path $feedDir $packageName

New-Item -ItemType Directory -Force -Path $feedDir | Out-Null

if (Test-Path $destination) {
    $actualHash = (Get-FileHash -Path $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -eq $expectedHash) {
        Write-Host "$packageName already present in .z3-feed/ with the expected hash." -ForegroundColor DarkGray
        exit 0
    }

    Remove-Item -Path $destination -Force
}

Write-Host "Downloading $packageName from the Z3Prover/z3 GitHub release..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $url -OutFile $destination

$actualHash = (Get-FileHash -Path $destination -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    Remove-Item -Path $destination -Force
    Write-Error "SHA-256 mismatch for $packageName. Expected $expectedHash, got $actualHash. The download was deleted."
    exit 1
}

Write-Host "$packageName verified." -ForegroundColor Green
exit 0
