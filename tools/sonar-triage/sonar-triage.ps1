#Requires -Version 5.1
<#
.SYNOPSIS
    Turns SonarQube Cloud's overall issue backlog into batched GitHub issues.

.DESCRIPTION
    Reads the SonarCloud web API, drops findings that policy.jsonc marks as accepted,
    groups what is left into coherent batches, and syncs each batch to one GitHub issue
    labelled 'sonar'. See docs/adr/0016-sonar-issue-triage.md and the README beside this file.

    Reads are anonymous: the project is public. SONAR_TOKEN is used only when present, and
    is required only by -PushResolutions.

    Nothing is written to GitHub unless -Apply is passed.
#>
[CmdletBinding()]
param(
    # Create, edit and close GitHub issues. Without this the script only reports.
    [switch]$Apply,

    # Also transition policy-accepted findings to Won't Fix in SonarCloud. Prompts first.
    [switch]$PushResolutions,

    [string]$ProjectKey = 'Zafnok_code-equivalency',
    [string]$Organization = 'zafnok',
    [string]$HostUrl = 'https://sonarcloud.io',
    # Resolved below: $PSScriptRoot is not bound yet while 5.1 binds parameters.
    [string]$PolicyPath,

    # A rule found in at least this many distinct files becomes one rule-wide batch.
    [int]$RuleBatchMinFiles = 3,

    # A file left with fewer than this many findings rolls up into its area's long-tail batch.
    [int]$FileBatchMin = 2,

    [string]$Label = 'sonar'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Runs under pwsh 7 in CI and Windows PowerShell 5.1 on the dev box, so nothing here may
# use 6+ only syntax. 5.1 still negotiates TLS 1.0 by default, which sonarcloud.io refuses.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if (-not $PolicyPath) { $PolicyPath = Join-Path $PSScriptRoot 'policy.jsonc' }

$MarkerPrefix = 'sonar-triage:v1 key='

# ---------------------------------------------------------------- small helpers

# StrictMode makes a missing property a terminating error, and the Sonar API omits properties
# rather than nulling them (line is absent on file-level issues, for example).
function Get-Prop {
    param($Object, [string]$Name, $Default = $null)
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}

# JSON with comments. A naive regex would corrupt any reason containing '//', and policy
# reasons cite URLs, so string literals are matched as tokens of their own and kept whole.
# An unterminated string or block comment runs to the end of the text.
function Remove-JsonComment {
    param([string]$Text)
    $tokens = '"(?:[^"\\]|\\[\s\S])*(?:"|\z)|//[^\n]*|/\*[\s\S]*?(?:\*/|\z)'
    return [regex]::Replace($Text, $tokens, [System.Text.RegularExpressions.MatchEvaluator]{
            param($Match)
            if ($Match.Value.StartsWith('"')) { return $Match.Value }
            return ''
        })
}

# Glob semantics: '**' crosses directory separators, '*' and '?' do not. A '**/' prefix
# also matches zero directories. Tokens are matched longest first, so '***' is '**' then '*'.
function ConvertTo-GlobRegex {
    param([string]$Glob)
    $regexFor = @{ '**/' = '(?:.*/)?'; '**' = '.*'; '*' = '[^/]*'; '?' = '[^/]' }
    $body = [regex]::Replace($Glob, '\*\*/|\*\*|[*?]|[^*?]+', [System.Text.RegularExpressions.MatchEvaluator]{
            param($Match)
            if ($regexFor.ContainsKey($Match.Value)) { return $regexFor[$Match.Value] }
            return [regex]::Escape($Match.Value)
        })
    return [regex]::new("^$body`$", 'IgnoreCase')
}

function Invoke-SonarApi {
    param([string]$Path, [hashtable]$Query = @{}, [string]$Method = 'Get')
    $pairs = foreach ($k in $Query.Keys) { "$k=$([uri]::EscapeDataString([string]$Query[$k]))" }
    $uri = "$HostUrl$Path"
    if ($pairs) { $uri += '?' + ($pairs -join '&') }
    $headers = @{}
    if ($env:SONAR_TOKEN) {
        $basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($env:SONAR_TOKEN):"))
        $headers['Authorization'] = "Basic $basic"
    }
    # -MaximumRetryCount is pwsh-only, so the retry is explicit.
    for ($attempt = 1; ; $attempt++) {
        try { return Invoke-RestMethod -Uri $uri -Headers $headers -Method $Method -UseBasicParsing }
        catch {
            if ($attempt -ge 3) { throw }
            Write-Warning "$Path failed (attempt $attempt): $($_.Exception.Message); retrying"
            Start-Sleep -Seconds (3 * $attempt)
        }
    }
}

function Invoke-Gh {
    param([string[]]$Arguments)
    $output = & gh @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "gh $($Arguments -join ' ') failed: $output" }
    return $output
}

