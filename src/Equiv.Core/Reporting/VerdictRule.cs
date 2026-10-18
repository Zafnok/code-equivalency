using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.RuntimeChanges;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

namespace Equiv.Core.Reporting;

/// <summary>
/// The section 6 mapping (VERIFICATION-MODEL.md): each <see cref="Verdict"/> kind's SARIF rule id,
/// <see cref="FailureLevel"/> and <see cref="ResultKind"/>. A <see cref="Divergent"/> whose
/// counterexample's call trace contains a call flagged by <see cref="RuntimeChangeTable"/> (ticket
/// M2-006) is EQ006 instead of EQ002, and <see cref="RuntimeChange"/> carries the reason and url
/// the SARIF writer renders; every other kind's <see cref="RuntimeChange"/> is null. The row is one that applies inside
/// the pair's runtime interval, as the frontend's flag was (ADR 0040 decision 2; ticket P2-055). A result whose pair is a
/// runtime exposure, its texts equal and only a runtime rule keeping it out of congruence, is EQ007 where it would be
/// EQ006 or EQ003, and EQ006 once the real runtimes show the difference (ADR 0060 decisions 2 and 4; ticket P2-238).
/// </summary>
internal static class VerdictRule
{
    /// <summary>The rule id of a Divergent whose counterexample calls a runtime-changed member.</summary>
    internal const string RuntimeChangeRule = "EQ006";

    /// <summary>The rule id of a runtime exposure (ADR 0060 decision 3).</summary>
    internal const string RuntimeExposureRule = "EQ007";

    private const string UnknownRule = "EQ003";

    /// <summary>
    /// <paramref name="result"/>'s rule (ADR 0060 decision 2): its verdict's, unless its pair is a runtime exposure. Then
    /// an EQ006, a witnessed exposure, and an EQ003, an unwitnessed one, are EQ007; a witnessed one whose replay reproduced
    /// it stays EQ006, and an observed Divergent is EQ006 too, its row the first rule's. EQ001 and EQ002 are unchanged.
    /// </summary>
    public static ResultRule Describe(VerificationResult result, RuntimeInterval runtimes)
    {
        ArgumentNullException.ThrowIfNull(result);
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = Describe(result.Verdict, runtimes);
        ImmutableArray<ExposureRule> exposure = ExposureRule.Of(result.NotCongruent, runtimes);
        return (exposure.IsEmpty, ruleId, result) switch
        {
            (true, _, _) => new ResultRule(ruleId, level, kind, runtimeChange, []),
            (_, RuntimeChangeRule, { Replay.Status: ReplayStatus.Reproduced }) => new ResultRule(ruleId, level, kind, runtimeChange, exposure),
            (_, RuntimeChangeRule or UnknownRule, _) => new ResultRule(RuntimeExposureRule, FailureLevel.Warning, ResultKind.Fail, runtimeChange, exposure),
            (_, _, { Verdict: Divergent { Observed: not null } }) => new ResultRule(RuntimeChangeRule, FailureLevel.Error, ResultKind.Fail, RuntimeChange: null, exposure),
            _ => new ResultRule(ruleId, level, kind, runtimeChange, []),
        };
    }

    /// <summary>
    /// <see cref="Verdict"/> is a closed hierarchy (private protected constructor) with five
    /// members; the final arm covers <see cref="Removed"/>, the only one not named above,
    /// mirroring the pattern <c>Equiv.Core.Ir.IrText.Type</c> uses for the same reason.
    /// </summary>
    public static (string RuleId, FailureLevel Level, ResultKind Kind, RuntimeChange? RuntimeChange) Describe(Verdict verdict, RuntimeInterval runtimes) => verdict switch
    {
        Equivalent => ("EQ001", FailureLevel.None, ResultKind.Pass, null),
        Divergent divergent => DescribeDivergent(divergent, runtimes),
        Unknown => ("EQ003", FailureLevel.Warning, ResultKind.Open, null),
        Added => ("EQ004", FailureLevel.Note, ResultKind.Informational, null),
        _ => ("EQ005", FailureLevel.Note, ResultKind.Informational, null),
    };

    private static (string, FailureLevel, ResultKind, RuntimeChange?) DescribeDivergent(Divergent divergent, RuntimeInterval runtimes)
    {
        RuntimeChange? runtimeChange = FindRuntimeChange(divergent.Counterexample, runtimes);
        return runtimeChange is null
            ? ("EQ002", FailureLevel.Error, ResultKind.Fail, null)
            : ("EQ006", FailureLevel.Error, ResultKind.Fail, runtimeChange);
    }

    /// <summary>
    /// The first flagged call's table row inside <paramref name="runtimes"/> across both sides' traces, or null when the divergence
    /// does not involve a runtime-changed callee (VERIFICATION-MODEL.md section 3: "any pair
    /// containing a flagged call that reaches an observable reports Divergent with ruleId EQ006").
    /// </summary>
    private static RuntimeChange? FindRuntimeChange(Counterexample counterexample, RuntimeInterval runtimes)
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        foreach (CallIdentity callee in counterexample.Old.Trace.Concat(counterexample.New.Trace)
            .Select(static record => record.Callee)
            .Where(static callee => callee.RuntimeChanged))
        {
            if (table.TryMatch(callee, runtimes, out RuntimeChange match))
            {
                return match;
            }
        }

        return null;
    }
}
