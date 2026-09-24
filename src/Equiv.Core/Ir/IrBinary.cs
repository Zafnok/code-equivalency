using System.Collections.Immutable;

namespace Equiv.Core.Ir;

public sealed record IrBinary(IrVar Target, IrBinaryOp Op, IrVar A, IrVar B) : IrInstruction
{
    internal override ImmutableArray<IrVar> Definitions() => [Target];

    internal override ImmutableArray<IrVar> Uses() => [A, B];

    internal override TResult Accept<TResult>(IIrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