# ---------------------------------------------------------------- fetch

function Get-SonarIssue {
    $all = [System.Collections.Generic.List[object]]::new()
    $page = 1
    while ($true) {
        $response = Invoke-SonarApi -Path '/api/issues/search' -Query @{
            componentKeys = $ProjectKey; resolved = 'false'; ps = 500; p = $page
        }
        foreach ($issue in $response.issues) { $all.Add($issue) }
        $total = [int](Get-Prop $response 'total' 0)
        # The API refuses p*ps beyond 10000; stop there rather than erroring out.
        if ($all.Count -ge $total -or @($response.issues).Count -eq 0 -or $page * 500 -ge 10000) { break }
        $page++
    }
    return $all
}

function Get-SonarHotspot {
    $response = Invoke-SonarApi -Path '/api/hotspots/search' -Query @{
        projectKey = $ProjectKey; status = 'TO_REVIEW'; ps = 500
    }
    return @(Get-Prop $response 'hotspots' @())
}

$script:RuleCache = @{}
function Get-RuleMetadata {
    param([string]$RuleKey)
    if ($script:RuleCache.ContainsKey($RuleKey)) { return $script:RuleCache[$RuleKey] }
    $meta = [pscustomobject]@{ Key = $RuleKey; Name = $RuleKey; Attribute = $null }
    try {
        $response = Invoke-SonarApi -Path '/api/rules/show' -Query @{ key = $RuleKey; organization = $Organization }
        $rule = Get-Prop $response 'rule'
        $meta = [pscustomobject]@{
            Key       = $RuleKey
            Name      = [string](Get-Prop $rule 'name' $RuleKey)
            Attribute = Get-Prop $rule 'cleanCodeAttribute'
        }
    }
    catch {
        Write-Warning "rule metadata unavailable for $RuleKey ($($_.Exception.Message)); using the key as the name"
    }
    $script:RuleCache[$RuleKey] = $meta
    return $meta
}

# ---------------------------------------------------------------- normalise and policy

function ConvertTo-Finding {
    param($Issue, [switch]$IsHotspot)
    $component = [string](Get-Prop $Issue 'component' '')
    $path = if ($component.StartsWith("${ProjectKey}:")) { $component.Substring($ProjectKey.Length + 1) } else { '' }
    $rule = if ($IsHotspot) { 'securityhotspot:' + [string](Get-Prop $Issue 'securityCategory' 'review') }
    else { [string](Get-Prop $Issue 'rule' 'unknown') }

    $severity = 'MEDIUM'
    $impacts = Get-Prop $Issue 'impacts'
    if ($impacts -and @($impacts).Count -gt 0) { $severity = [string](Get-Prop @($impacts)[0] 'severity' 'MEDIUM') }
    elseif (Get-Prop $Issue 'vulnerabilityProbability') { $severity = [string](Get-Prop $Issue 'vulnerabilityProbability') }

    $key = [string](Get-Prop $Issue 'key' '')
    $query = if ($IsHotspot) { "id=$ProjectKey&hotspots=$key" } else { "id=$ProjectKey&open=$key&resolved=false" }
    return [pscustomobject]@{
        Key       = $key
        Rule      = $rule
        Path      = $path
        Line      = [int](Get-Prop $Issue 'line' 0)
        Message   = ([string](Get-Prop $Issue 'message' '')) -replace '\s+', ' '
        Severity  = $severity
        Area      = if ($path) { ($path -split '/')[0] } else { 'project' }
        Permalink = "$HostUrl/project/issues?$query"
    }
}

