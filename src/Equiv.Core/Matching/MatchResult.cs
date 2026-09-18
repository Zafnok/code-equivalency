using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Matching;

/// <summary>
/// The result of <see cref="IProcedureMatcher.Match"/>. Every distinct identity present in either
/// input collection lands in exactly one of these four (ARCHITECTURE.md; VERIFICATION-MODEL.md section 4).
/// </summary>
public sealed record MatchResult(
    ImmutableArray<ProcedurePair> Pairs,
    ImmutableArray<ProcedureIdentity> Added,
    ImmutableArray<ProcedureIdentity> Removed,
    ImmutableArray<ProcedureIdentity> Ambiguous)
{
    public bool Equals(MatchResult? other) =>
        other is not null
        && IrEquality.SequenceEqual(Pairs, other.Pairs)
            & IrEquality.SequenceEqual(Added, other.Added)
            & IrEquality.SequenceEqual(Removed, other.Removed)
            & IrEquality.SequenceEqual(Ambiguous, other.Ambiguous);

    public override int GetHashCode() =>
        HashCode.Combine(IrEquality.Hash(Pairs), IrEquality.Hash(Added), IrEquality.Hash(Removed), IrEquality.Hash(Ambiguous));
}
