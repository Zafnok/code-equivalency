using Equiv.Core.Verdicts;

namespace Equiv.Core.Reporting;

/// <summary>
/// One row of a report: the verdict reached for a single procedure identity. This is the
/// hand-off point between matching/verification and reporting (VERIFICATION-MODEL.md section 6);
/// assembling a list of these from a <see cref="Matching.MatchResult"/> plus
/// <see cref="IVerificationBackend"/> runs (including what an <see cref="Matching.MatchResult.Ambiguous"/>
/// identity becomes) is CLI-orchestration, out of scope for this ticket.
/// </summary>
public sealed record VerificationResult(ProcedureIdentity Identity, Verdict Verdict);
