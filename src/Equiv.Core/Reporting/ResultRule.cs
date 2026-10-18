using System.Collections.Immutable;

using Equiv.Core.RuntimeChanges;

using Microsoft.CodeAnalysis.Sarif;

namespace Equiv.Core.Reporting;

/// <summary>
/// What <see cref="VerdictRule.Describe(VerificationResult, RuntimeInterval)"/> makes of one result: its SARIF rule id,
/// level and kind, the table row its counterexample's runtime-changed call matched (EQ006 and a witnessed EQ007), and,
/// when its pair is a runtime exposure reported as one (EQ007, or EQ006 once shown), the rules that keep the pair out of
/// congruence (ADR 0060; ticket P2-238). <see cref="Exposure"/> is empty on every other result.
/// </summary>
internal sealed record ResultRule(string RuleId, FailureLevel Level, ResultKind Kind, RuntimeChange? RuntimeChange, ImmutableArray<ExposureRule> Exposure)
{
    /// <summary>
    /// The member an EQ006 or EQ007 names in its review group: the row the counterexample's call matched, else the
    /// exposure's first rule. Null on any other result.
    /// </summary>
    public string? RuntimeMember => RuntimeChange?.Member ?? (Exposure.IsEmpty ? null : Exposure[0].Rule);
}
