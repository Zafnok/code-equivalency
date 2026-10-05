namespace Equiv.Core.Progress;

/// <summary>
/// Where a run reports its progress (ADR 0038): a phase of <c>total</c> items weighing <c>totalWeight</c> together, each
/// item started and finished in turn, and free-text details at <c>debug</c>. Every call returns at once and never
/// changes a result; <see cref="NullRunLog"/> ignores them all. A caller builds a costly <see cref="Detail"/> string only
/// when <see cref="IsDebug"/> is true. A phase starts and ends on one thread. Its items may be in flight on several
/// threads at once, one item to a thread (ticket P2-077): <see cref="Item"/>, the <see cref="Detail"/>s about that item
/// and its <see cref="ItemDone"/> come from the thread that works on it, which is how a log tells the items apart.
/// </summary>
public interface IRunLog
{
    /// <summary>Whether <see cref="Detail"/> is written, so a caller can skip building its text.</summary>
    bool IsDebug { get; }

    /// <summary>
    /// Starts a phase of <paramref name="total"/> items weighing <paramref name="totalWeight"/>. <paramref name="bound"/>,
    /// when given, is what a worst case for the phase is computed from (<see cref="EtaEstimator.WorstCase"/>).
    /// </summary>
    void Phase(string name, int total, long totalWeight, PhaseBound? bound = null);

    /// <summary>Starts an item of the phase on the calling thread.</summary>
    void Item(string identity, long weight);

    /// <summary>Finishes the item <see cref="Item"/> started on the calling thread, with a one-word <paramref name="outcome"/>.</summary>
    void ItemDone(string outcome);

    /// <summary>A line written only at <c>debug</c>.</summary>
    void Detail(string text);

    /// <summary>Ends the phase <see cref="Phase"/> started.</summary>
    void PhaseDone();
}
