using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>What an <see cref="IPureOracle"/> answers for one <see cref="IrPure"/>: its value, and one flag per entry of its <see cref="IrPure.Throws"/>.</summary>
public sealed record IrPureResult(IrValue Value, ImmutableArray<bool> Threw)
{
    public bool Equals(IrPureResult? other) =>
        other is not null && (Value == other.Value) & IrEquality.SequenceEqual(Threw, other.Threw);

    public override int GetHashCode() => HashCode.Combine(Value, IrEquality.Hash(Threw));
}
