#Requires -Version 5.1
<#
.SYNOPSIS
    Fetches the cvc5 release executable into the git-ignored .cvc5/ folder, for development and CI.

.DESCRIPTION
    ADR 0050 decision 6: equiv ships no cvc5. Its release binary links GMP and LibPoly, which
    are LGPL-3.0, so under ADR 0017 it is a standalone executable run as a process and never
    redistributed: no cvc5 file is in this repository, in a published binary, in the image or
    in the action. A run uses cvc5 only when equiv.config.json names an executable
    (solvers.cvc5.path).

    This script downloads the exact release archive from the cvc5/cvc5 GitHub release and
    checks it against the SHA-256 pinned beside this script, as tools/z3-feed/fetch.ps1 does
    for Z3 (ADR 0030); a mismatch deletes the file and fails. It then unpacks the archive and
    prints the path of cvc5.exe. Windows only: the Linux legs and the container image have no
    cvc5 (ticket P1-033, out of scope).

    Idempotent: if the archive already in .cvc5/ matches the pinned hash and is unpacked,
    nothing is downloaded.
#>
$ErrorActionPreference = "Stop"
# Windows PowerShell 5.1 redraws the Invoke-WebRequest progress bar per chunk, which makes a
# download many times slower.
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$version = "1.4.1"
$archiveName = "cvc5-Win64-x86_64-static.zip"
$url = "https://github.com/cvc5/cvc5/releases/download/cvc5-$version/$archiveName"
$expectedHash = (Get-Content (Join-Path $PSScriptRoot "$archiveName.sha256") -Raw).Trim()

$solverDir = Join-Path $repoRoot ".cvc5"
$archive = Join-Path $solverDir $archiveName
$executable = Join-Path $solverDir "cvc5-Win64-x86_64-static/bin/cvc5.exe"

New-Item -ItemType Directory -Force -Path $solverDir | Out-Null

if (Test-Path $archive) {
    $actualHash = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        Remove-Item -Path $archive -Force
    }
}

if (-not (Test-Path $archive)) {
    Write-Host "Downloading $archiveName ($version) from the cvc5/cvc5 GitHub release..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $url -OutFile $archive

    $actualHash = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        Remove-Item -Path $archive -Force
        Write-Error "SHA-256 mismatch for $archiveName. Expected $expectedHash, got $actualHash. The download was deleted."
        exit 1
    }

    Write-Host "$archiveName verified." -ForegroundColor Green
    # A stale unpacked copy must not outlive the archive it came from.
    $unpacked = Join-Path $solverDir "cvc5-Win64-x86_64-static"
    if (Test-Path $unpacked) {
        Remove-Item -Path $unpacked -Recurse -Force
    }
}

if (-not (Test-Path $executable)) {
    Expand-Archive -Path $archive -DestinationPath $solverDir -Force
}

if (-not (Test-Path $executable)) {
    Write-Error "cvc5.exe was not found in $archiveName at $executable."
    exit 1
}

Write-Host "cvc5 $version is at $executable" -ForegroundColor DarkGray
exit 0
