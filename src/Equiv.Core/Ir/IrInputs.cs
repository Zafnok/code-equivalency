using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>Arguments for <see cref="IrInterpreter.Run"/>, one per parameter in declaration order.</summary>
public sealed record IrInputs(ImmutableArray<IrValue> Arguments)
{
    public bool Equals(IrInputs? other) => other is not null && IrEquality.SequenceEqual(Arguments, other.Arguments);

    public override int GetHashCode() => IrEquality.Hash(Arguments);
}
