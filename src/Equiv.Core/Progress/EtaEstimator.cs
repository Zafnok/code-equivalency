namespace Equiv.Core.Progress;

/// <summary>
/// ADR 0038's estimate of a phase's remaining time: the time per unit of weight so far, times the weight left. It never
/// reads a clock: the caller measures <c>elapsed</c> with <see cref="TimeProvider"/> timestamps. An instance follows one
/// phase of <paramref name="totalWeight"/> and keeps the estimate it gave when 25%, 50% and 75% of the weight was done,
/// so the phase's end can print them beside the actual time.
/// </summary>
public sealed class EtaEstimator(long totalWeight)
{
    /// <summary>No estimate before this percentage of the weight is done...</summary>
    public const long MinPercent = 5;

    /// <summary>...unless this many items are done.</summary>
    public const int MinItems = 20;

    private readonly TimeSpan?[] checkpoints = new TimeSpan?[3];

    /// <summary>The estimate given when 25% of the weight was done, or null if none was.</summary>
    public TimeSpan? At25 => checkpoints[0];

    /// <summary>The estimate given when 50% of the weight was done, or null if none was.</summary>
    public TimeSpan? At50 => checkpoints[1];

    /// <summary>The estimate given when 75% of the weight was done, or null if none was.</summary>
    public TimeSpan? At75 => checkpoints[2];

    /// <summary>
    /// <paramref name="elapsed"/> ÷ <paramref name="doneWeight"/> × the weight left, never more than <paramref name="ceiling"/>
    /// or <see cref="TimeSpan.MaxValue"/>;
    /// null until <see cref="MinPercent"/>% of <paramref name="totalWeight"/> or <see cref="MinItems"/> items are done.
    /// </summary>
    public static TimeSpan? Estimate(TimeSpan elapsed, long doneWeight, long totalWeight, int doneItems, TimeSpan? ceiling = null)
    {
        if (doneWeight <= 0 || (doneWeight * 100 < totalWeight * MinPercent && doneItems < MinItems))
        {
            return null;
        }

        long remaining = Math.Max(0, totalWeight - doneWeight);
        // A long phase with a sliver of its weight done can overshoot TimeSpan's range; saturate rather than wrap (M4-016).
        TimeSpan estimate = TimeSpan.FromTicks((long)Int128.Min((Int128)elapsed.Ticks * remaining / doneWeight, long.MaxValue));
        return ceiling is { } bound && estimate > bound ? bound : estimate;
    }

    /// <summary>The longest <paramref name="remainingSolverPairs"/> pairs can take: each rung of each pair runs to its timeout.</summary>
    public static TimeSpan WorstCase(int remainingSolverPairs, int timeoutMs, int rungs) =>
        TimeSpan.FromTicks((long)remainingSolverPairs * timeoutMs * rungs * TimeSpan.TicksPerMillisecond);

    /// <summary><see cref="Estimate"/> for this phase, recording it at the first observation at or past each quarter of the weight.</summary>
    public TimeSpan? Observe(TimeSpan elapsed, long doneWeight, int doneItems, TimeSpan? ceiling = null)
    {
        TimeSpan? estimate = Estimate(elapsed, doneWeight, totalWeight, doneItems, ceiling);
        for (int quarter = 0; quarter < checkpoints.Length; quarter++)
        {
            if (doneWeight * 4 >= totalWeight * (quarter + 1))
            {
                checkpoints[quarter] ??= estimate;
            }
        }

        return estimate;
    }
}