function Import-Policy {
    if (-not (Test-Path -LiteralPath $PolicyPath)) {
        Write-Warning "no policy file at $PolicyPath; every finding will be batched"
        return @()
    }
    $parsed = (Remove-JsonComment (Get-Content -LiteralPath $PolicyPath -Raw)) | ConvertFrom-Json
    $entries = @(Get-Prop $parsed 'rules' @())
    $index = 0
    foreach ($entry in $entries) {
        $index++
        $rule = Get-Prop $entry 'rule'
        if (-not $rule) { throw "policy.jsonc entry #$index has no 'rule'" }
        $verdict = [string](Get-Prop $entry 'verdict' 'fix')
        if ($verdict -notin @('fix', 'accept', 'defer')) {
            throw "policy.jsonc entry for $rule has verdict '$verdict'; expected fix, accept or defer"
        }
        # A suppression without a stated reason is how a backlog quietly rots.
        if ($verdict -ne 'fix' -and -not ([string](Get-Prop $entry 'reason' '')).Trim()) {
            throw "policy.jsonc entry for $rule has verdict '$verdict' but no reason"
        }
        $entry | Add-Member -NotePropertyName Verdict -NotePropertyValue $verdict -Force
        $entry | Add-Member -NotePropertyName PathRegexes -NotePropertyValue @(
            foreach ($glob in @(Get-Prop $entry 'paths' @())) { ConvertTo-GlobRegex $glob }
        ) -Force
    }
    return $entries
}

# An entry applies when the rule matches, any of its path globs matches (or it has none),
# and its message regex matches (or it has none).
function Test-PolicyEntry {
    param($Entry, $Finding)
    if ($Entry.rule -ne $Finding.Rule) { return $false }
    $regexes = @($Entry.PathRegexes)
    if ($regexes.Count -gt 0 -and -not @($regexes | Where-Object { $_.IsMatch($Finding.Path) })) { return $false }
    $messagePattern = [string](Get-Prop $Entry 'message' '')
    return (-not $messagePattern) -or ($Finding.Message -match $messagePattern)
}

function Get-PolicyVerdict {
    param($Finding, $Policy)
    foreach ($entry in $Policy) {
        if (Test-PolicyEntry $entry $Finding) { return $entry }
    }
    return $null
}

# ---------------------------------------------------------------- batching

function Format-Count {
    param([int]$Count, [string]$Noun)
    return "$Count $Noun" + $(if ($Count -eq 1) { '' } else { 's' })
}

function Group-Finding {
    param([object[]]$Findings)
    $batches = [System.Collections.Generic.List[object]]::new()

    # A rule spread across several files is one decision, so it becomes one batch and one PR.
    foreach ($group in ($Findings | Group-Object Rule)) {
        $fileCount = @($group.Group | Select-Object -ExpandProperty Path -Unique).Count
        if ($fileCount -ge $RuleBatchMinFiles) {
            $meta = Get-RuleMetadata $group.Name
            $shortRule = Split-Path -Leaf ($group.Name -replace ':', '/')
            $batches.Add([pscustomobject]@{
                    Key      = "rule:$($group.Name)"
                    Kind     = 'rule'
                    Rule     = $group.Name
                    Title    = "sonar($shortRule): $($meta.Name)"
                    Findings = @($group.Group)
                })
        }
    }

    $claimed = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@($batches | ForEach-Object { $_.Findings } | ForEach-Object { $_.Key }))
    $leftover = @($Findings | Where-Object { -not $claimed.Contains($_.Key) })

    # Everything else groups by file, except files holding a single finding, which would
    # otherwise produce a queue of near-empty issues; those roll up per top-level area.
    $tail = [System.Collections.Generic.List[object]]::new()
    foreach ($group in ($leftover | Group-Object Path)) {
        if ($group.Count -ge $FileBatchMin) {
            $batches.Add([pscustomobject]@{
                    Key      = "file:$($group.Name)"
                    Kind     = 'file'
                    Rule     = $null
                    Title    = "sonar($($group.Name)): $(Format-Count $group.Count 'finding')"
                    Findings = @($group.Group)
                })
        }
        else { foreach ($finding in $group.Group) { $tail.Add($finding) } }
    }
    foreach ($group in ($tail | Group-Object Area)) {
        $batches.Add([pscustomobject]@{
                Key      = "tail:$($group.Name)"
                Kind     = 'tail'
                Rule     = $null
                Title    = "sonar($($group.Name)): long tail, $(Format-Count $group.Count 'single finding')"
                Findings = @($group.Group)
            })
    }
    return $batches
}

# ---------------------------------------------------------------- rendering

$SeverityRank = @{ BLOCKER = 0; HIGH = 1; MEDIUM = 2; LOW = 3; INFO = 4 }

