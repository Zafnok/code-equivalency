using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>One entry of the observable call trace.</summary>
public sealed record IrCallRecord(CallIdentity Callee, ImmutableArray<IrValue> Arguments)
{
    public bool Equals(IrCallRecord? other) =>
        other is not null
        && (Callee == other.Callee) & IrEquality.SequenceEqual(Arguments, other.Arguments);

    public override int GetHashCode() => HashCode.Combine(Callee, IrEquality.Hash(Arguments));
}
