using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// Something the frontend could not lower; poisons every dependent value. <see cref="WholeBody"/> marks one that stands
/// for the whole method because a construct in it could not be lowered at all; its <see cref="Span"/> is still that
/// construct's (ADR 0029 decision 3). Such an Unknown is <c>method</c>-scoped (decision 4).
/// </summary>
public sealed record IrOpaque(IrVar? Target, string Reason, SourceSpan Span) : IrInstruction
{
    public bool WholeBody { get; init; }

    internal override ImmutableArray<IrVar> Definitions() => [.. new[] { Target }.OfType<IrVar>()];

    internal override ImmutableArray<IrVar> Uses() => [];

    internal override TResult Accept<TResult>(IIrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
