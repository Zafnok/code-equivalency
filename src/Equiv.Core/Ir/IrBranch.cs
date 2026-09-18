using System.Collections.Immutable;

namespace Equiv.Core.Ir;

public sealed record IrBranch(IrVar Cond, IrBlockId Then, IrBlockId Else) : IrTerminator
{
    internal override ImmutableArray<IrVar> Uses() => [Cond];

    internal override ImmutableArray<IrBlockId> Successors() => [Then, Else];

    internal override TResult Accept<TResult>(IrTerminatorVisitor<TResult> visitor) => visitor.Visit(this);
}
