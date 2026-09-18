using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>A basic block: straight-line instructions then one terminator.</summary>
public sealed record IrBlock(IrBlockId Id, ImmutableArray<IrInstruction> Instructions, IrTerminator Terminator)
{
    public bool Equals(IrBlock? other) =>
        other is not null
        && (Id == other.Id)
            & (Terminator == other.Terminator)
            & IrEquality.SequenceEqual(Instructions, other.Instructions);

    public override int GetHashCode() => HashCode.Combine(Id, Terminator, IrEquality.Hash(Instructions));
}
