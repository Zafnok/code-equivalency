using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>Opaque call, appended to the observable call trace. <paramref name="Threw"/> is a Bool output.</summary>
public sealed record IrCall(IrVar? Target, IrVar? Threw, CallIdentity Callee, ImmutableArray<IrVar> Args) : IrInstruction
{
    public bool Equals(IrCall? other) =>
        other is not null
        && (Target == other.Target)
            & (Threw == other.Threw)
            & (Callee == other.Callee)
            & IrEquality.SequenceEqual(Args, other.Args);

    public override int GetHashCode() => HashCode.Combine(Target, Threw, Callee, IrEquality.Hash(Args));

    internal override ImmutableArray<IrVar> Definitions() => [.. new[] { Target, Threw }.OfType<IrVar>()];

    internal override ImmutableArray<IrVar> Uses() => Args;

    internal override TResult Accept<TResult>(IrInstructionVisitor<TResult> visitor) => visitor.Visit(this);
}
