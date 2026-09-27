using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// Something the frontend could not lower; poisons every dependent value. <see cref="WholeBody"/> marks one that stands
/// for the whole method because a construct in it could not be lowered at all; its <see cref="Span"/> is still that
/// construct's (ADR 0029 decision 3). Such an Unknown is <c>method</c>-scoped (decision 4).
/// <para>
/// An expression-level fragment the frontend can fingerprint carries its bound <see cref="Fingerprint"/> and the variables
/// it <see cref="Reads"/> (ADR 0024 decision 2; ticket M4-004). When the same fingerprint occurs on both sides of a pair,
/// both occurrences are one call <c>opaque:&lt;fingerprint&gt;</c> over the reads, so the fragment also has what a call has:
/// a <see cref="Threw"/> flag the frontend branches on and a <see cref="Heap"/> pair per heap map (ticket P1-005). The reads
/// and each pair's <see cref="IrHeapPair.Before"/> are uses; the flag and each <see cref="IrHeapPair.After"/> definitions.
/// </para>
/// </summary>
public sealed record IrOpaque(IrVar? Target, string Reason, SourceSpan Span) : IrInstruction
{
    public bool WholeBody { get; init; }

    public string? Fingerprint { get; init; }

    public ImmutableArray<IrVar> Reads { get; init; } = [];

    public IrVar? Threw { get; init; }

    public ImmutableArray<IrHeapPair> Heap { get; init; } = [];

    public bool Equals(IrOpaque? other) =>
        other is not null
        && (Target == other.Target)
            & string.Equals(Reason, other.Reason, StringComparison.Ordinal)
            & (Span == other.Span)
            & (WholeBody == other.WholeBody)
            & string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal)
            & IrEquality.SequenceEqual(Reads, other.Reads)
            & (Threw == other.Threw)
            & IrEquality.SequenceEqual(Heap, other.Heap);

    public override int GetHashCode() =>
        HashCode.Combine(Target, Reason, Span, WholeBody, Fingerprint, IrEquality.Hash(Reads), Threw, IrEquality.Hash(Heap));

    internal override ImmutableArray<IrVar> Definitions() => [.. new[] { Target, Threw }.OfType<IrVar>(), .. Heap.Select(static h => h.After)];

    internal override ImmutableArray<IrVar> Uses() => [.. Reads, .. Heap.Select(static h => h.Before)];

    internal override TResult Accept<TResult>(IIrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
