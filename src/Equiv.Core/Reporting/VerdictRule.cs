using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

namespace Equiv.Core.Reporting;

/// <summary>
/// The section 6 mapping (VERIFICATION-MODEL.md): each <see cref="Verdict"/> kind's SARIF rule id,
/// <see cref="FailureLevel"/> and <see cref="ResultKind"/>. EQ001-EQ005 only; EQ006
/// (runtime-changed-API divergence) is a later ticket's addition to <see cref="Divergent"/>.
/// </summary>
internal static class VerdictRule
{
    /// <summary>
    /// <see cref="Verdict"/> is a closed hierarchy (private protected constructor) with five
    /// members; the final arm covers <see cref="Removed"/>, the only one not named above,
    /// mirroring the pattern <c>Equiv.Core.Ir.IrText.Type</c> uses for the same reason.
    /// </summary>
    public static (string RuleId, FailureLevel Level, ResultKind Kind) Describe(Verdict verdict) => verdict switch
    {
        Equivalent => ("EQ001", FailureLevel.None, ResultKind.Pass),
        Divergent => ("EQ002", FailureLevel.Error, ResultKind.Fail),
        Unknown => ("EQ003", FailureLevel.Warning, ResultKind.Open),
        Added => ("EQ004", FailureLevel.Note, ResultKind.Informational),
        _ => ("EQ005", FailureLevel.Note, ResultKind.Informational),
    };
}
