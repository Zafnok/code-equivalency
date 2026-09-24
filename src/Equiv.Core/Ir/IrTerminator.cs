using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>Ends a block.</summary>
public abstract record IrTerminator
{
    private protected IrTerminator()
    {
    }

    internal abstract ImmutableArray<IrVar> Uses();

    internal abstract ImmutableArray<IrBlockId> Successors();

    internal abstract TResult Accept<TResult>(IIrTerminatorVisitor<TResult> visitor);
}
