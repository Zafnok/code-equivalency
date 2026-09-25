using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// Applies the catalogued pure function <paramref name="Function"/> (<c>f64.add</c>, <c>dec.mul</c>, <c>op:&lt;identity&gt;</c>)
/// to <paramref name="Args"/> (ADR 0025; ticket M4-002). It adds no trace event and depends on neither the heap nor its
/// position, so both sides share it: equal arguments give equal results. Each of <paramref name="Throws"/> is a Bool
/// output. <see cref="RuntimeSensitive"/> marks a function whose behaviour differs between .NET Framework and .NET, which
/// the backend never shares between the sides.
/// </summary>
public sealed record IrPure(IrVar Target, ImmutableArray<IrPureThrow> Throws, string Function, ImmutableArray<IrVar> Args) : IrInstruction
{
    public bool RuntimeSensitive { get; init; }

    public bool Equals(IrPure? other) =>
        other is not null
        && (Target == other.Target)
            & IrEquality.SequenceEqual(Throws, other.Throws)
            & string.Equals(Function, other.Function, StringComparison.Ordinal)
            & IrEquality.SequenceEqual(Args, other.Args)
            & (RuntimeSensitive == other.RuntimeSensitive);

    public override int GetHashCode() => HashCode.Combine(Target, IrEquality.Hash(Throws), Function, IrEquality.Hash(Args), RuntimeSensitive);

    internal override ImmutableArray<IrVar> Definitions() => [Target, .. Throws.Select(static t => t.Flag)];

    internal override ImmutableArray<IrVar> Uses() => Args;

    internal override TResult Accept<TResult>(IIrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
