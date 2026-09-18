using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>Assume false: no execution reaches this point. Produced by loop unrolling, never by the frontend.</summary>
public sealed record IrUnreachable : IrTerminator
{
    internal override ImmutableArray<IrVar> Uses() => [];

    internal override ImmutableArray<IrBlockId> Successors() => [];

    internal override TResult Accept<TResult>(IrTerminatorVisitor<TResult> visitor) => visitor.Visit(this);
}
