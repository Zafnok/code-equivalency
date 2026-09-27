#!/usr/bin/env pwsh
<#
M3-029/M3-004 parity job, one leg: restore every sample, publish the CLI as the single-file binary
M3-004 ships, run it (not `dotnet run`) as `equiv compare` on every samples/* pair, and write
<OutDir>/<sample>.sarif. The `parity` job diffs the Windows and Ubuntu outputs with sarif-parity.ps1.
Running the published binary, not the build output, is what this job is for: it is the artifact
users get from the release, and PublishSingleFile has its own failure modes (see Equiv.Cli.csproj's
IncludeAllContentForSelfExtract comment) that `dotnet run` never exercises.

Windows restores the legacy (non-SDK) sides with VS Build Tools' MSBuild.exe, as build.ps1 -Integration does.
Elsewhere every side is restored with `dotnet restore`: for a non-SDK project that writes the project.assets.json the
bare loader reads, and packages.config packages are restored by equiv itself (ADR 0031, M3-029).
#>
param(
    [Parameter(Mandatory)] [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$samples = Join-Path $repoRoot 'samples'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$msbuild = $null
if ($IsWindows) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'MSBuild.exe not found via vswhere; cannot restore samples/**/legacy' }
}

Get-ChildItem -Path $samples -Filter '*.csproj' -Recurse | ForEach-Object {
    if ($msbuild -and $_.FullName -match '[\\/]legacy[\\/]') {
        & $msbuild $_.FullName -t:Restore -v:minimal -nologo
    } else {
        dotnet restore $_.FullName
    }
    if ($LASTEXITCODE -ne 0) { throw "restore of $($_.FullName) failed with exit code $LASTEXITCODE" }
}

$cli = Join-Path $repoRoot 'src/Equiv.Cli'
$rid = if ($IsWindows) { 'win-x64' } else { 'linux-x64' }
$publishDir = Join-Path $OutDir '_publish'
dotnet publish $cli -c Release -r $rid -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "publishing the CLI failed with exit code $LASTEXITCODE" }
$exe = Join-Path $publishDir (if ($IsWindows) { 'Equiv.Cli.exe' } else { 'Equiv.Cli' })

$exitCodes = [ordered]@{}
foreach ($sample in Get-ChildItem -Path $samples -Directory | Sort-Object Name) {
    $legacy = Get-ChildItem -Path (Join-Path $sample.FullName 'legacy') -Filter '*.sln' | Select-Object -First 1
    $modern = Get-ChildItem -Path (Join-Path $sample.FullName 'modern') -Filter '*.slnx' | Select-Object -First 1
    $out = Join-Path $OutDir "$($sample.Name).sarif"
    & $exe compare --legacy $legacy.FullName --modern $modern.FullName --out $out
    # 0, 1 (a Divergent result) and 4 (a skipped project) still write SARIF; the diff decides whether the legs agree.
    $exitCodes[$sample.Name] = $LASTEXITCODE
}

$exitCodes | ConvertTo-Json | Set-Content -Path (Join-Path $OutDir 'exit-codes.json') -Encoding utf8
Set-Content -Path (Join-Path $OutDir 'checkout.txt') -Value $repoRoot.Path -Encoding utf8
$exitCodes | Format-Table -AutoSize | Out-String | Write-Host
