using System.Collections.Immutable;

namespace Equiv.Core.Ir;

public sealed record IrGoto(IrBlockId Target) : IrTerminator
{
    internal override ImmutableArray<IrVar> Uses() => [];

    internal override ImmutableArray<IrBlockId> Successors() => [Target];

    internal override TResult Accept<TResult>(IrTerminatorVisitor<TResult> visitor) => visitor.Visit(this);
}
