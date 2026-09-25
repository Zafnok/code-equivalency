using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

namespace Equiv.Cli;

/// <summary>A run's Unknown results by scope, as the lowering census reports them (ADR 0029 decision 4; ticket M3-025).</summary>
internal sealed record ScopeCounts(int Line, int Method)
{
    public static ScopeCounts Of(IEnumerable<VerificationResult> results)
    {
        Unknown[] unknowns = [.. results.Select(static r => r.Verdict).OfType<Unknown>()];
        int line = unknowns.Count(static u => u.Scope == UnknownScope.Line);
        return new ScopeCounts(line, unknowns.Length - line);
    }
}
