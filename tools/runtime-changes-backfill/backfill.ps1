#Requires -Version 5.1
<#
.SYNOPSIS
    One-off backfill of "changedIn" on every runtime-changes.json row (ticket P2-054; ADR 0040 decision 2).

.DESCRIPTION
    Run once by hand; not run in CI. Every row gets "changedIn", the target framework moniker of the
    first runtime whose behaviour differs:

    - a curated row gets the version its reason names ("in .NET Core 3.0"), else its URL's;
    - a compatibility/<v> URL gets net<v> (netcoreapp3.0 for 3.0);
    - unsupported-apis and fx-core URLs get netcoreapp1.0, the .NET Framework to .NET boundary;
    - a measured row whose reason is .NET Framework's upfront path-character validation gets
      netcoreapp1.0 (the Framework-only check ticket P2-054 names);
    - anything else stays null ("unknown": the row applies whenever the runtimes differ), and is
      printed so the PR can list it.

    The root array becomes { "coveredFrom": "netcoreapp3.0", "rows": [...] }. Rows keep their
    formatting; the script only inserts one line per row and indents the array. It refuses to run
    on a file that already has "coveredFrom".
#>
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$path = Join-Path $repoRoot "src/Equiv.Core/RuntimeChanges/runtime-changes.json"
$lines = [System.IO.File]::ReadAllLines($path)

if ($lines -match '"coveredFrom"') {
    throw "runtime-changes.json already has coveredFrom; the backfill has run"
}

$open = [Array]::IndexOf($lines, "[")
$close = [Array]::LastIndexOf($lines, "]")
$rows = ($lines[$open..$close] -join "`n") | ConvertFrom-Json

function ConvertTo-Moniker([string] $version) {
    $parsed = [Version]::Parse($(if ($version.Contains(".")) { $version } else { "$version.0" }))
    if ($parsed.Major -lt 5) { "netcoreapp$($parsed.Major).$($parsed.Minor)" } else { "net$($parsed.Major).$($parsed.Minor)" }
}

function Get-ChangedIn($row) {
    if ($row.source -eq "curated" -and $row.reason -match '\bin \.NET (?:Core )?(\d+(?:\.\d+)?)\b') {
        return ConvertTo-Moniker $Matches[1]
    }

    if ($row.url -match 'core/compatibility/(?:[a-z-]+/)?(\d+(?:\.\d+)?)[/#]') {
        return ConvertTo-Moniker $Matches[1]
    }

    if ($row.url -match 'core/compatibility/(?:unsupported-apis|fx-core)#') {
        return "netcoreapp1.0"
    }

    if ($row.source -eq "measured" -and $row.reason -match 'path characters|character validation') {
        return "netcoreapp1.0"
    }

    return $null
}

$values = @($rows | ForEach-Object { Get-ChangedIn $_ })
$output = [System.Collections.Generic.List[string]]::new()
$output.AddRange([string[]] $lines[0..($open - 1)])
$output.Add("{")
$output.Add('  "coveredFrom": "netcoreapp3.0",')
$output.Add('  "rows": [')

$row = 0
foreach ($line in $lines[($open + 1)..($close - 1)]) {
    $output.Add("  $line")
    if ($line -match '^(\s*)"source": ') {
        $value = $values[$row]
        $json = if ($null -eq $value) { "null" } else { """$value""" }
        $output.Add("  $($Matches[1])""changedIn"": $json,")
        $row++
    }
}

if ($row -ne $rows.Count) {
    throw "inserted $row changedIn lines for $($rows.Count) rows"
}

$output.Add("  ]")
$output.Add("}")
[System.IO.File]::WriteAllText($path, ($output -join "`r`n") + "`r`n", [System.Text.UTF8Encoding]::new($false))

for ($i = 0; $i -lt $rows.Count; $i++) {
    if ($null -eq $values[$i]) { Write-Output "null: $($rows[$i].member)" }
}
