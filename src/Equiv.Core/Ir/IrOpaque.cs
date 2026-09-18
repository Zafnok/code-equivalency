using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>Something the frontend could not lower; poisons every dependent value.</summary>
public sealed record IrOpaque(IrVar? Target, string Reason, SourceSpan Span) : IrInstruction
{
    internal override ImmutableArray<IrVar> Definitions() => [.. new[] { Target }.OfType<IrVar>()];

    internal override ImmutableArray<IrVar> Uses() => [];

    internal override TResult Accept<TResult>(IrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
