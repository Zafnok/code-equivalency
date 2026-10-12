using System.Collections.Immutable;

using Equiv.Core.Ir;

using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// The two replayed traces compared by their open calls alone (ADR 0056; ticket P2-081). A closed call's event is
/// tainted, but an open call one side makes and the other does not is an observable whatever the closed calls between
/// them answer.
/// </summary>
internal static class OpenCallComparison
{
    /// <summary>
    /// The taint of the first difference between the two sides' open calls, their closed events left out, or nothing when
    /// their open calls agree. <paramref name="oldTrace"/> and <paramref name="newTrace"/> are the runs' traces under the
    /// identities both sides use.
    /// </summary>
    public static IEnumerable<bool> Tainted(ModelDecoder.Replayed old, ImmutableArray<IrCallRecord> oldTrace, ModelDecoder.Replayed @new, ImmutableArray<IrCallRecord> newTrace, TraceEncoder calls)
    {
        ImmutableArray<int> oldOpen = Open(old.Run, Side.Old, calls);
        ImmutableArray<int> newOpen = Open(@new.Run, Side.New, calls);
        int both = Math.Min(oldOpen.Length, newOpen.Length);
        int open = Enumerable.Range(0, both).Where(i => oldTrace[oldOpen[i]] != newTrace[newOpen[i]]).DefaultIfEmpty(both).First();
        return open == both && oldOpen.Length == newOpen.Length
            ? []
            :
            [
                (EventTainted(old.Run, oldOpen, open) || EventTainted(@new.Run, newOpen, open))
                    && !ClosedCallTaint.Unmatched(old.Run, oldOpen, oldTrace, open, @new, Side.New, calls)
                    && !ClosedCallTaint.Unmatched(@new.Run, newOpen, newTrace, open, old, Side.Old, calls),
            ];
    }

    /// <summary>The indices of <paramref name="run"/>'s events that are open calls, in order.</summary>
    private static ImmutableArray<int> Open(IrRun run, Side side, TraceEncoder calls) =>
        [.. Enumerable.Range(0, run.Trace.Length).Where(i => !ClosedCallTaint.Taints(calls, side, run.Trace[i].Callee))];

    /// <summary>
    /// Whether the <paramref name="position"/>-th open event of <paramref name="run"/> is tainted. Past the last one it is
    /// whether the run having none there is, which follows the path.
    /// </summary>
    private static bool EventTainted(IrRun run, ImmutableArray<int> open, int position) =>
        position < open.Length ? run.EventTainted(open[position]) : run.Taint.Outcome;
}
