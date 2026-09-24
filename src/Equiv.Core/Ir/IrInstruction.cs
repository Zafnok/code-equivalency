using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>A non-terminator instruction. Instructions never throw; exception edges are explicit branches.</summary>
public abstract record IrInstruction
{
    private protected IrInstruction()
    {
    }

    /// <summary>Variables this instruction assigns.</summary>
    internal abstract ImmutableArray<IrVar> Definitions();

    /// <summary>Variables this instruction reads at its own position (a phi reads its operands on the incoming edges instead).</summary>
    internal abstract ImmutableArray<IrVar> Uses();

    internal abstract TResult Accept<TResult>(IIrInstructionVisitor<TResult> visitor);
}
