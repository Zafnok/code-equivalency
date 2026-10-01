<#
.SYNOPSIS
Runs the Pester tests under tools/ with code coverage and writes the result in Sonar's generic
coverage format, the only one Sonar imports for PowerShell (ticket P2-063; sonar.yml passes the
file as sonar.coverageReportPaths). Fails if a test fails.

A line counts as covered when any command on it ran. The test files are measured too: Sonar
indexes them as source, so a line it has no report for counts as uncovered.

Works with the Pester 5 on the GitHub runners and with Windows PowerShell's bundled Pester 3.4.
#>
[CmdletBinding()]
param(
    [string]$Out = 'TestResults/pester-coverage.xml'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$tests = @(Get-ChildItem -Path (Join-Path $repoRoot 'tools') -Recurse -Filter '*.Tests.ps1' | ForEach-Object { $_.FullName })
$measured = @(Join-Path $repoRoot 'tools/corpus/corpus.ps1') + $tests

$outPath = Join-Path $repoRoot $Out
New-Item -ItemType Directory -Force -Path (Split-Path $outPath -Parent) | Out-Null

Import-Module Pester
if ((Get-Module Pester).Version.Major -ge 5) {
    $config = New-PesterConfiguration
    $config.Run.Path = $tests
    $config.Run.PassThru = $true
    $config.Output.Verbosity = 'Detailed'
    $config.CodeCoverage.Enabled = $true
    $config.CodeCoverage.Path = $measured
    $config.CodeCoverage.OutputPath = [IO.Path]::ChangeExtension($outPath, '.jacoco.xml')
    $result = Invoke-Pester -Configuration $config
    $hit = @($result.CodeCoverage.CommandsExecuted)
    $missed = @($result.CodeCoverage.CommandsMissed)
}
else {
    $result = Invoke-Pester -Script $tests -CodeCoverage $measured -PassThru
    $hit = @($result.CodeCoverage.HitCommands)
    $missed = @($result.CodeCoverage.MissedCommands)
}

if ($result.FailedCount -gt 0) { throw "$($result.FailedCount) Pester test(s) failed" }

# file -> line -> covered
$files = @{}
foreach ($entry in @($missed | ForEach-Object { @{ Command = $_; Covered = $false } }) + @($hit | ForEach-Object { @{ Command = $_; Covered = $true } })) {
    if ($null -eq $entry.Command) { continue }
    $file = (Resolve-Path -LiteralPath $entry.Command.File).Path
    if (-not $files.ContainsKey($file)) { $files[$file] = @{} }
    $files[$file][[int]$entry.Command.Line] = $entry.Covered
}

$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$writer = [System.Xml.XmlWriter]::Create($outPath, $settings)
try {
    $writer.WriteStartElement('coverage')
    $writer.WriteAttributeString('version', '1')
    foreach ($file in $files.Keys | Sort-Object) {
        $writer.WriteStartElement('file')
        $writer.WriteAttributeString('path', $file.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/'))
        foreach ($line in $files[$file].Keys | Sort-Object) {
            $writer.WriteStartElement('lineToCover')
            $writer.WriteAttributeString('lineNumber', [string]$line)
            $writer.WriteAttributeString('covered', ([string]$files[$file][$line]).ToLowerInvariant())
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
}
finally {
    $writer.Dispose()
}

foreach ($file in $files.Keys | Sort-Object) {
    $lines = $files[$file]
    $coveredCount = @($lines.Values | Where-Object { $_ }).Count
    Write-Host ("{0}: {1} of {2} lines covered" -f $file.Substring($repoRoot.Length).TrimStart('\', '/'), $coveredCount, $lines.Count)
}
Write-Host "wrote $outPath"
