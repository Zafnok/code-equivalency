# Ticket M4-015: corpus.ps1 -Progress against a synthetic progress.log (made-up identities only).
# Plain `if (...) { throw }` assertions keep this runnable under Windows PowerShell's bundled Pester 3.4 and Pester 5.
# Run: Invoke-Pester ./tools/corpus/tests/Progress.Tests.ps1

Describe 'corpus.ps1 -Progress' {
    BeforeAll {
        $script:Corpus = Join-Path (Split-Path $PSScriptRoot -Parent) 'corpus.ps1'
        $script:RunDir = Join-Path ([IO.Path]::GetTempPath()) ('equiv-progress-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force -Path $script:RunDir | Out-Null
        $script:Log = Join-Path $script:RunDir 'progress.log'
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/progress.log') -Destination $script:Log

        function script:Assert-Lines([string[]]$Actual, [string[]]$Expected) {
            $diff = Compare-Object -ReferenceObject $Expected -DifferenceObject $Actual -SyncWindow 0
            if ($diff) { throw "output differs:`n$(($Actual) -join "`n")" }
        }

        # A writer that holds the log open, as equiv does mid-run: a reader that did not share ReadWrite would fail.
        function script:Invoke-WhileWriterHoldsLog([scriptblock]$Body) {
            $writer = [IO.File]::Open($script:Log, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
            try { & $Body } finally { $writer.Dispose() }
        }
    }

    AfterAll {
        Remove-Item -LiteralPath $script:RunDir -Recurse -Force
    }

    It 'Progress_Reports_Current_Item_And_Eta' {
        $out = @(Invoke-WhileWriterHoldsLog { & $script:Corpus -Progress $script:RunDir })
        Assert-Lines $out @(
            'phase: verify 3/6 (40%)'
            'eta: 00:00:40.500 worst: 00:02:30.000 (at +00:01:02)'
            'current: Contoso.Widgets.Sprocket.Rest() running 16.800s slow'
            'slowest:'
            '  8.250s verify Contoso.Widgets.Gear.Stop() (equivalent)'
            '  6.000s lower Contoso.Widgets.Sprocket.Turn(int) (opaque)'
            '  2.000s lower Contoso.Widgets.Gear.Stop() (lowered)'
            '  1.900s lower Contoso.Widgets.Gear.Spin(int, string) (lowered)'
            '  1.200s enumerate legacy (enumerated)'
        )
    }

    # Ticket P2-077 criterion 3: with pairs verified in parallel a heartbeat is one line for each item in flight.
    It 'Progress_Reports_Every_Item_In_Flight' {
        $dir = Join-Path $script:RunDir 'parallel'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $lines = @(Get-Content -LiteralPath $script:Log) + @(
            'equiv: +00:01:02 verify detail: stage=check:divergence took=1.5s result=unknown item=Contoso.Widgets.Sprocket.Rest()'
            'equiv: +00:01:12 verify 3/6 (40%) item=Contoso.Widgets.Sprocket.Rest() took=26.800 eta=00:00:40.500 worst=00:02:30.000 rate=0.1/s slow'
            'equiv: +00:01:12 verify 3/6 (40%) item=Contoso.Widgets.Cog.Mesh(int, int) took=3.250 eta=00:00:40.500 worst=00:02:30.000 rate=0.1/s'
        )
        Set-Content -LiteralPath (Join-Path $dir 'progress.log') -Value $lines -Encoding utf8
        $out = @(& $script:Corpus -Progress $dir)
        Assert-Lines $out[0..3] @(
            'phase: verify 3/6 (40%)'
            'eta: 00:00:40.500 worst: 00:02:30.000 (at +00:01:12)'
            'current: Contoso.Widgets.Sprocket.Rest() running 26.800s slow'
            'current: Contoso.Widgets.Cog.Mesh(int, int) running 3.250s'
        )
    }

    It 'Progress_Between_Items_Reports_None_In_Flight' {
        $dir = Join-Path $script:RunDir 'between'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Set-Content -LiteralPath (Join-Path $dir 'progress.log') -Value @(Get-Content -LiteralPath $script:Log | Select-Object -First 26) -Encoding utf8
        $out = @(& $script:Corpus -Progress $dir)
        Assert-Lines $out[0..2] @(
            'phase: verify 3/6 (40%)'
            'eta: 00:00:15.000 worst: 00:02:30.000 (at +00:00:45)'
            'current: none reported since the last item finished'
        )
    }

    It 'Progress_Never_Touches_The_Equiv_Process' {
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($script:Corpus, [ref]$tokens, [ref]$errors)
        $names = 'Read-ProgressLog', 'Get-InFlight', 'Get-ProgressReport', 'Get-PhaseTimes'
        $bodies = $ast.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $names -contains $n.Name }, $true)
        if (@($bodies).Count -ne $names.Count) { throw 'progress functions not found in corpus.ps1' }
        foreach ($body in $bodies) {
            if ($body.Extent.Text -match 'Get-Process|Diagnostics\.Process|Stop-Process|Wait-Process') { throw "$($body.Name) touches a process" }
        }
    }

    It 'Summary_Fills_Phase_Times' {
        $out = @(Invoke-WhileWriterHoldsLog { & $script:Corpus -Progress $script:RunDir -Summary })
        Assert-Lines $out @(
            '| phase | items | seconds | ETA error at 50% |'
            '|---|---|---|---|'
            '| load-legacy | 2 | 12.400 | +8.000 |'
            '| load-modern | 1 | 9.100 | +0.000 |'
            '| enumerate | 2 | 2.300 | +0.200 |'
            '| match | 1 | 0.800 | +0.000 |'
            '| lower | 4 | 11.000 | -3.000 |'
        )
    }
}
