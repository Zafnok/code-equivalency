using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>One entry of the observable call trace. <see cref="Heap"/> is the heap the call read (ticket P1-005).</summary>
public sealed record IrCallRecord(CallIdentity Callee, ImmutableArray<IrValue> Arguments)
{
    public ImmutableArray<IrHeapSlice> Heap { get; init; } = [];

    public bool Equals(IrCallRecord? other) =>
        other is not null
        && (Callee == other.Callee) & IrEquality.SequenceEqual(Arguments, other.Arguments) & IrEquality.SequenceEqual(Heap, other.Heap);

    public override int GetHashCode() => HashCode.Combine(Callee, IrEquality.Hash(Arguments), IrEquality.Hash(Heap));
}
