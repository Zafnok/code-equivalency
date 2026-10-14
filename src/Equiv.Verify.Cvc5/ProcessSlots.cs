namespace Equiv.Verify.Cvc5;

/// <summary>
/// How many cvc5 processes split queries may run at once, shared by every query one <see cref="Cvc5Solver"/> splits
/// (ticket P1-039). <c>equiv compare</c> gives it the run's <c>jobs</c> (ticket P2-077), so the partitions of the pairs
/// being verified at once take their places from that number, not <c>n</c> more processes a pair. A slot is held only
/// while its process runs, and whoever holds one waits for nothing else, so no two waiters can block each other.
/// </summary>
internal sealed class ProcessSlots
{
    private readonly object gate = new();
    private int free;

    public ProcessSlots(int total)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        Total = total;
        free = total;
    }

    public int Total { get; }

    /// <summary>Takes a slot, waiting until one is free.</summary>
    public void Acquire()
    {
        lock (gate)
        {
            while (free == 0)
            {
                Monitor.Wait(gate);
            }

            free--;
        }
    }

    /// <summary>Gives back a slot <see cref="Acquire"/> took.</summary>
    public void Release()
    {
        lock (gate)
        {
            free++;
            Monitor.Pulse(gate);
        }
    }
}
