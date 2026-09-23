using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Verdicts;

/// <summary>
/// The result for one procedure (VERIFICATION-MODEL.md section 1; ARCHITECTURE.md). Closed:
/// <see cref="Equivalent"/>, <see cref="Divergent"/>, <see cref="Unknown"/>, <see cref="Added"/>,
/// <see cref="Removed"/>. Nothing else. <see cref="Ladder"/> lists every loop-ladder rung the backend
/// attempted, in order, with its outcome (VERIFICATION-MODEL.md section 5.1); it is empty for a verdict
/// no backend produced.
/// </summary>
public abstract record Verdict
{
    private protected Verdict()
    {
    }

    public ImmutableArray<LadderStep> Ladder { get; init; } = [];

    public virtual bool Equals(Verdict? other) =>
        other is not null && (EqualityContract == other.EqualityContract) & IrEquality.SequenceEqual(Ladder, other.Ladder);

    public override int GetHashCode() => HashCode.Combine(EqualityContract, IrEquality.Hash(Ladder));
}
