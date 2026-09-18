using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>Normal exit. <paramref name="Outs"/> lists every by-ref parameter once, in declaration order.</summary>
public sealed record IrReturn(IrVar? Value, ImmutableArray<IrOut> Outs) : IrTerminator
{
    public bool Equals(IrReturn? other) =>
        other is not null
        && (Value == other.Value) & IrEquality.SequenceEqual(Outs, other.Outs);

    public override int GetHashCode() => HashCode.Combine(Value, IrEquality.Hash(Outs));

    internal override ImmutableArray<IrVar> Uses() => [.. new[] { Value }.OfType<IrVar>(), .. Outs.Select(static o => o.Final)];

    internal override ImmutableArray<IrBlockId> Successors() => [];

    internal override TResult Accept<TResult>(IrTerminatorVisitor<TResult> visitor) => visitor.Visit(this);
}