function Get-BatchSeverity {
    param($Batch)
    $ranked = @($Batch.Findings | Sort-Object {
            if ($SeverityRank.ContainsKey($_.Severity)) { $SeverityRank[$_.Severity] } else { 2 }
        })
    return $ranked[0].Severity
}

function Format-IssueBody {
    param($Batch)
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("<!-- $MarkerPrefix$($Batch.Key) -->")
    $lines.Add('')
    $lines.Add('_Filed by `tools/sonar-triage/sonar-triage.ps1`. This issue is the ticket: see `docs/adr/0016-sonar-issue-triage.md`._')
    $lines.Add('')

    $fileCount = @($Batch.Findings | Select-Object -ExpandProperty Path -Unique).Count
    $lines.Add('## Goal')
    $lines.Add('')
    if ($Batch.Kind -eq 'rule') {
        $meta = Get-RuleMetadata $Batch.Rule
        $lines.Add("Clear every ``$($Batch.Rule)`` finding in the project: _$($meta.Name)_.")
        $lines.Add("$($Batch.Findings.Count) findings across $fileCount files. One rule, one decision, one PR.")
        $lines.Add('')
        $encoded = [uri]::EscapeDataString($Batch.Rule)
        $lines.Add("- In SonarCloud: $HostUrl/project/issues?id=$ProjectKey&rules=$encoded&resolved=false")
        if ($Batch.Rule -like 'csharpsquid:S*') {
            $rspec = $Batch.Rule -replace '^csharpsquid:S', ''
            $lines.Add("- Rule description: https://rules.sonarsource.com/csharp/RSPEC-$rspec/")
        }
    }
    elseif ($Batch.Kind -eq 'file') {
        $lines.Add("Clear all $($Batch.Findings.Count) SonarCloud findings in ``$(@($Batch.Findings)[0].Path)``.")
    }
    else {
        $area = @($Batch.Findings)[0].Area
        $lines.Add("Clear $($Batch.Findings.Count) one-off SonarCloud findings under ``$area/``, each the only")
        $lines.Add("finding in its file. Grouped so they are one PR rather than $($Batch.Findings.Count).")
    }
    $lines.Add('')

    $lines.Add('## Findings')
    $lines.Add('')
    $lines.Add('| Location | Rule | Message | Sonar |')
    $lines.Add('|---|---|---|---|')
    foreach ($finding in ($Batch.Findings | Sort-Object Path, Line)) {
        $location = if ($finding.Line -gt 0) { "``$($finding.Path):$($finding.Line)``" } else { "``$($finding.Path)``" }
        $message = $finding.Message -replace '\|', '\|'
        $lines.Add("| $location | ``$($finding.Rule)`` | $message | [open]($($finding.Permalink)) |")
    }
    $lines.Add('')

    $lines.Add('## Acceptance criteria')
    $lines.Add('')
    $lines.Add('1. Every finding above is either fixed, or added to `tools/sonar-triage/policy.jsonc` with')
    $lines.Add('   `verdict: accept` and a reason citing an ADR or a ticket. A finding that no longer')
    $lines.Add('   reproduces on `HEAD` is called out in the PR body, not silently dropped.')
    $externalRules = @($Batch.Findings |
            Where-Object { $_.Rule -like 'external_roslyn:*' } |
            Select-Object -ExpandProperty Rule -Unique |
            ForEach-Object { $_ -replace '^external_roslyn:', '' })
    $n = 2
    if ($externalRules.Count -gt 0) {
        $pins = ($externalRules | ForEach-Object { "``dotnet_diagnostic.$_.severity = error``" }) -join ', '
        $lines.Add("$n. ``.editorconfig`` pins $pins, so the build now fails on what was")
        $lines.Add('   previously a silent suggestion and the finding cannot come back.')
        $n++
    }
    $lines.Add("$n. ``./build.ps1 -Integration`` is green."); $n++
    $lines.Add("$n. No ``#pragma warning disable``, no coverage exclusion, no gate lowered.")
    $lines.Add('')
    $lines.Add('## How to fix')
    $lines.Add('')
    $lines.Add('Point a session at this issue and say `use equiv-sonar-fix`.')
    return ($lines -join "`n")
}

# ---------------------------------------------------------------- GitHub sync

