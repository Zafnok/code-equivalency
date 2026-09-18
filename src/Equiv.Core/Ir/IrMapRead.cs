using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>SMT <c>select</c>.</summary>
public sealed record IrMapRead(IrVar Target, IrVar Map, IrVar Key) : IrInstruction
{
    internal override ImmutableArray<IrVar> Definitions() => [Target];

    internal override ImmutableArray<IrVar> Uses() => [Map, Key];

    internal override TResult Accept<TResult>(IrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
