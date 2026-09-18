using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>Exceptional exit. By-ref writes before a throw are visible to the caller, hence <paramref name="Outs"/>.</summary>
public sealed record IrThrow(string ExceptionType, ImmutableArray<IrOut> Outs) : IrTerminator
{
    public bool Equals(IrThrow? other) =>
        other is not null
        && string.Equals(ExceptionType, other.ExceptionType, StringComparison.Ordinal) & IrEquality.SequenceEqual(Outs, other.Outs);

    public override int GetHashCode() => HashCode.Combine(ExceptionType, IrEquality.Hash(Outs));

    internal override ImmutableArray<IrVar> Uses() => [.. Outs.Select(static o => o.Final)];

    internal override ImmutableArray<IrBlockId> Successors() => [];

    internal override TResult Accept<TResult>(IrTerminatorVisitor<TResult> visitor) => visitor.Visit(this);
}
