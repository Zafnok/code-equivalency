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
      .corpus/Directory.Build.props, .targets, Directory.Packages.props, .editorconfig
                                                sentinels so this repo's own MSBuild/NuGet/format
                                                files stop at .corpus/ instead of leaking in (-Prepare)
      .corpus/refasm/.NETFramework/v<X>/       reference assemblies net20 through net481 (-Prepare)
      .corpus/repos/<owner>__<name>@<sha12>/   shallow checkouts, one per repo and commit
      .corpus/pairs/<slug>/pair.json           resolved solution paths for one pair
      .corpus/pairs/<slug>/modern/             agent pairs only: the copy an agent migrates
      .corpus/pairs/<slug>/runs/               raw SARIF and logs written by the equiv-corpus-run skill

    The skill .claude/skills/equiv-corpus-run/SKILL.md says when to use each switch.

.EXAMPLE
    ./tools/corpus/corpus.ps1 -Prepare                     # once per box
    ./tools/corpus/corpus.ps1 -List
    ./tools/corpus/corpus.ps1 -Select -Count 3
    ./tools/corpus/corpus.ps1 -Fetch gitextensions-8522
    ./tools/corpus/corpus.ps1 -Fetch madelson/DistributedLock
    ./tools/corpus/corpus.ps1 -PrepareAgent madelson/DistributedLock
    ./tools/corpus/corpus.ps1 -Unchanged gitextensions-8522
    ./tools/corpus/corpus.ps1 -Env | Invoke-Expression     # before any restore or run of equiv
    ./tools/corpus/corpus.ps1 -Refresh                     # dry run against upstream main
    ./tools/corpus/corpus.ps1 -Refresh -Apply -UpstreamRef <sha>
#>
[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(ParameterSetName = 'List')] [switch]$List,

    # Once per box: sentinel MSBuild/NuGet/format files and reference assemblies under .corpus/.
    [Parameter(ParameterSetName = 'Prepare', Mandatory)] [switch]$Prepare,

    # Deterministic choice of agent-pair repos: the ones nearest the quantiles of num_cs_files,
    # followed by every other repo in order of distance, so a failed repo has a fixed replacement.
    [Parameter(ParameterSetName = 'Select', Mandatory)] [switch]$Select,
    [Parameter(ParameterSetName = 'Select')] [int]$Count = 3,

    # A pairs.csv slug, or a Poly-MigrationBench repo as owner/name.
    [Parameter(ParameterSetName = 'Fetch', Mandatory)] [string]$Fetch,
    [Parameter(ParameterSetName = 'PrepareAgent', Mandatory)] [string]$PrepareAgent,
    [Parameter(ParameterSetName = 'Unchanged', Mandatory)] [string]$Unchanged,
    [Parameter(ParameterSetName = 'Clean', Mandatory)] [string]$Clean,

    # Prints the environment block (SDK resolver, reference assemblies, restore warnings) that a
    # restore or an `equiv` run against .corpus/ needs, for `... | Invoke-Expression`.
    [Parameter(ParameterSetName = 'Env', Mandatory)] [switch]$Env,

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

# Status lines are for the operator, not the pipeline: Get-Checkout returns a path, so they must
# stay off the output stream. One Show- helper keeps that intent explicit (Sonar S8677).
function Show-Step([string]$Message) { Write-Host $Message }

function Invoke-Git {
    param([string[]]$GitArgs)
    # core.longpaths=true on every call (not just the initial clone): a worktree path plus a
    # corpus repo's own deep paths routinely exceeds MAX_PATH, and git reports that as "Filename
    # too long" rather than a path-length error. -c propagates to the child git processes that
    # `submodule update` spawns, so submodules get it too.
    #
    # git writes routine progress (clone/fetch/submodule status lines) to stderr. Under this
    # script's $ErrorActionPreference = 'Stop', Windows PowerShell 5.1 can promote those lines to a
    # terminating NativeCommandError even though git's own exit code is 0. Route stderr through the
    # success stream as plain text instead of trusting $ErrorActionPreference to leave it alone; the
    # real success/failure signal is $LASTEXITCODE, checked below regardless.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & git -c core.longpaths=true @GitArgs 2>&1 | ForEach-Object { "$_" } | Out-Host
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed with exit code $LASTEXITCODE" }
}

# Distinguishes a checkout Get-Checkout can safely reuse from one a previous run left half-done
# (e.g. after "Filename too long" aborted a fetch partway through): only a resolvable HEAD and a
# clean status count as reusable.
function Test-CleanCheckout([string]$Dir) {
    if (-not (Test-Path -LiteralPath (Join-Path $Dir '.git'))) { return $false }
    & git -c core.longpaths=true -C $Dir rev-parse --verify -q HEAD *> $null
    if ($LASTEXITCODE -ne 0) { return $false }
    $dirty = & git -c core.longpaths=true -C $Dir status --porcelain
    return ($LASTEXITCODE -eq 0) -and (-not $dirty)
}

