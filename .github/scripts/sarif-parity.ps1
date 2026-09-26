#!/usr/bin/env pwsh
<#
M3-029 parity job: diff the SARIF results `equiv compare` wrote for every sample on two operating systems
(parity-run.ps1 output). Per sample, runs[0].results are compared as multisets of their rule id, level, message,
logical locations and properties. Physical locations and URIs are left out, and in what is compared the checkout
root each leg ran from becomes <checkout> and backslashes become slashes, so nothing rooted in the checkout can
differ. Any other difference, or a sample one leg has no SARIF for, fails.
#>
param(
    [Parameter(Mandatory)] [string]$Left,
    [Parameter(Mandatory)] [string]$Right
)

$ErrorActionPreference = 'Stop'

# Sorted keys at every level, so two logs that differ only in property order compare equal.
function ConvertTo-Canonical($node) {
    if ($node -is [System.Collections.IDictionary]) {
        $ordered = [ordered]@{}
        foreach ($key in ($node.Keys | Sort-Object -CaseSensitive)) { $ordered[$key] = ConvertTo-Canonical $node[$key] }
        return $ordered
    }
    if ($node -is [System.Collections.IList]) {
        return , @($node | ForEach-Object { ConvertTo-Canonical $_ })
    }
    return $node
}

function Get-Results([string]$Dir, [string]$Sample) {
    $path = Join-Path $Dir "$Sample.sarif"
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $root = (Get-Content -LiteralPath (Join-Path $Dir 'checkout.txt') -Raw).Trim()
    $log = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable -Depth 100
    $results = @($log.runs[0].results | Where-Object { $null -ne $_ })
    # The comma keeps an empty array an array; PowerShell would otherwise unroll it to $null, which means "missing".
    return , @($results | ForEach-Object {
        $result = [ordered]@{
            ruleId           = $_.ruleId
            level            = $_.level
            message          = $_.message.text
            logicalLocations = @($_.locations | ForEach-Object { $_.logicalLocations })
            properties       = $_.properties
        }
        $json = ConvertTo-Canonical $result | ConvertTo-Json -Depth 100 -Compress
        $json.Replace($root.Replace('\', '\\'), '<checkout>').Replace($root.Replace('\', '/'), '<checkout>').Replace($root, '<checkout>').Replace('\\', '/')
    } | Sort-Object -CaseSensitive)
}

$samples = @(Get-ChildItem -Path $Left, $Right -Filter '*.sarif' | ForEach-Object { $_.BaseName } | Sort-Object -Unique)
if ($samples.Count -eq 0) { throw "no SARIF files under '$Left' or '$Right'" }

$failures = 0
foreach ($sample in $samples) {
    $leftResults = Get-Results $Left $sample
    $rightResults = Get-Results $Right $sample
    if ($null -eq $leftResults -or $null -eq $rightResults) {
        Write-Host "::error::$sample`: SARIF missing on $(if ($null -eq $leftResults) { $Left } else { $Right })"
        $failures++
        continue
    }

    $diff = @(Compare-Object -ReferenceObject $leftResults -DifferenceObject $rightResults -CaseSensitive)
    if ($diff.Count -eq 0) {
        Write-Host "$sample`: $($leftResults.Count) results match"
        continue
    }

    $failures++
    Write-Host "::error::$sample`: the results differ"
    foreach ($entry in $diff) {
        $side = if ($entry.SideIndicator -eq '<=') { $Left } else { $Right }
        Write-Host "  only in $side`: $($entry.InputObject)"
    }
}

if ($failures -gt 0) {
    Write-Host "Exit codes: $Left $(Get-Content -Raw (Join-Path $Left 'exit-codes.json')) $Right $(Get-Content -Raw (Join-Path $Right 'exit-codes.json'))"
    throw "$failures sample(s) differ between '$Left' and '$Right'"
}

Write-Host "All $($samples.Count) samples match."
