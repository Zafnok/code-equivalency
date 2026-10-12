using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;

using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// Which calls the replay taints because they are closed (ADR 0056; ticket P2-081), and when an open call one side makes
/// is a real difference although the other side's path is tainted by them.
/// </summary>
internal static class ClosedCallTaint
{
    /// <summary>
    /// Whether <paramref name="callee"/> is a closed call the replay of <paramref name="side"/> taints: the model chose its
    /// result, <c>threw</c> flag and outputs, and the real member may give none of them, so a divergence that needs them is
    /// a candidate, not a witness. A <see cref="CallIdentity.RuntimeChanged"/> member is not tainted: that its two
    /// runtimes answer differently is a row of the runtime-changes table, which is what an EQ006 reports.
    /// </summary>
    public static bool Taints(TraceEncoder calls, Side side, CallIdentity callee) => !callee.RuntimeChanged && calls.IsClosed(side, callee);

    /// <summary>
    /// Whether <paramref name="run"/> has an untainted open event at <paramref name="position"/> that no open call of
    /// <paramref name="other"/>, the other side, can equal: none of its procedure's calls its callee. A shared fragment's
    /// call (<see cref="ModelDecoder.IsAbstraction"/>) stands for calls the replay does not see, so a procedure that has
    /// one can. This only undoes what tainting closed calls costs: when <paramref name="other"/>'s run reached any other
    /// abstraction, a pure function or a shared fragment, the difference stays as that taint leaves it.
    /// <paramref name="open"/> are the indices of the run's open events, and <paramref name="trace"/> is its trace under
    /// the identities both sides use.
    /// </summary>
    public static bool Unmatched(IrRun run, ImmutableArray<int> open, ImmutableArray<IrCallRecord> trace, int position, ModelDecoder.Replayed other, Side otherSide, TraceEncoder calls) =>
        position < open.Length
        && !run.EventTainted(open[position])
        && other.Run.Taint.Sources.All(s => Taints(calls, otherSide, s))
        && !other.Procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>().Any(c =>
            ModelDecoder.IsAbstraction(c.Callee)
            || (!Taints(calls, otherSide, c.Callee) && string.Equals(calls.Canonical(otherSide, c.Callee), trace[open[position]].Callee.Value, StringComparison.Ordinal)));
}
