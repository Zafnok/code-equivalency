#!/usr/bin/env pwsh
<#
M3-029 parity job, one leg: restore every sample, build the CLI, run `equiv compare` on every samples/* pair and
write <OutDir>/<sample>.sarif. The `parity` job diffs the Windows and Ubuntu outputs with sarif-parity.ps1.

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
dotnet build $cli -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "building the CLI failed with exit code $LASTEXITCODE" }

$exitCodes = [ordered]@{}
foreach ($sample in Get-ChildItem -Path $samples -Directory | Sort-Object Name) {
    $legacy = Get-ChildItem -Path (Join-Path $sample.FullName 'legacy') -Filter '*.sln' | Select-Object -First 1
    $modern = Get-ChildItem -Path (Join-Path $sample.FullName 'modern') -Filter '*.slnx' | Select-Object -First 1
    $out = Join-Path $OutDir "$($sample.Name).sarif"
    dotnet run --project $cli -c Release --no-build -- compare --legacy $legacy.FullName --modern $modern.FullName --out $out
    # 0, 1 (a Divergent result) and 4 (a skipped project) still write SARIF; the diff decides whether the legs agree.
    $exitCodes[$sample.Name] = $LASTEXITCODE
}

$exitCodes | ConvertTo-Json | Set-Content -Path (Join-Path $OutDir 'exit-codes.json') -Encoding utf8
Set-Content -Path (Join-Path $OutDir 'checkout.txt') -Value $repoRoot.Path -Encoding utf8
$exitCodes | Format-Table -AutoSize | Out-String | Write-Host
