using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>Jumps to the first case whose value equals <paramref name="Scrutinee"/>, else to <paramref name="Default"/>.</summary>
public sealed record IrSwitch(IrVar Scrutinee, ImmutableArray<(IrValue Value, IrBlockId Target)> Cases, IrBlockId Default) : IrTerminator
{
    public bool Equals(IrSwitch? other) =>
        other is not null
        && (Scrutinee == other.Scrutinee)
            & (Default == other.Default)
            & IrEquality.SequenceEqual(Cases, other.Cases);

    public override int GetHashCode() => HashCode.Combine(Scrutinee, Default, IrEquality.Hash(Cases));

    internal override ImmutableArray<IrVar> Uses() => [Scrutinee];

    internal override ImmutableArray<IrBlockId> Successors() => [.. Cases.Select(static c => c.Target), Default];

    internal override TResult Accept<TResult>(IrTerminatorVisitor<TResult> visitor) => visitor.Visit(this);
}
