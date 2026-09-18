using System.Collections.Immutable;

namespace Equiv.Core.Ir;

public sealed record IrConst(IrVar Target, IrValue Value) : IrInstruction
{
    internal override ImmutableArray<IrVar> Definitions() => [Target];

    internal override ImmutableArray<IrVar> Uses() => [];

    internal override TResult Accept<TResult>(IrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
