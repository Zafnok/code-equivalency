using System.Linq;

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
/// the SARIF writer renders; every other kind's <see cref="RuntimeChange"/> is null.
/// </summary>
internal static class VerdictRule
{
    /// <summary>
    /// <see cref="Verdict"/> is a closed hierarchy (private protected constructor) with five
    /// members; the final arm covers <see cref="Removed"/>, the only one not named above,
    /// mirroring the pattern <c>Equiv.Core.Ir.IrText.Type</c> uses for the same reason.
    /// </summary>
    public static (string RuleId, FailureLevel Level, ResultKind Kind, RuntimeChange? RuntimeChange) Describe(Verdict verdict) => verdict switch
    {
        Equivalent => ("EQ001", FailureLevel.None, ResultKind.Pass, null),
        Divergent divergent => DescribeDivergent(divergent),
        Unknown => ("EQ003", FailureLevel.Warning, ResultKind.Open, null),
        Added => ("EQ004", FailureLevel.Note, ResultKind.Informational, null),
        _ => ("EQ005", FailureLevel.Note, ResultKind.Informational, null),
    };

    private static (string, FailureLevel, ResultKind, RuntimeChange?) DescribeDivergent(Divergent divergent)
    {
        RuntimeChange? runtimeChange = FindRuntimeChange(divergent.Counterexample);
        return runtimeChange is null
            ? ("EQ002", FailureLevel.Error, ResultKind.Fail, null)
            : ("EQ006", FailureLevel.Error, ResultKind.Fail, runtimeChange);
    }

    /// <summary>
    /// The first flagged call's table row across both sides' traces, or null when the divergence
    /// does not involve a runtime-changed callee (VERIFICATION-MODEL.md section 3: "any pair
    /// containing a flagged call that reaches an observable reports Divergent with ruleId EQ006").
    /// </summary>
    private static RuntimeChange? FindRuntimeChange(Counterexample counterexample)
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        foreach (CallIdentity callee in counterexample.Old.Trace.Concat(counterexample.New.Trace)
            .Select(static record => record.Callee)
            .Where(static callee => callee.RuntimeChanged))
        {
            if (table.TryMatch(callee, out RuntimeChange match))
            {
                return match;
            }
        }

        return null;
    }
}
