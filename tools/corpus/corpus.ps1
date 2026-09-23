#Requires -Version 5.1
<#
.SYNOPSIS
    Fetches public migration pairs into the git-ignored .corpus/ directory (ADR 0028).

.DESCRIPTION
    The corpus is two checked-in manifests beside this script:
      pairs.csv                        public before/after migrations (human- or tool-made), pinned by commit
      poly-migrationbench-dotnet.csv   verbatim copy of Amazon's Poly-MigrationBench .NET list (Apache-2.0)

    Third-party code only ever lands under <repo>/.corpus/, which .gitignore excludes. The script
    refuses to run if that directory is not ignored. Nothing is pushed anywhere.

    Layout it creates:
      .corpus/repos/<owner>__<name>@<sha12>/   shallow checkouts, one per repo and commit
      .corpus/pairs/<slug>/pair.json           resolved solution paths for one pair
      .corpus/pairs/<slug>/modern/             agent pairs only: the copy an agent migrates
      .corpus/pairs/<slug>/runs/               raw SARIF and logs written by the equiv-corpus-run skill

    The skill .claude/skills/equiv-corpus-run/SKILL.md says when to use each switch.

.EXAMPLE
    ./tools/corpus/corpus.ps1 -List
    ./tools/corpus/corpus.ps1 -Select -Count 3
    ./tools/corpus/corpus.ps1 -Fetch gitextensions-8522
    ./tools/corpus/corpus.ps1 -Fetch madelson/DistributedLock
    ./tools/corpus/corpus.ps1 -PrepareAgent madelson/DistributedLock
    ./tools/corpus/corpus.ps1 -Unchanged gitextensions-8522
    ./tools/corpus/corpus.ps1 -Refresh                     # dry run against upstream main
    ./tools/corpus/corpus.ps1 -Refresh -Apply -UpstreamRef <sha>
#>
[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(ParameterSetName = 'List')] [switch]$List,

    # Deterministic choice of agent-pair repos: the ones nearest the quantiles of num_cs_files,
    # followed by every other repo in order of distance, so a failed repo has a fixed replacement.
    [Parameter(ParameterSetName = 'Select', Mandatory)] [switch]$Select,
    [Parameter(ParameterSetName = 'Select')] [int]$Count = 3,

    # A pairs.csv slug, or a Poly-MigrationBench repo as owner/name.
    [Parameter(ParameterSetName = 'Fetch', Mandatory)] [string]$Fetch,
    [Parameter(ParameterSetName = 'PrepareAgent', Mandatory)] [string]$PrepareAgent,
    [Parameter(ParameterSetName = 'Unchanged', Mandatory)] [string]$Unchanged,
    [Parameter(ParameterSetName = 'Clean', Mandatory)] [string]$Clean,

    # Reads one equiv SARIF log and prints the numbers a SUMMARY.md needs (never source text).
    [Parameter(ParameterSetName = 'Metrics', Mandatory)] [string]$Metrics,

    # Re-download the Poly-MigrationBench .NET list and report what changed. Writes only with -Apply.
    [Parameter(ParameterSetName = 'Refresh', Mandatory)] [switch]$Refresh,
    [Parameter(ParameterSetName = 'Refresh')] [string]$UpstreamRef = 'main',
    [Parameter(ParameterSetName = 'Refresh')] [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Upstream = 'amazon-science/Poly-MigrationBench'
$PmbPath = Join-Path $PSScriptRoot 'poly-migrationbench-dotnet.csv'
$PairsPath = Join-Path $PSScriptRoot 'pairs.csv'

function Invoke-Git {
    param([string[]]$GitArgs)
    & git @GitArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed with exit code $LASTEXITCODE" }
}

$RepoRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel).Trim()
$CorpusRoot = Join-Path $RepoRoot '.corpus'

# The one guard that matters: third-party code must never be committable from here.
function Assert-CorpusIgnored {
    & git -C $RepoRoot check-ignore -q '.corpus/probe'
    if ($LASTEXITCODE -ne 0) {
        throw ".corpus/ is not git-ignored in $RepoRoot. Add '.corpus/' to .gitignore before fetching anything (ADR 0028)."
    }
}

function Get-Pairs { Import-Csv -LiteralPath $PairsPath }
function Get-Pmb { Import-Csv -LiteralPath $PmbPath }

function Get-SafeName([string]$Repo) { ($Repo -replace '/', '__').ToLowerInvariant() }

