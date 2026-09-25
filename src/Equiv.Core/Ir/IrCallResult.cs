using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// What an <see cref="ICallOracle"/> answers for one call. <see cref="Heap"/> is the new value of each heap slice the
/// call was given, in the same order; empty leaves every slice unchanged (ticket P1-005).
/// </summary>
public sealed record IrCallResult(IrValue? Value, bool Threw)
{
    public ImmutableArray<IrValue> Heap { get; init; } = [];

    public bool Equals(IrCallResult? other) =>
        other is not null
        && (Value == other.Value) & (Threw == other.Threw) & IrEquality.SequenceEqual(Heap, other.Heap);

    public override int GetHashCode() => HashCode.Combine(Value, Threw, IrEquality.Hash(Heap));
}
