using System.Globalization;

using Equiv.Core.Ir;
using Equiv.Core.RuntimeChanges;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

namespace Equiv.Core.Reporting;

/// <summary>
/// The review list (ticket P2-064; VERIFICATION-MODEL.md section 6): every flagged result, EQ002, EQ003 or EQ006, is put
/// in a group by its cause (<see cref="KeyOf"/>, SARIF <c>properties.reviewGroup</c>), and each group gets one SARIF
/// <c>rank</c>, so that a reviewer reads tens of groups, most certain first, instead of every method. A group is a rule
/// id and a key. It takes the best tier any of its results is in: a Divergent the real runtimes showed
/// (<c>proofMethod: observed</c> or <c>replay: reproduced</c>), any other EQ002, EQ006, a line-scoped EQ003, a
/// method-scoped EQ003. Inside a tier a larger group ranks higher. <see cref="Apply"/> reads the SARIF results, so a
/// baseline's carry-overs are grouped, ranked and counted like the run's own. Nothing here changes a verdict, a rule id, a
/// fingerprint or an exit code (ADR 0006's 2026-10-01 clarification).
/// </summary>
public static class ReviewList
{
    /// <summary>The result property that holds a flagged result's group key.</summary>
    internal const string GroupProperty = "reviewGroup";

    /// <summary>The run property that holds the groups, highest rank first.</summary>
    internal const string ListProperty = "reviewList";

    /// <summary>The key of a carried-over result whose baseline was written before this ticket: the SARIF alone does not hold its cause.</summary>
    internal const string Ungrouped = "ungrouped";

    private const string DivergentRule = "EQ002";
    private const string UnknownRule = "EQ003";
    private const string RuntimeChangeRule = "EQ006";

    /// <summary>How many of a group's procedure identities its <c>reviewList</c> entry names.</summary>
    private const int IdentitiesListed = 5;

    /// <summary>How many groups <see cref="Lines"/> prints.</summary>
    private const int GroupsPrinted = 10;

    /// <summary>A tier is 20 wide and a result is worth 0.002, so a group's size counts up to here.</summary>
    private const int LargestCounted = 9999;

    /// <summary>
    /// What <c>equiv compare</c> prints after the analysed line counts, and what <c>equiv mcp</c> adds to its summary:
    /// <c>review list: G groups for R flagged results</c>, then the ten highest-ranked groups as
    /// <c>  ruleId count=n rank=r group</c>. Only <c>new</c> and <c>updated</c> results are counted, so against a baseline it
    /// lists what the run changed, while <c>run.properties.reviewList</c> counts every result. Empty for a run without
    /// that property (<c>--lower-only</c>).
    /// </summary>
    public static IReadOnlyList<string> Lines(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!run.PropertyNames.Contains(ListProperty, StringComparer.Ordinal))
        {
            return [];
        }

