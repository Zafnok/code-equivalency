namespace Equiv.Cli.Tests.Progress;

/// <summary>
/// A clock that moves only when a test calls <see cref="Advance"/>, with timers that fire during that call. A timer due
/// more than once in one advance fires once, as a <see cref="PeriodicTimer"/> coalesces missed ticks anyway.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock gate = new();
    private readonly List<ManualTimer> timers = [];
    private long now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (gate)
        {
            return now;
        }
    }

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(this, callback, state);
        lock (gate)
        {
            timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (gate)
        {
            now += by.Ticks;
            due = [.. timers.Where(t => t.Due is { } at && at <= now)];
            foreach (ManualTimer timer in due)
            {
                timer.Due = timer.Period is { } period ? now + period : null;
            }
        }

        foreach (ManualTimer timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public long? Due { get; set; }

        public long? Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.now + dueTime.Ticks;
                Period = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : period.Ticks;
            }

            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner.gate)
            {
                owner.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
