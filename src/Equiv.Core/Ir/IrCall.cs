using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// Opaque call, appended to the observable call trace. <paramref name="Threw"/> is a Bool output. <see cref="Heap"/>
/// names each heap map the call reads and writes (ticket P1-005): its <see cref="IrHeapPair.Before"/> is a use and its
/// <see cref="IrHeapPair.After"/> a definition.
/// </summary>
public sealed record IrCall(IrVar? Target, IrVar? Threw, CallIdentity Callee, ImmutableArray<IrVar> Args) : IrInstruction
{
    public ImmutableArray<IrHeapPair> Heap { get; init; } = [];

    public bool Equals(IrCall? other) =>
        other is not null
        && (Target == other.Target)
            & (Threw == other.Threw)
            & (Callee == other.Callee)
            & IrEquality.SequenceEqual(Args, other.Args)
            & IrEquality.SequenceEqual(Heap, other.Heap);

    public override int GetHashCode() => HashCode.Combine(Target, Threw, Callee, IrEquality.Hash(Args), IrEquality.Hash(Heap));

    internal override ImmutableArray<IrVar> Definitions() => [.. new[] { Target, Threw }.OfType<IrVar>(), .. Heap.Select(static h => h.After)];

    internal override ImmutableArray<IrVar> Uses() => [.. Args, .. Heap.Select(static h => h.Before)];

    internal override TResult Accept<TResult>(IIrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