# Git Extensions' modern global.json pins an SDK with no roll-forward, so the box's actual SDK
# is never resolved. Patches only a checkout's own copy, never this repo's global.json, and marks
# it skip-worktree so the patch (a tracked file diverging from the index) does not itself make
# "-Fetch leaves both checkouts clean" false: acceptance criterion 1 checks git status --porcelain.
function Set-RollForwardLatestMajor([string]$Dir) {
    $path = Join-Path $Dir 'global.json'
    if (-not (Test-Path -LiteralPath $path)) { return }
    $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if (-not $json.PSObject.Properties['sdk']) { return }
    if ($json.sdk.PSObject.Properties['rollForward']) { return }
    Show-Step "patch   $path (sdk.rollForward = latestMajor)"
    $json.sdk | Add-Member -NotePropertyName 'rollForward' -NotePropertyValue 'latestMajor'
    [IO.File]::WriteAllText($path, ($json | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
    Invoke-Git @('-C', $Dir, 'update-index', '--skip-worktree', 'global.json')
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
        if (-not (Test-CleanCheckout $dir)) {
            throw "$dir exists but its HEAD does not resolve, or 'git status --porcelain' is not empty. Refusing to reuse it: delete the directory and run -Fetch again."
        }
        Show-Step "reuse   $dir"
        return $dir
    }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Show-Step "fetch   $Repo@$Commit -> $dir"
    Invoke-Git @('-C', $dir, 'init', '-q')
    Invoke-Git @('-C', $dir, 'remote', 'add', 'origin', "https://github.com/$Repo.git")
    Invoke-Git @('-C', $dir, 'fetch', '-q', '--depth', '1', 'origin', $Commit)
    Invoke-Git @('-C', $dir, 'checkout', '-q', '--detach', 'FETCH_HEAD')
    Invoke-Git @('-C', $dir, 'submodule', 'update', '--init', '--depth', '1')
    Set-RollForwardLatestMajor $dir
    return $dir
}

# net20 through net481, version 1.0.3: the range VS 2026 Build Tools' installer will not provide
# (it rejects Microsoft.Net.Component.4.6.1.TargetingPack, exit 87) or does not ship at all below
# 4.7.2. Ordered oldest to newest only for readable -Prepare output.
$RefAsmVersion = '1.0.3'
$RefAsmTfms = [ordered]@{
    net20 = 'v2.0'; net35 = 'v3.5'; net40 = 'v4.0'; net45 = 'v4.5'; net451 = 'v4.5.1'
    net452 = 'v4.5.2'; net46 = 'v4.6'; net461 = 'v4.6.1'; net462 = 'v4.6.2'; net47 = 'v4.7'
    net471 = 'v4.7.1'; net472 = 'v4.7.2'; net48 = 'v4.8'; net481 = 'v4.8.1'
}

function Get-RefAsmRoot { Join-Path $CorpusRoot 'refasm' }

# .corpus/ sits inside this repository's own directory tree, so MSBuild, NuGet and dotnet format
# all walk up from a corpus project and find this repo's Directory.Build.props (repo-wide settings
# that don't apply to third-party code), Directory.Packages.props (central package management,
# which fails a corpus project's own <PackageReference Version=...> with NU1008) and .editorconfig.
# A sentinel file at .corpus/ stops each search there. A sentinel global.json was tried too, to stop
# this repo's Microsoft.Testing.Platform test-runner setting from reaching the corpus, but removed
# again while diagnosing the SDK-resolver problem -Env now covers; it is deliberately not written.
function Write-CorpusSentinels {
    New-Item -ItemType Directory -Force -Path $CorpusRoot | Out-Null
    $files = [ordered]@{
        'Directory.Build.props'    = "<Project />`n"
        'Directory.Build.targets'  = "<Project />`n"
        'Directory.Packages.props' = "<Project>`n  <PropertyGroup>`n    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>`n  </PropertyGroup>`n</Project>`n"
        '.editorconfig'            = "root = true`n"
    }
    foreach ($name in $files.Keys) {
        $path = Join-Path $CorpusRoot $name
        [IO.File]::WriteAllText($path, $files[$name], (New-Object Text.UTF8Encoding $false))
    }
    Show-Step ("wrote   sentinel {0} in {1}" -f ($files.Keys -join ', '), $CorpusRoot)
}

function Test-ReferenceAssembliesReady([string]$RefAsmRoot) {
    foreach ($version in $RefAsmTfms.Values) {
        if (-not (Test-Path -LiteralPath (Join-Path $RefAsmRoot ".NETFramework\$version"))) { return $false }
    }
    return $true
}

# VS 2026 Build Tools ships targeting packs only for 4.7.2 and 4.8, and its installer rejects
# Microsoft.Net.Component.4.6.1.TargetingPack (exit 87). The NuGet reference-assembly packages
# carry the same files under build/.NETFramework/<version>/ as the classic
# %ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework layout, so restoring them once and
# handing the result to TargetFrameworkRootPath (-Env) covers every TFM without touching the box.
function Install-ReferenceAssemblies([string]$RefAsmRoot) {
    $scratch = Join-Path $RefAsmRoot '_scratch'
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    try {
        $refs = ($RefAsmTfms.Keys | ForEach-Object {
            "    <PackageReference Include=""Microsoft.NETFramework.ReferenceAssemblies.$_"" Version=""$RefAsmVersion"" />"
        }) -join "`n"
        $csproj = "<Project Sdk=`"Microsoft.NET.Sdk`">`n  <PropertyGroup>`n    <TargetFramework>net10.0</TargetFramework>`n  </PropertyGroup>`n  <ItemGroup>`n$refs`n  </ItemGroup>`n</Project>`n"
        $csprojPath = Join-Path $scratch 'refasm.csproj'
        [IO.File]::WriteAllText($csprojPath, $csproj, (New-Object Text.UTF8Encoding $false))
        Show-Step "restore reference assemblies $RefAsmVersion (net20-net481) -> $scratch"
        & dotnet restore $csprojPath --packages $scratch | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore of reference assemblies failed with exit code $LASTEXITCODE" }
        foreach ($tfm in $RefAsmTfms.Keys) {
            $version = $RefAsmTfms[$tfm]
            $source = Join-Path $scratch "microsoft.netframework.referenceassemblies.$tfm\$RefAsmVersion\build\.NETFramework\$version"
            if (-not (Test-Path -LiteralPath $source)) { throw "expected reference assemblies at $source; the package layout has changed" }
            $dest = Join-Path $RefAsmRoot ".NETFramework\$version"
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
            Copy-Item -LiteralPath $source -Destination $dest -Recurse -Force
        }
    }
    finally {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
    Show-Step "wrote   $RefAsmRoot"
}

function Get-DotnetSdksPath {
    $dotnetCmd = Get-Command dotnet -ErrorAction Stop
    $dotnetRoot = Split-Path -Parent $dotnetCmd.Source
    $version = (& dotnet --version).Trim()
    return Join-Path $dotnetRoot "sdk\$version\Sdks"
}

function Get-PairDir([string]$Slug) { Join-Path (Join-Path $CorpusRoot 'pairs') $Slug }

function Write-PairJson {
    param([string]$Slug, [hashtable]$Data)
    $dir = Get-PairDir $Slug
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $path = Join-Path $dir 'pair.json'
    [IO.File]::WriteAllText($path, ($Data | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))
    Show-Step "wrote   $path"
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
    if ($Id.StartsWith('pmb-')) {
        # -Fetch and pair.json print this slug back; accept it as-is (e.g. from -Unchanged) instead
        # of forcing the caller to reconstruct the owner/name it came from.
        $suffix = $Id.Substring(4)
        $bySlug = @(Get-Pmb | Where-Object { (Get-SafeName $_.repo) -eq $suffix })
        if ($bySlug.Count -eq 1) { return $Id }
    }
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
    'Prepare' {
        Assert-CorpusIgnored
        Write-CorpusSentinels
        $refAsmRoot = Get-RefAsmRoot
        if (Test-ReferenceAssembliesReady $refAsmRoot) {
            Show-Step "reuse   $refAsmRoot"
        }
        else {
            Install-ReferenceAssemblies $refAsmRoot
        }
    }

    'List' {
        Show-Step "pairs.csv"
        Get-Pairs | Select-Object slug, kind, repo, license | Format-Table -AutoSize | Out-Host
        $pmb = @(Get-Pmb)
        Show-Step ("poly-migrationbench-dotnet.csv: {0} repos (use -Select to choose agent pairs)" -f $pmb.Count)
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
            Show-Step "next    -PrepareAgent $Fetch, then migrate the copy as the skill says"
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
        Show-Step "copy    $legacyRoot -> $modernRoot"
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
        Show-Step "== census"
        if ($null -eq $census) { Show-Step "n/a (no run.properties.loweringCensus; needs M3-014)" }
        else { $census | ConvertTo-Json -Depth 6 | Out-Host }
        $unverified = Get-Bag (Get-Bag $run 'properties') 'unverified'
        Show-Step ("== unverified procedures: {0}" -f @($unverified | Where-Object { $_ }).Count)
        $invocations = @(Get-Bag $run 'invocations' | Where-Object { $_ })
        $notes = @()
        if ($invocations.Count -gt 0) { $notes = @(Get-Bag $invocations[0] 'toolExecutionNotifications' | Where-Object { $_ }) }
        Show-Step ("== tool execution notifications: {0}" -f $notes.Count)
        $notes | ForEach-Object { Show-Step ("  {0}: {1}" -f $_.level, $_.message.text) }
        $results = @(Get-Bag $run 'results' | Where-Object { $_ })
        Show-Step ("== results: {0}" -f $results.Count)
        $results | Group-Object ruleId | Sort-Object Name | ForEach-Object { Show-Step ("  {0} {1}" -f $_.Name, $_.Count) }
        foreach ($prop in 'proofMethod', 'scope') {
            Show-Step "== by $prop"
            $results | ForEach-Object {
                $value = Get-Bag (Get-Bag $_ 'properties') $prop
                if ($null -eq $value) { 'n/a' } else { [string]$value }
            } | Group-Object | Sort-Object Name | ForEach-Object { Show-Step ("  {0} {1}" -f $_.Name, $_.Count) }
        }
        Show-Step "== Unknown (EQ003) by reason (first word of the message until a reason property exists)"
        $results | Where-Object { $_.ruleId -eq 'EQ003' } | ForEach-Object {
            $reason = Get-Bag (Get-Bag $_ 'properties') 'reason'
            if ($null -eq $reason) { $reason = ($_.message.text -split '[\s:(]')[0] }
            [string]$reason
        } | Group-Object | Sort-Object Count -Descending | ForEach-Object { Show-Step ("  {0} {1}" -f $_.Name, $_.Count) }
    }

    'Clean' {
        $slug = Resolve-Slug $Clean
        $dir = Get-PairDir $slug
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force; Show-Step "removed $dir" }
        Show-Step "note    shared checkouts under .corpus/repos are kept; delete them by hand if needed"
    }

    'Env' {
        # On the success stream (Write-Output), not Show-Step/Write-Host, so `-Env | Invoke-Expression`
        # evaluates only these lines. MSBuild and the .NET SDK resolver read all of these from the
        # environment, so setting them before a restore or before `dotnet run --project src/Equiv.Cli`
        # is enough; no project file needs editing.
        $sdks = Get-DotnetSdksPath
        $refAsmRoot = Get-RefAsmRoot
        Write-Output "`$env:MSBuildSDKsPath = '$sdks'"
        Write-Output "`$env:MSBuildEnableWorkloadResolver = 'false'"
        Write-Output "`$env:TargetFrameworkRootPath = '$refAsmRoot'"
        Write-Output "`$env:NuGetAudit = 'false'"
        Write-Output "`$env:NoWarn = 'NU1701;NU1702;NU1903'"
        Write-Output "# also pass --force to dotnet restore (or -p:RestoreForce=true to MSBuild) for a forced restore"
    }

    'Refresh' {
        $url = "https://raw.githubusercontent.com/$Upstream/$UpstreamRef/Poly-MigrationBench-dotnet.csv"
        $resolved = ((& git ls-remote "https://github.com/$Upstream.git" $UpstreamRef) -split '\s+')[0]
        if (-not $resolved) { $resolved = $UpstreamRef }
        Show-Step "upstream $Upstream@$UpstreamRef ($resolved)"
        $content = (Invoke-WebRequest -UseBasicParsing -Uri $url).Content
        $new = @($content -split "`r?`n" | Where-Object { $_ } | ConvertFrom-Csv)
        $old = @(Get-Pmb)
        $oldByRepo = @{}; $old | ForEach-Object { $oldByRepo[$_.repo] = $_ }
        $newByRepo = @{}; $new | ForEach-Object { $newByRepo[$_.repo] = $_ }
        $added = @($new | Where-Object { -not $oldByRepo.ContainsKey($_.repo) })
        $removed = @($old | Where-Object { -not $newByRepo.ContainsKey($_.repo) })
        $moved = @($new | Where-Object { $oldByRepo.ContainsKey($_.repo) -and $oldByRepo[$_.repo].base_commit -ne $_.base_commit })
        Show-Step ("repos {0} -> {1}; added {2}, removed {3}, base_commit changed {4}" -f $old.Count, $new.Count, $added.Count, $removed.Count, $moved.Count)
        $added | ForEach-Object { Show-Step "  + $($_.repo)" }
        $removed | ForEach-Object { Show-Step "  - $($_.repo)" }
        $moved | ForEach-Object { Show-Step "  ~ $($_.repo)" }
        if ($Apply) {
            [IO.File]::WriteAllText($PmbPath, $content, (New-Object Text.UTF8Encoding $false))
            Show-Step "wrote   $PmbPath"
            Show-Step "now     record '$resolved' as the pinned upstream commit in tools/corpus/README.md"
        }
        else {
            Show-Step "dry run: nothing written (pass -Apply)"
        }
    }
}
