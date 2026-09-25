using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// Which observables of an <see cref="IrRun"/> depend on an abstraction (ADR 0026; ticket M3-016): a value the solver
/// may interpret freely, so a difference in it need not be real. <see cref="Outcome"/> is whether the run returned or
/// threw, and which exception, and it is also the taint of the trace past its last event: both follow the path, so
/// they are tainted once the run branched on a tainted condition. <see cref="Value"/> is the returned value's taint,
/// which includes the path's. <see cref="Outs"/> and <see cref="Trace"/> are the indices of the tainted final by-ref
/// values and call events. <see cref="Sources"/> are the tainting call identities the run reached, in first-reached
/// order.
/// </summary>
public sealed record IrTaint(bool Outcome, bool Value, ImmutableArray<int> Outs, ImmutableArray<int> Trace, ImmutableArray<CallIdentity> Sources)
{
    /// <summary>A run that depends on no abstraction, which is every run without a taint predicate.</summary>
    public static IrTaint None { get; } = new(Outcome: false, Value: false, [], [], []);

    public bool Equals(IrTaint? other) =>
        other is not null
        && (Outcome == other.Outcome)
            & (Value == other.Value)
            & IrEquality.SequenceEqual(Outs, other.Outs)
            & IrEquality.SequenceEqual(Trace, other.Trace)
            & IrEquality.SequenceEqual(Sources, other.Sources);

    public override int GetHashCode() => HashCode.Combine(Outcome, Value, IrEquality.Hash(Outs), IrEquality.Hash(Trace), IrEquality.Hash(Sources));
}