function Get-Checkout {
    param([string]$Repo, [string]$Commit)
    $dir = Join-Path (Join-Path $CorpusRoot 'repos') ('{0}@{1}' -f (Get-SafeName $Repo), $Commit.Substring(0, 12))
    if (Test-Path -LiteralPath (Join-Path $dir '.git')) {
        Write-Host "reuse   $dir"
        return $dir
    }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Write-Host "fetch   $Repo@$Commit -> $dir"
    Invoke-Git @('-C', $dir, 'init', '-q')
    Invoke-Git @('-C', $dir, 'remote', 'add', 'origin', "https://github.com/$Repo.git")
    Invoke-Git @('-C', $dir, 'fetch', '-q', '--depth', '1', 'origin', $Commit)
    Invoke-Git @('-C', $dir, 'checkout', '-q', '--detach', 'FETCH_HEAD')
    return $dir
}

function Get-PairDir([string]$Slug) { Join-Path (Join-Path $CorpusRoot 'pairs') $Slug }

function Write-PairJson {
    param([string]$Slug, [hashtable]$Data)
    $dir = Get-PairDir $Slug
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $path = Join-Path $dir 'pair.json'
    [IO.File]::WriteAllText($path, ($Data | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))
    Write-Host "wrote   $path"
}

function Read-PairJson([string]$Slug) {
    $path = Join-Path (Get-PairDir $Slug) 'pair.json'
    if (-not (Test-Path -LiteralPath $path)) { throw "No pair.json for '$Slug'. Run -Fetch first." }
    Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Resolve-Slug([string]$Id) {
    $pair = @(Get-Pairs | Where-Object { $_.slug -eq $Id })
    if ($pair.Count -eq 1) { return $Id }
    $pmb = @(Get-Pmb | Where-Object { $_.repo -eq $Id })
    if ($pmb.Count -eq 1) { return 'pmb-' + (Get-SafeName $Id) }
    throw "'$Id' is neither a pairs.csv slug nor a Poly-MigrationBench repo (owner/name)."
}

function Select-PmbRoot([string]$Roots) {
    # root_sln_or_csproj_files is ';'-separated; prefer a solution.
    $items = @($Roots -split ';' | Where-Object { $_ })
    $sln = @($items | Where-Object { $_ -like '*.sln' })
    if ($sln.Count -gt 0) { return $sln[0] }
    return $items[0]
}

switch ($PSCmdlet.ParameterSetName) {
    'List' {
        Write-Host "pairs.csv"
        Get-Pairs | Select-Object slug, kind, repo, license | Format-Table -AutoSize | Out-Host
        $pmb = @(Get-Pmb)
        Write-Host ("poly-migrationbench-dotnet.csv: {0} repos (use -Select to choose agent pairs)" -f $pmb.Count)
    }

    'Select' {
        $sorted = @(Get-Pmb | Sort-Object { [int]$_.num_cs_files }, repo)
        $n = $sorted.Count
        $targets = @(1..$Count | ForEach-Object { [int][Math]::Round(($n - 1) * $_ / ($Count + 1)) })
        $order = 0
        $sorted |
            ForEach-Object -Begin { $i = 0 } -Process {
                $index = $i
                $distance = ($targets | ForEach-Object { [Math]::Abs($index - $_) } | Measure-Object -Minimum).Minimum
                [pscustomobject]@{ Distance = $distance; Index = $index; Repo = $_.repo; CsFiles = [int]$_.num_cs_files; License = $_.license }
                $i++
            } |
            Sort-Object Distance, Index |
            ForEach-Object { $order++; [pscustomobject]@{ Order = $order; Repo = $_.Repo; CsFiles = $_.CsFiles; License = $_.License; Pick = ($order -le $Count) } } |
            Format-Table -AutoSize | Out-Host
    }

    'Fetch' {
        Assert-CorpusIgnored
        $slug = Resolve-Slug $Fetch
        $pair = @(Get-Pairs | Where-Object { $_.slug -eq $Fetch })
        if ($pair.Count -eq 1) {
            $p = $pair[0]
            $legacy = Get-Checkout -Repo $p.repo -Commit $p.legacy_commit
            $modern = Get-Checkout -Repo $p.repo -Commit $p.modern_commit
            Write-PairJson -Slug $slug -Data @{
                slug = $slug; kind = $p.kind; repo = $p.repo; license = $p.license; source = $p.source
                legacyCommit = $p.legacy_commit; modernCommit = $p.modern_commit
                legacySolution = (Join-Path $legacy $p.legacy_solution)
                modernSolution = (Join-Path $modern $p.modern_solution)
                verifyCommand = $null
            }
        }
        else {
            $row = @(Get-Pmb | Where-Object { $_.repo -eq $Fetch })[0]
            $legacy = Get-Checkout -Repo $row.repo -Commit $row.base_commit
            Write-PairJson -Slug $slug -Data @{
                slug = $slug; kind = 'agent'; repo = $row.repo; license = $row.license
                source = "https://github.com/$Upstream"
                legacyCommit = $row.base_commit; modernCommit = $null
                legacySolution = (Join-Path $legacy (Select-PmbRoot $row.root_sln_or_csproj_files))
                modernSolution = $null
                verifyCommand = $row.verify_command
            }
            Write-Host "next    -PrepareAgent $Fetch, then migrate the copy as the skill says"
        }
    }

    'PrepareAgent' {
        Assert-CorpusIgnored
        $slug = Resolve-Slug $PrepareAgent
        $pair = Read-PairJson $slug
        if ($pair.kind -ne 'agent') { throw "'$PrepareAgent' is a $($pair.kind) pair; only agent pairs get a modern copy." }
        $legacyRoot = (& git -C (Split-Path -Parent $pair.legacySolution) rev-parse --show-toplevel).Trim()
        $modernRoot = Join-Path (Get-PairDir $slug) 'modern'
        if (Test-Path -LiteralPath $modernRoot) { throw "$modernRoot exists. Use -Clean $PrepareAgent to start over." }
        Write-Host "copy    $legacyRoot -> $modernRoot"
        Copy-Item -LiteralPath $legacyRoot -Destination $modernRoot -Recurse
        $relative = $pair.legacySolution.Substring($legacyRoot.Length).TrimStart('\', '/')
        $data = @{}
        $pair.PSObject.Properties | ForEach-Object { $data[$_.Name] = $_.Value }
        $data.modernSolution = Join-Path $modernRoot $relative
        Write-PairJson -Slug $slug -Data $data
    }

    'Unchanged' {
        # ADR 0028's unchanged share, before M3-015: share of legacy .cs lines in files left byte-identical.
        # Files are matched by path relative to each side's solution directory.
        $slug = Resolve-Slug $Unchanged
        $pair = Read-PairJson $slug
        if (-not $pair.modernSolution) { throw "'$Unchanged' has no modern side yet." }
        $legacyDir = Split-Path -Parent $pair.legacySolution
        $modernDir = Split-Path -Parent $pair.modernSolution
        $total = 0; $same = 0; $files = 0; $sameFiles = 0
        Get-ChildItem -LiteralPath $legacyDir -Recurse -File -Filter '*.cs' |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]' } |
            ForEach-Object {
                $relative = $_.FullName.Substring($legacyDir.Length).TrimStart('\', '/')
                $lines = @([IO.File]::ReadAllLines($_.FullName)).Count
                $total += $lines; $files++
                $other = Join-Path $modernDir $relative
                if ((Test-Path -LiteralPath $other) -and
                    ((Get-FileHash -LiteralPath $_.FullName).Hash -eq (Get-FileHash -LiteralPath $other).Hash)) {
                    $same += $lines; $sameFiles++
                }
            }
        $share = 0
        if ($total -gt 0) { $share = [Math]::Round(100.0 * $same / $total, 1) }
        [pscustomobject]@{ Pair = $slug; LegacyCsFiles = $files; UnchangedFiles = $sameFiles; LegacyCsLines = $total; UnchangedLines = $same; UnchangedSharePercent = $share } |
            Format-List | Out-Host
    }

    'Metrics' {
        # Defensive by design: properties appear as M3 tickets land (census M3-014, proofMethod
        # M3-002, scope M3-025), so anything absent prints as n/a instead of failing.
        $log = Get-Content -LiteralPath $Metrics -Raw | ConvertFrom-Json
        $run = $log.runs[0]
        function Get-Bag($Object, [string]$Name) {
            if ($null -eq $Object) { return $null }
            $p = $Object.PSObject.Properties[$Name]
            if ($null -eq $p) { return $null }
            return $p.Value
        }
        $census = Get-Bag (Get-Bag $run 'properties') 'loweringCensus'
        Write-Host "== census"
        if ($null -eq $census) { Write-Host "n/a (no run.properties.loweringCensus; needs M3-014)" }
        else { $census | ConvertTo-Json -Depth 6 | Out-Host }
        $unverified = Get-Bag (Get-Bag $run 'properties') 'unverified'
        Write-Host ("== unverified procedures: {0}" -f @($unverified | Where-Object { $_ }).Count)
        $invocations = @(Get-Bag $run 'invocations' | Where-Object { $_ })
        $notes = @()
        if ($invocations.Count -gt 0) { $notes = @(Get-Bag $invocations[0] 'toolExecutionNotifications' | Where-Object { $_ }) }
        Write-Host ("== tool execution notifications: {0}" -f $notes.Count)
        $notes | ForEach-Object { Write-Host ("  {0}: {1}" -f $_.level, $_.message.text) }
        $results = @(Get-Bag $run 'results' | Where-Object { $_ })
        Write-Host ("== results: {0}" -f $results.Count)
        $results | Group-Object ruleId | Sort-Object Name | ForEach-Object { Write-Host ("  {0} {1}" -f $_.Name, $_.Count) }
        foreach ($prop in 'proofMethod', 'scope') {
            Write-Host "== by $prop"
            $results | ForEach-Object {
                $value = Get-Bag (Get-Bag $_ 'properties') $prop
                if ($null -eq $value) { 'n/a' } else { [string]$value }
            } | Group-Object | Sort-Object Name | ForEach-Object { Write-Host ("  {0} {1}" -f $_.Name, $_.Count) }
        }
        Write-Host "== Unknown (EQ003) by reason (first word of the message until a reason property exists)"
        $results | Where-Object { $_.ruleId -eq 'EQ003' } | ForEach-Object {
            $reason = Get-Bag (Get-Bag $_ 'properties') 'reason'
            if ($null -eq $reason) { $reason = ($_.message.text -split '[\s:(]')[0] }
            [string]$reason
        } | Group-Object | Sort-Object Count -Descending | ForEach-Object { Write-Host ("  {0} {1}" -f $_.Name, $_.Count) }
    }

    'Clean' {
        $slug = Resolve-Slug $Clean
        $dir = Get-PairDir $slug
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force; Write-Host "removed $dir" }
        Write-Host "note    shared checkouts under .corpus/repos are kept; delete them by hand if needed"
    }

    'Refresh' {
        $url = "https://raw.githubusercontent.com/$Upstream/$UpstreamRef/Poly-MigrationBench-dotnet.csv"
        $resolved = ((& git ls-remote "https://github.com/$Upstream.git" $UpstreamRef) -split '\s+')[0]
        if (-not $resolved) { $resolved = $UpstreamRef }
        Write-Host "upstream $Upstream@$UpstreamRef ($resolved)"
        $content = (Invoke-WebRequest -UseBasicParsing -Uri $url).Content
        $new = @($content -split "`r?`n" | Where-Object { $_ } | ConvertFrom-Csv)
        $old = @(Get-Pmb)
        $oldByRepo = @{}; $old | ForEach-Object { $oldByRepo[$_.repo] = $_ }
        $newByRepo = @{}; $new | ForEach-Object { $newByRepo[$_.repo] = $_ }
        $added = @($new | Where-Object { -not $oldByRepo.ContainsKey($_.repo) })
        $removed = @($old | Where-Object { -not $newByRepo.ContainsKey($_.repo) })
        $moved = @($new | Where-Object { $oldByRepo.ContainsKey($_.repo) -and $oldByRepo[$_.repo].base_commit -ne $_.base_commit })
        Write-Host ("repos {0} -> {1}; added {2}, removed {3}, base_commit changed {4}" -f $old.Count, $new.Count, $added.Count, $removed.Count, $moved.Count)
        $added | ForEach-Object { Write-Host "  + $($_.repo)" }
        $removed | ForEach-Object { Write-Host "  - $($_.repo)" }
        $moved | ForEach-Object { Write-Host "  ~ $($_.repo)" }
        if ($Apply) {
            [IO.File]::WriteAllText($PmbPath, $content, (New-Object Text.UTF8Encoding $false))
            Write-Host "wrote   $PmbPath"
            Write-Host "now     record '$resolved' as the pinned upstream commit in tools/corpus/README.md"
        }
        else {
            Write-Host "dry run: nothing written (pass -Apply)"
        }
    }
}
