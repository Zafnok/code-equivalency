using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

namespace Equiv.Cli;

/// <summary>
/// How many Unknown pairs ran ADR 0037's failure-refinement queries, and the time those queries took in all, as the
/// lowering census reports them (ticket P1-013).
/// </summary>
internal sealed record RefinementTime(int Pairs, long Milliseconds)
{
    public static RefinementTime Of(IEnumerable<VerificationResult> results)
    {
        FailureRefinement[] refinements = [.. results.Select(static r => r.Verdict).OfType<Unknown>().Select(static u => u.FailureRefinement).OfType<FailureRefinement>()];
        return new RefinementTime(refinements.Length, (long)refinements.Sum(static r => r.Elapsed.TotalMilliseconds));
    }
}
