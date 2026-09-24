namespace Equiv.Core.Matching;

/// <summary>
/// A matched pair a frontend could not lower (ticket P2-011): lowering one of its bodies threw <paramref name="Exception"/>.
/// The pair is in no <see cref="MatchResult.Pairs"/>; like a pair whose verification throws (ADR 0023), it gets no
/// result, an <c>error</c> notification naming both identities, and its identity in <c>run.properties.unverified</c>.
/// </summary>
public sealed record LoweringFailure(ProcedureIdentity Old, ProcedureIdentity New, Exception Exception);
