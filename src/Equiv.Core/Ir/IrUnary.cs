using System.Collections.Immutable;

namespace Equiv.Core.Ir;

public sealed record IrUnary(IrVar Target, IrUnaryOp Op, IrVar A) : IrInstruction
{
    internal override ImmutableArray<IrVar> Definitions() => [Target];

    internal override ImmutableArray<IrVar> Uses() => [A];

    internal override TResult Accept<TResult>(IIrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
