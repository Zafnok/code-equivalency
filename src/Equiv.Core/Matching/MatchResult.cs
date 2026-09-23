using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Matching;

/// <summary>
/// The result of <see cref="IProcedureMatcher.Match"/>. Every distinct identity present in either
/// input collection lands in exactly one of these four (ARCHITECTURE.md; VERIFICATION-MODEL.md section 4).
/// <see cref="LegacySkipped"/> and <see cref="ModernSkipped"/> are the projects a frontend skipped on each side
/// (ADR 0029); their procedures are in none of the four.
/// </summary>
public sealed record MatchResult(
    ImmutableArray<ProcedurePair> Pairs,
    ImmutableArray<ProcedureIdentity> Added,
    ImmutableArray<ProcedureIdentity> Removed,
    ImmutableArray<ProcedureIdentity> Ambiguous)
{
    public ImmutableArray<UnverifiedProject> LegacySkipped { get; init; } = [];

    public ImmutableArray<UnverifiedProject> ModernSkipped { get; init; } = [];

    // Deliberate non-short-circuit '&': see the comment on Equiv.Core.Configuration.EquivConfig.Equals.
    public bool Equals(MatchResult? other) =>
        other is not null
        && IrEquality.SequenceEqual(Pairs, other.Pairs)
            & IrEquality.SequenceEqual(Added, other.Added) // NOSONAR
            & IrEquality.SequenceEqual(Removed, other.Removed) // NOSONAR
            & IrEquality.SequenceEqual(Ambiguous, other.Ambiguous) // NOSONAR
            & IrEquality.SequenceEqual(LegacySkipped, other.LegacySkipped) // NOSONAR
            & IrEquality.SequenceEqual(ModernSkipped, other.ModernSkipped); // NOSONAR

    public override int GetHashCode() =>
        HashCode.Combine(
            IrEquality.Hash(Pairs),
            IrEquality.Hash(Added),
            IrEquality.Hash(Removed),
            IrEquality.Hash(Ambiguous),
            IrEquality.Hash(LegacySkipped),
            IrEquality.Hash(ModernSkipped));
}
