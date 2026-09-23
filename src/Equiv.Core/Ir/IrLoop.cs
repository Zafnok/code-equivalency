using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// A natural loop (ticket M3-002): its <paramref name="Header"/>, its <paramref name="Blocks"/> in reverse
/// postorder (header first), the <paramref name="Latches"/> whose back edges close it, its
/// <paramref name="Exits"/> (edges from a loop block to a block outside, in reverse postorder of the source),
/// and the header of the smallest loop that encloses it, if any.
/// </summary>
public sealed record IrLoop(
    IrBlockId Header,
    ImmutableArray<IrBlockId> Blocks,
    ImmutableArray<IrBlockId> Latches,
    ImmutableArray<(IrBlockId From, IrBlockId To)> Exits,
    IrBlockId? Parent)
{
    public bool Equals(IrLoop? other) =>
        other is not null
        && (Header == other.Header)
            & (Parent == other.Parent)
            & IrEquality.SequenceEqual(Blocks, other.Blocks)
            & IrEquality.SequenceEqual(Latches, other.Latches)
            & IrEquality.SequenceEqual(Exits, other.Exits);

    public override int GetHashCode() =>
        HashCode.Combine(Header, Parent, IrEquality.Hash(Blocks), IrEquality.Hash(Latches), IrEquality.Hash(Exits));
}