function Write-IssueBodyFile {
    param([string]$Body)
    $file = [System.IO.Path]::GetTempFileName()
    # -Encoding utf8NoBOM is pwsh-only; a BOM would show up in the issue body.
    [System.IO.File]::WriteAllText($file, $Body, [System.Text.UTF8Encoding]::new($false))
    return $file
}

# Open 'sonar' issues keyed by the batch key in their marker comment. Issues without a
# marker were filed by hand and are left alone.
function Get-ExistingIssue {
    $existing = @{}
    $listed = Invoke-Gh @('issue', 'list', '--label', $Label, '--state', 'open',
        '--json', 'number,title,body', '--limit', '300') | ConvertFrom-Json
    foreach ($issue in @($listed)) {
        $body = [string](Get-Prop $issue 'body' '')
        $match = [regex]::Match($body, [regex]::Escape($MarkerPrefix) + '([^\s>]+)')
        if ($match.Success) { $existing[$match.Groups[1].Value] = $issue }
    }
    return $existing
}

# Creates or edits the one issue for a batch. Returns 'Created', 'Edited' or 'Unchanged'.
function Sync-BatchIssue {
    param($Batch, $Current)
    $body = Format-IssueBody $Batch
    if ($null -eq $Current) {
        Write-Host "  create  $($Batch.Title)" -ForegroundColor Green
        $severityLabel = 'sonar:' + (Get-BatchSeverity $Batch).ToLowerInvariant()
        if ($Apply) {
            Invoke-GhWithBodyFile $body @('issue', 'create', '--title', $Batch.Title,
                '--label', $Label, '--label', 'tech-debt', '--label', $severityLabel)
        }
        return 'Created'
    }
    if ((([string](Get-Prop $Current 'body' '')) -replace "`r`n", "`n").TrimEnd() -eq $body.TrimEnd()) { return 'Unchanged' }
    Write-Host "  edit    #$($Current.number)  $($Batch.Title)" -ForegroundColor Yellow
    if ($Apply) { Invoke-GhWithBodyFile $body @('issue', 'edit', [string]$Current.number) }
    return 'Edited'
}

function Invoke-GhWithBodyFile {
    param([string]$Body, [string[]]$Arguments)
    $file = Write-IssueBodyFile $Body
    try { Invoke-Gh ($Arguments + @('--body-file', $file)) | Out-Null }
    finally { Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue }
}

# Closes every marked issue whose batch no longer exists. Returns how many.
function Close-StaleIssue {
    param([hashtable]$Existing, [object[]]$Batches)
    $liveKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]@($Batches | ForEach-Object { $_.Key }))
    $closed = 0
    foreach ($key in @($Existing.Keys | Where-Object { -not $liveKeys.Contains($_) })) {
        $issue = $Existing[$key]
        Write-Host "  close   #$($issue.number)  $($issue.title)" -ForegroundColor Cyan
        if ($Apply) {
            Invoke-Gh @('issue', 'close', [string]$issue.number, '--comment',
                'SonarCloud no longer reports any finding in this batch. Closed by tools/sonar-triage.') | Out-Null
        }
        $closed++
    }
    return $closed
}

function Sync-GitHubIssue {
    param([object[]]$Batches)
    $existing = Get-ExistingIssue
    $counts = @{ Created = 0; Edited = 0; Unchanged = 0 }
    foreach ($batch in $Batches) { $counts[(Sync-BatchIssue $batch $existing[$batch.Key])]++ }
    $closed = Close-StaleIssue $existing $Batches
    return [pscustomobject]@{
        Created = $counts.Created; Edited = $counts.Edited; Closed = $closed; Unchanged = $counts.Unchanged
    }
}

function Initialize-Label {
    $definitions = @(
        @{ Name = $Label; Color = '4c9aff'; Description = 'Filed from the SonarQube Cloud backlog' },
        @{ Name = 'tech-debt'; Color = 'd4c5f9'; Description = 'Quality debt, not a feature or a bug' },
        @{ Name = 'sonar:blocker'; Color = 'b60205'; Description = 'Sonar impact severity: blocker' },
        @{ Name = 'sonar:high'; Color = 'd93f0b'; Description = 'Sonar impact severity: high' },
        @{ Name = 'sonar:medium'; Color = 'fbca04'; Description = 'Sonar impact severity: medium' },
        @{ Name = 'sonar:low'; Color = '0e8a16'; Description = 'Sonar impact severity: low' },
        @{ Name = 'sonar:info'; Color = 'c5def5'; Description = 'Sonar impact severity: info' }
    )
    foreach ($definition in $definitions) {
        Invoke-Gh @('label', 'create', $definition.Name, '--color', $definition.Color,
            '--description', $definition.Description, '--force') | Out-Null
    }
}

