using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>Bool: would <paramref name="Op"/> applied to <paramref name="A"/> and <paramref name="B"/> overflow.</summary>
public sealed record IrOverflows(IrVar Target, IrOverflowOp Op, IrVar A, IrVar B) : IrInstruction
{
    internal override ImmutableArray<IrVar> Definitions() => [Target];

    internal override ImmutableArray<IrVar> Uses() => [A, B];

    internal override TResult Accept<TResult>(IrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
