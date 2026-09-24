using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>SSA merge: takes the value paired with the block control arrived from.</summary>
public sealed record IrPhi(IrVar Target, ImmutableArray<(IrBlockId From, IrVar Value)> Incoming) : IrInstruction
{
    public bool Equals(IrPhi? other) =>
        other is not null
        && (Target == other.Target) & IrEquality.SequenceEqual(Incoming, other.Incoming);

    public override int GetHashCode() => HashCode.Combine(Target, IrEquality.Hash(Incoming));

    internal override ImmutableArray<IrVar> Definitions() => [Target];

    /// <summary>None inside the block: each incoming value is used at the end of its predecessor.</summary>
    internal override ImmutableArray<IrVar> Uses() => [];

    internal override TResult Accept<TResult>(IIrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
