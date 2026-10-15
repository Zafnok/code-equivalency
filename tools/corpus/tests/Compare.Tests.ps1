# Ticket P1-032 (ADR 0049): corpus.ps1 -Compare passes the compare mode through, and -Metrics names the mode a log was written in.
# Plain `if (...) { throw }` assertions keep this runnable under Windows PowerShell's bundled Pester 3.4 and Pester 5.
# Run: Invoke-Pester ./tools/corpus/tests/Compare.Tests.ps1

Describe 'corpus.ps1 -Compare and the compare mode' {
    BeforeAll {
        $script:Corpus = Join-Path (Split-Path $PSScriptRoot -Parent) 'corpus.ps1'

        # Dot-sourcing with the read-only -List set defines the script's functions here without running anything.
        . $script:Corpus -List 6>$null
        $script:Root = Join-Path ([IO.Path]::GetTempPath()) ('equiv-compare-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force -Path $script:Root | Out-Null

        # A made-up SARIF log with the given run properties and one result per (ruleId, decidedBy) pair.
        function script:New-Sarif([string]$Name, [hashtable]$Properties, [object[]]$Results) {
            $path = Join-Path $script:Root $Name
            $log = @{ version = '2.1.0'; runs = @(@{ properties = $Properties; results = @($Results) }) }
            [IO.File]::WriteAllText($path, ($log | ConvertTo-Json -Depth 8))
            return $path
        }

        function script:Get-Metrics([string]$Path) {
            @(& $script:Corpus -Metrics $Path 6>&1 | ForEach-Object { [string]$_ })
        }

        function script:Assert-Throws([scriptblock]$Action, [string]$Like) {
            $message = $null
            try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
            if ($null -eq $message -or $message -notlike $Like) { throw "expected an error like '$Like', got '$message'" }
        }
    }

    AfterAll {
        Remove-Item -LiteralPath $script:Root -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'Compare_PassesTheModeThrough' {
        foreach ($mode in 'thorough', 'quick') {
            $arguments = @(Get-CompareArguments -Legacy 'a.sln' -ModernSolution 'b.sln' -RunDir 'run' -CompareMode $mode -Census $false -Extra @('--jobs', '4'))
            $expected = @('compare', '--legacy', 'a.sln', '--modern', 'b.sln', '--out', (Join-Path 'run' 'equiv.sarif'),
                '--verbosity', 'debug', '--log', (Join-Path 'run' 'progress.log'), '--mode', $mode, '--jobs', '4')
            if (Compare-Object -ReferenceObject $expected -DifferenceObject $arguments -SyncWindow 0) { throw "arguments differ: $($arguments -join ' ')" }
        }
    }

    It 'Compare_TheCensusTakesNoMode' {
        $arguments = @(Get-CompareArguments -Legacy 'a.sln' -ModernSolution 'b.sln' -RunDir 'run' -CompareMode '' -Census $true -Extra @())
        if ($arguments -contains '--mode') { throw "the census was given a mode: $($arguments -join ' ')" }
        if ($arguments[-1] -ne '--lower-only') { throw "the census is not --lower-only: $($arguments -join ' ')" }
        Assert-Throws { Get-CompareArguments -Legacy 'a.sln' -ModernSolution 'b.sln' -RunDir 'run' -CompareMode 'quick' -Census $true -Extra @() } '*takes no -Mode*'
    }

    It 'Compare_PassesThePairsOwnConfig' {
        $arguments = @(Get-CompareArguments -Legacy 'a.sln' -ModernSolution 'b.sln' -RunDir 'run' -CompareMode 'quick' -Census $false -Extra @() -Config 'pair.json')
        $at = [array]::IndexOf($arguments, '--config')
        if ($at -lt 0 -or $arguments[$at + 1] -ne 'pair.json') { throw "the pair's config was not passed: $($arguments -join ' ')" }
        $census = @(Get-CompareArguments -Legacy 'a.sln' -ModernSolution 'b.sln' -RunDir 'run' -CompareMode '' -Census $true -Extra @() -Config 'pair.json')
        if ($census -notcontains 'pair.json') { throw "the census was not given the pair's config: $($census -join ' ')" }
        $none = @(Get-CompareArguments -Legacy 'a.sln' -ModernSolution 'b.sln' -RunDir 'run' -CompareMode 'quick' -Census $false -Extra @())
        if ($none -contains '--config') { throw "a pair without a config got one: $($none -join ' ')" }
    }

    It 'Compare_ACommandLineConfigReplacesThePairs' {
        $arguments = @(Get-CompareArguments -Legacy 'a.sln' -ModernSolution 'b.sln' -RunDir 'run' -CompareMode 'quick' -Census $false -Extra @('--config', 'mine.json') -Config 'pair.json')
        if (@($arguments | Where-Object { $_ -eq '--config' }).Count -ne 1 -or $arguments -contains 'pair.json') { throw "expected only --config mine.json: $($arguments -join ' ')" }
    }

    It 'PairConfig_OpenRaStatesItsModsRunOnNet5' {
        # P2-197: OpenRA's modern mods (netstandard2.1, loaded as plugins) run in the net5.0 launcher's process.
        $path = Get-PairConfig 'openra-17989'
        if (-not $path) { throw 'openra-17989 has no pair config' }
        $config = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($config.runtimes.modern -ne 'net5.0') { throw "runtimes.modern is '$($config.runtimes.modern)', not net5.0" }
        if ($config.runtimes.PSObject.Properties['legacy']) { throw 'the legacy side reads its runtime from its projects; it states none' }
        if (Get-PairConfig 'no-such-pair') { throw 'a pair with no config file got one' }
    }

    It 'Compare_AVerifyingRunMustNameItsMode' {
        Assert-Throws { Get-CompareArguments -Legacy 'a.sln' -ModernSolution 'b.sln' -RunDir 'run' -CompareMode '' -Census $false -Extra @() } '*names its compare mode*'
    }

    It 'Metrics_NamesTheCompareModeAndWhatEachLaterPassProduced' {
        $mode = @{ name = 'thorough'; bound = 3; resourceLimit = 2000000; timeoutMs = 60000; explicit = @('bound')
            escalation = @{ bound = 8; resourceLimit = 30000000; timeoutMs = 600000 } }
        $results = @(
            @{ ruleId = 'EQ001'; properties = @{ decidedBy = 'budget-pass' } }
            @{ ruleId = 'EQ002'; properties = @{ decidedBy = 'budget-pass' } }
            @{ ruleId = 'EQ001'; properties = @{ decidedBy = 'budget-pass' } }
            @{ ruleId = 'EQ003'; properties = @{ decidedBy = 'il-pass'; unknownReason = 'opaque' } }
            @{ ruleId = 'EQ001'; properties = @{ proofMethod = 'bounded' } }
        )
        $out = Get-Metrics (New-Sarif 'thorough.sarif' @{ mode = $mode } $results)
        $expected = @(
            '== compare mode (ADR 0049)'
            '  thorough: bound 3, resourceLimit 2000000, timeoutMs 60000'
            '  budget pass: bound 8, resourceLimit 30000000, timeoutMs 600000'
            '  set explicitly: bound'
            '== results a later pass produced (decidedBy), by rule'
            '  budget-pass EQ001 2'
            '  budget-pass EQ002 1'
            '  il-pass EQ003 1'
        )
        foreach ($line in $expected) { if ($out -notcontains $line) { throw "no '$line' line:`n$($out -join "`n")" } }
    }

    It 'Metrics_AQuickRunHasNoBudgetPassAndACensusNoMode' {
        $quick = Get-Metrics (New-Sarif 'quick.sarif' @{ mode = @{ name = 'quick'; bound = 3; resourceLimit = 2000000; timeoutMs = 60000; explicit = @() } } @())
        if ($quick -notcontains '  quick: bound 3, resourceLimit 2000000, timeoutMs 60000') { throw "no quick line:`n$($quick -join "`n")" }
        if (@($quick | Where-Object { $_ -like '  budget pass:*' }).Count -ne 0) { throw 'a quick run printed a budget pass' }
        if ($quick -notcontains '  set explicitly: ') { throw "no empty 'set explicitly' line:`n$($quick -join "`n")" }
        $census = Get-Metrics (New-Sarif 'census.sarif' @{ loweringCensus = @{ matchedPairs = 1 } } @())
        if ($census -notcontains 'n/a (no run.properties.mode: a --lower-only census, or a run from before P1-032)') { throw "no n/a line:`n$($census -join "`n")" }
    }
}
