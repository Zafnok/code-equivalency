using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// The observables of one run: outcome, final by-ref parameter values in declaration order
/// (empty unless the run returned or threw), and the call trace.
/// </summary>
public sealed record IrRun(IrOutcome Outcome, ImmutableArray<IrValue> Outs, ImmutableArray<IrCallRecord> Trace)
{
    public bool Equals(IrRun? other) =>
        other is not null
        && (Outcome == other.Outcome)
            & IrEquality.SequenceEqual(Outs, other.Outs)
            & IrEquality.SequenceEqual(Trace, other.Trace);

    public override int GetHashCode() => HashCode.Combine(Outcome, IrEquality.Hash(Outs), IrEquality.Hash(Trace));
}
