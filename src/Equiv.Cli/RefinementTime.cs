using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

namespace Equiv.Cli;

/// <summary>
/// How many Unknown pairs ran ADR 0037's failure-refinement queries, and the time those queries took in all, as the
/// lowering census reports them (ticket P1-013); and the same count and time for the Divergent pairs (ticket P1-020).
/// </summary>
internal sealed record RefinementTime(int Pairs, long Milliseconds)
{
    public static RefinementTime Of(IEnumerable<VerificationResult> results) =>
        Of(results.Select(static r => r.Verdict).OfType<Unknown>().Select(static u => u.FailureRefinement));

    /// <summary>The same for the Divergent pairs, which run three queries each (ticket P1-020 criterion 8).</summary>
    public static RefinementTime OfDivergent(IEnumerable<VerificationResult> results) =>
        Of(results.Select(static r => r.Verdict).OfType<Divergent>().Select(static d => d.FailureRefinement));

    private static RefinementTime Of(IEnumerable<FailureRefinement?> queried)
    {
        FailureRefinement[] refinements = [.. queried.OfType<FailureRefinement>()];
        return new RefinementTime(refinements.Length, (long)refinements.Sum(static r => r.Elapsed.TotalMilliseconds));
    }
}
