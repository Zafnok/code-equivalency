using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// The observables of one run: outcome, final by-ref parameter values in declaration order
/// (empty unless the run returned or threw), and the call trace. <see cref="Taint"/> says which of them depend on an
/// abstraction (ADR 0026); it is <see cref="IrTaint.None"/> for a run without a taint predicate.
/// </summary>
public sealed record IrRun(IrOutcome Outcome, ImmutableArray<IrValue> Outs, ImmutableArray<IrCallRecord> Trace)
{
    public IrTaint Taint { get; init; } = IrTaint.None;

    /// <summary>Whether the final value <see cref="Outs"/>[<paramref name="index"/>] is tainted.</summary>
    public bool OutTainted(int index) => Taint.Outs.Contains(index);

    /// <summary>
    /// Whether the call event at <paramref name="index"/> is tainted. Past the end of the trace it is whether the run
    /// having no event there is: that follows the path, so it is <see cref="IrTaint.Outcome"/>.
    /// </summary>
    public bool EventTainted(int index) => index < Trace.Length ? Taint.Trace.Contains(index) : Taint.Outcome;

    public bool Equals(IrRun? other) =>
        other is not null
        && (Outcome == other.Outcome)
            & IrEquality.SequenceEqual(Outs, other.Outs)
            & IrEquality.SequenceEqual(Trace, other.Trace)
            & (Taint == other.Taint);

    public override int GetHashCode() => HashCode.Combine(Outcome, IrEquality.Hash(Outs), IrEquality.Hash(Trace), Taint);
}
