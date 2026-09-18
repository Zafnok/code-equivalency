using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>SMT <c>store</c>: <paramref name="Target"/> is the new map version.</summary>
public sealed record IrMapWrite(IrVar Target, IrVar Map, IrVar Key, IrVar Value) : IrInstruction
{
    internal override ImmutableArray<IrVar> Definitions() => [Target];

    internal override ImmutableArray<IrVar> Uses() => [Map, Key, Value];

    internal override TResult Accept<TResult>(IrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
