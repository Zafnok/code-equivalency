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
        $actual = $out[($at + 1)..($out.Count - 1)]
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
}
