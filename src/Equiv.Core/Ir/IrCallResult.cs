using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// What an <see cref="ICallOracle"/> answers for one call. <see cref="Heap"/> is the new value of each heap slice the
/// call was given, in the same order; empty leaves every slice unchanged (ticket P1-005). <see cref="RefOuts"/> is the new
/// value of each <c>ref</c> or <c>out</c> argument the call writes, in parameter order (ticket M4-003).
/// </summary>
public sealed record IrCallResult(IrValue? Value, bool Threw)
{
    public ImmutableArray<IrValue> RefOuts { get; init; } = [];

    public ImmutableArray<IrValue> Heap { get; init; } = [];

    public bool Equals(IrCallResult? other) =>
        other is not null
        && (Value == other.Value) & (Threw == other.Threw) & IrEquality.SequenceEqual(RefOuts, other.RefOuts) & IrEquality.SequenceEqual(Heap, other.Heap);

    public override int GetHashCode() => HashCode.Combine(Value, Threw, IrEquality.Hash(RefOuts), IrEquality.Hash(Heap));
}