function Push-Resolution {
    param([object[]]$Suppressed)
    if ($Suppressed.Count -eq 0) { Write-Host 'Nothing to push: no findings were suppressed by policy.'; return }
    if (-not $env:SONAR_TOKEN) { throw '-PushResolutions needs SONAR_TOKEN with issue-admin rights on the project.' }
    Write-Host ''
    Write-Host "About to mark $($Suppressed.Count) findings Won't Fix in SonarCloud. That changes shared" -ForegroundColor Yellow
    Write-Host 'state everyone with project access sees, and re-running does not undo it.' -ForegroundColor Yellow
    if ((Read-Host 'Type the word apply to continue') -ne 'apply') { Write-Host 'Skipped.'; return }
    foreach ($item in $Suppressed) {
        Invoke-SonarApi -Path '/api/issues/add_comment' -Method Post -Query @{
            issue = $item.Finding.Key
            text  = "Accepted by tools/sonar-triage/policy.jsonc: $($item.Reason)"
        } | Out-Null
        Invoke-SonarApi -Path '/api/issues/do_transition' -Method Post -Query @{
            issue = $item.Finding.Key; transition = 'wontfix'
        } | Out-Null
        Write-Host "  wontfix  $($item.Finding.Rule)  $($item.Finding.Path)"
    }
}

# ---------------------------------------------------------------- main

function Invoke-Triage {
    Write-Host "Reading $HostUrl for $ProjectKey ..." -ForegroundColor Cyan
    $issues = @(Get-SonarIssue | ForEach-Object { ConvertTo-Finding $_ })
    $hotspots = @(Get-SonarHotspot | ForEach-Object { ConvertTo-Finding $_ -IsHotspot })
    $all = @($issues) + @($hotspots)

    $policy = Import-Policy
    $kept = [System.Collections.Generic.List[object]]::new()
    $suppressed = [System.Collections.Generic.List[object]]::new()
    foreach ($finding in $all) {
        $entry = Get-PolicyVerdict $finding $policy
        if ($null -ne $entry -and $entry.Verdict -ne 'fix') {
            $suppressed.Add([pscustomobject]@{
                    Finding = $finding; Verdict = $entry.Verdict; Reason = [string](Get-Prop $entry 'reason' '')
                })
        }
        else { $kept.Add($finding) }
    }

    $batches = @(Group-Finding -Findings $kept.ToArray() | Sort-Object { -$_.Findings.Count })

    Write-Host ''
    Write-Host "fetched $($all.Count) / suppressed $($suppressed.Count) / batches $($batches.Count)"
    if ($hotspots.Count -gt 0) {
        Write-Host "  (includes $($hotspots.Count) security hotspots awaiting review)" -ForegroundColor Yellow
    }
    Write-Host ''
    foreach ($batch in $batches) {
        $fileCount = @($batch.Findings | Select-Object -ExpandProperty Path -Unique).Count
        Write-Host ('{0,5} in {1,3}   {2}' -f (Format-Count $batch.Findings.Count 'finding'), (Format-Count $fileCount 'file'), $batch.Key)
    }
    if ($suppressed.Count -gt 0) {
        Write-Host ''
        Write-Host 'Suppressed by policy:' -ForegroundColor DarkGray
        foreach ($group in ($suppressed | Group-Object { $_.Finding.Rule })) {
            Write-Host ('{0,5}  {1}  ({2})' -f $group.Count, $group.Name, @($group.Group)[0].Verdict) -ForegroundColor DarkGray
        }
    }

    Write-Host ''
    if ($Apply) {
        Write-Host 'Syncing GitHub issues.' -ForegroundColor Cyan
        Initialize-Label
    }
    else {
        Write-Host 'Dry run. Nothing is written; pass -Apply to sync GitHub issues.' -ForegroundColor DarkGray
    }
    $result = Sync-GitHubIssue -Batches $batches
    Write-Host ''
    Write-Host "created $($result.Created) / edited $($result.Edited) / closed $($result.Closed) / unchanged $($result.Unchanged)"

    if ($PushResolutions) { Push-Resolution -Suppressed $suppressed.ToArray() }
}

Invoke-Triage
