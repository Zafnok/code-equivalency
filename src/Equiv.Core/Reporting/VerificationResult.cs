using System.Collections.Immutable;

using Equiv.Core.Verdicts;

namespace Equiv.Core.Reporting;

/// <summary>
/// One row of a report: the verdict reached for a single procedure identity. This is the
/// hand-off point between matching/verification and reporting (VERIFICATION-MODEL.md section 6);
/// assembling a list of these from a <see cref="Matching.MatchResult"/> plus
/// <see cref="IVerificationBackend"/> runs (including what an <see cref="Matching.MatchResult.Ambiguous"/>
/// identity becomes) is CLI-orchestration, out of scope for this ticket. <see cref="EquivalencesApplied"/> carries the
/// pair's <see cref="Matching.ProcedurePair.EquivalencesApplied"/> through to the report (ADR 0020; ticket M3-009).
/// <see cref="AssumedCallees"/> are the matched callee pairs the verdict assumed equivalent, sorted, and
/// <see cref="UnprovenAssumptions"/> the ones among them whose own result in the run is not Equivalent (ADR 0019; ticket M3-015).
/// Neither is part of the result's fingerprint.
/// </summary>
public sealed record VerificationResult(ProcedureIdentity Identity, Verdict Verdict)
{
    public ImmutableArray<string> EquivalencesApplied { get; init; } = [];

    public ImmutableArray<string> AssumedCallees { get; init; } = [];

    public ImmutableArray<string> UnprovenAssumptions { get; init; } = [];
}
