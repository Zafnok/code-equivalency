# Ticket P2-063: corpus.ps1 -Metrics against a synthetic SARIF (made-up identities only).
# Plain `if (...) { throw }` assertions keep this runnable under Windows PowerShell's bundled Pester 3.4 and Pester 5.
# Run: Invoke-Pester ./tools/corpus/tests/Metrics.Tests.ps1

Describe 'corpus.ps1 -Metrics' {
    BeforeAll {
        $script:Corpus = Join-Path (Split-Path $PSScriptRoot -Parent) 'corpus.ps1'
        $script:Sarif = Join-Path $PSScriptRoot 'fixtures/metrics.sarif'
    }

    # -Metrics prints with Write-Host, which is the information stream (6).
    It 'Metrics_Groups_Unknown_By_UnknownReason_Then_Scope' {
        $out = @(& $script:Corpus -Metrics $script:Sarif 6>&1 | ForEach-Object { [string]$_ })
        $header = '== Unknown (EQ003) by unknownReason, and by scope within it'
        $at = [Array]::IndexOf($out, $header)
        if ($at -lt 0) { throw "no '$header' line:`n$($out -join "`n")" }
        $actual = $out[($at + 1)..($at + 7)]
        $expected = @(
            '  abstraction 3'
            '    whole 2'
            '    partial 1'
            '  n/a 1'
            '    n/a 1'
            '  timeout 1'
            '    whole 1'
        )
        $diff = Compare-Object -ReferenceObject $expected -DifferenceObject $actual -SyncWindow 0
        if ($diff) { throw "output differs:`n$($actual -join "`n")" }
    }

    # Ticket P2-238: runtime exposures, those with a witness and the others by unknownReason.
    It 'Metrics_Splits_RuntimeExposures_By_Witness_Then_UnknownReason' {
        $out = @(& $script:Corpus -Metrics $script:Sarif 6>&1 | ForEach-Object { [string]$_ })
        $header = '== runtime exposure (EQ007): witnessed, and the others by unknownReason'
        $at = [Array]::IndexOf($out, $header)
        if ($at -lt 0) { throw "no '$header' line:`n$($out -join "`n")" }
        $actual = $out[($at + 1)..($out.Count - 1)]
        $expected = @(
            '  witnessed 1'
            '  not witnessed, opaque 2'
        )
        $diff = Compare-Object -ReferenceObject $expected -DifferenceObject $actual -SyncWindow 0
        if ($diff) { throw "output differs:`n$($actual -join "`n")" }
    }

    # Ticket P2-173 criterion 4: the pairs that are not congruent, counted by the component kind their result names.
    It 'Metrics_Counts_NotCongruent_Results_By_Component' {
        $out = @(& $script:Corpus -Metrics $script:Sarif 6>&1 | ForEach-Object { [string]$_ })
        $header = '== not congruent (notCongruent), by component'
        $at = [Array]::IndexOf($out, $header)
        if ($at -lt 0) { throw "no '$header' line:`n$($out -join "`n")" }
        $actual = $out[($at + 1)..($at + 4)]
        $expected = @(
            '  callee 2'
            '  constant 1'
            '  type-layout 1'
            '== Unknown (EQ003) by unknownReason, and by scope within it'
        )
        $diff = Compare-Object -ReferenceObject $expected -DifferenceObject $actual -SyncWindow 0
        if ($diff) { throw "output differs:`n$($actual -join "`n")" }
    }
}
