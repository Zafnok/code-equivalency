using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

namespace Equiv.Cli;

/// <summary>
/// How many pairs ran ADR 0048's search for an input condition, how many of them have one, and the time the searches took
/// in all, as the lowering census reports them (ticket P1-022).
/// </summary>
internal sealed record ConditionTime(int Pairs, int Admitted, long Milliseconds)
{
    public static ConditionTime Of(IEnumerable<VerificationResult> results)
    {
        ConditionSearch[] searches = [.. results.Select(static r => ConditionSearch.Of(r.Verdict)).OfType<ConditionSearch>()];
        return new ConditionTime(searches.Length, searches.Count(static s => s.AgreesWhen is not null), (long)searches.Sum(static s => s.Elapsed.TotalMilliseconds));
    }
}