        List<(Group Group, int Count)> groups =
        [
            .. Groups(run.Results)
                .Select(static g => (g, g.Results.Count(static r => r.BaselineState is BaselineState.New or BaselineState.Updated)))
                .Where(static g => g.Item2 > 0),
        ];
        return
        [
            string.Create(CultureInfo.InvariantCulture, $"review list: {groups.Count} groups for {groups.Sum(static g => g.Count)} flagged results"),
            .. groups.Take(GroupsPrinted).Select(static g =>
                string.Create(CultureInfo.InvariantCulture, $"  {g.Group.RuleId} count={g.Count} rank={g.Group.Rank:0.###} {g.Group.Key}")),
        ];
    }

    /// <summary>
    /// The group key of <paramref name="verdict"/>, or null when it is not a flagged one. It is derived from what the
    /// verdict already carries and reads every list as a set, so it is the same on every run of the same inputs:
    /// <list type="bullet">
    /// <item>EQ006: <c>runtime-change:</c> and the table row's member, the one the message cites;</item>
    /// <item>EQ002: <c>calls:</c> and the call identities only one side's trace holds, sorted and joined by <c>|</c> (the
    /// migration swapped one member for another); when both traces call the same members, <c>proofMethod:observed</c>, or
    /// <c>proofMethod:none</c> for a solver's counterexample, which carries no <c>proofMethod</c>;</item>
    /// <item>EQ003: the <c>unknownReason</c>, and for <c>opaque</c> and <c>abstraction</c> a <c>:</c> and the opaque reasons
    /// behind it, sorted and joined by <c>+</c>: an opaque Unknown's causes, an abstraction Unknown's opaque fragments.</item>
    /// </list>
    /// </summary>
    internal static string? KeyOf(Verdict verdict, RuntimeChange? runtimeChange) => verdict switch
    {
        Divergent when runtimeChange is not null => $"runtime-change:{runtimeChange.Member}",
        Divergent divergent => DivergentKey(divergent),
        Unknown unknown => UnknownKey(unknown),
        _ => null,
    };

    /// <summary>
    /// Gives every flagged result in <paramref name="results"/> its group's <c>rank</c>, and <see cref="Ungrouped"/> as its
    /// <c>reviewGroup</c> when it has none, and returns <c>run.properties.reviewList</c>: one entry per group, highest rank
    /// first, with its <c>group</c>, <c>ruleId</c>, <c>rank</c>, <c>count</c> and first five <c>identities</c>.
    /// </summary>
    internal static List<Dictionary<string, object>> Apply(IReadOnlyList<Result> results)
    {
        List<Group> groups = Groups(results);
        foreach (Group group in groups)
        {
            foreach (Result result in group.Results)
            {
                result.SetProperty(GroupProperty, group.Key);
                result.Rank = group.Rank;
            }
        }

        return
        [
            .. groups.Select(static g => new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["group"] = g.Key,
                ["ruleId"] = g.RuleId,
                ["rank"] = g.Rank,
                ["count"] = g.Results.Count,
                ["identities"] = g.Results.Take(IdentitiesListed).Select(static r => r.PartialFingerprints[SarifReportWriter.ProcedureIdentityFingerprintId]).ToList(),
            }),
        ];
    }

    private static string DivergentKey(Divergent divergent)
    {
        HashSet<string> differing = Callees(divergent.Counterexample.Old);
        differing.SymmetricExceptWith(Callees(divergent.Counterexample.New));
        string proofMethod = divergent.Observed is null ? "none" : SarifReportWriter.ObservedProofMethod;
        return differing.Count > 0
            ? $"calls:{string.Join('|', differing.Order(StringComparer.Ordinal))}"
            : $"proofMethod:{proofMethod}";
    }

    private static HashSet<string> Callees(IrRun run) => new(run.Trace.Select(static c => c.Callee.Value), StringComparer.Ordinal);

    private static string UnknownKey(Unknown unknown)
    {
        IEnumerable<string> reasons = unknown.Reason switch
        {
            UnknownReason.Opaque => unknown.Causes.Select(static c => c.Reason),
            UnknownReason.Abstraction => unknown.Abstractions.Select(static a => a.Reason).OfType<string>(),
            _ => [],
        };
        string[] sorted = [.. reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string reason = SarifReportWriter.Name(unknown.Reason);
        return sorted.Length > 0 ? $"{reason}:{string.Join('+', sorted)}" : reason;
    }

    /// <summary>The flagged results' groups, highest rank first; equal ranks by key, then by rule id, so the order is total.</summary>
    private static List<Group> Groups(IEnumerable<Result> results) =>
    [
        .. results
            .Where(static r => r.RuleId is DivergentRule or UnknownRule or RuntimeChangeRule)
            .GroupBy(static r => (r.RuleId, Key: r.TryGetProperty(GroupProperty, out string? key) ? key : Ungrouped))
            .Select(static g => new Group(g.Key.Key, g.Key.RuleId, RankOf(g.Min(Tier), g.Count()), [.. g]))
            .OrderByDescending(static g => g.Rank)
            .ThenBy(static g => g.Key, StringComparer.Ordinal)
            .ThenBy(static g => g.RuleId, StringComparer.Ordinal),
    ];

    /// <summary>1, the most certain, to 5, read from the properties <see cref="SarifReportWriter"/> wrote on the result.</summary>
    private static int Tier(Result result) => result.RuleId switch
    {
        UnknownRule => Has(result, "scope", "line") ? 4 : 5,
        _ when Has(result, "proofMethod", SarifReportWriter.ObservedProofMethod) || Has(result, "replay", "reproduced") => 1,
        DivergentRule => 2,
        _ => 3,
    };

    private static bool Has(Result result, string property, string value) =>
        result.TryGetProperty(property, out string? found) && string.Equals(found, value, StringComparison.Ordinal);

    /// <summary>
    /// <c>20 x (5 - tier) + min(count, 9999) / 500</c>: tier 1 is 80.002 to 99.998 and tier 5 is 0.002 to 19.998. It depends
    /// on the group's tier and size only, never on the run's other groups.
    /// </summary>
    private static double RankOf(int tier, int count) => ((20_000 * (5 - tier)) + (2 * Math.Min(count, LargestCounted))) / 1000.0;

    private sealed record Group(string Key, string RuleId, double Rank, IReadOnlyList<Result> Results);
}
