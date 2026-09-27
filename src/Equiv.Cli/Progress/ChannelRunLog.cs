using System.Threading.Channels;

using Equiv.Core.Progress;

namespace Equiv.Cli.Progress;

/// <summary>
/// The CLI's <see cref="IRunLog"/> (ADR 0038). A producer call stamps a <see cref="TimeProvider"/> timestamp, swaps in a
/// new <see cref="Snapshot"/> of the current phase and item with <see cref="Interlocked.Exchange{T}(ref T, T)"/>, and
/// does one <see cref="ChannelWriter{T}.TryWrite"/> to a bounded channel that drops (and counts) what does not fit. It
/// never awaits and never locks, so the pipeline never waits for the log. One consumer task turns events into
/// <see cref="RunLogLine"/>s on <paramref name="error"/> and, with <c>--log</c>, <paramref name="file"/>. The same task
/// wakes on a <see cref="PeriodicTimer"/> and writes a heartbeat from the snapshot, so an item that never finishes is
/// still named, with how long it has run and <c>slow</c> once that is ten times the phase's median item.
/// </summary>
internal sealed class ChannelRunLog : IRunLog, IDisposable
{
    public const int Capacity = 4096;

    /// <summary>How long <see cref="Dispose"/> waits for the writer to drain the channel.</summary>
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private const int SlowFactor = 10;

    private const long Buckets = 20;

    private readonly Verbosity verbosity;
    // The caller owns the writers; the log only writes to them.
    private readonly TextWriter[] outputs;
    private readonly TimeProvider time;
    private readonly long started;
    private readonly Channel<RunEvent> channel;
    private readonly Task consumer;
    private long dropped;
    private Snapshot snapshot;

    // Only the consumer task reads and writes this.
    private PhaseState phase;

    public ChannelRunLog(Verbosity verbosity, TextWriter error, TextWriter? file, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(time);

        this.verbosity = verbosity;
        outputs = file is null ? [error] : [error, file];
        this.time = time;
        started = time.GetTimestamp();
        snapshot = Snapshot.Idle(started);
        phase = new PhaseState(new RunEvent.PhaseStarted(started, string.Empty, 0, 0, Bound: null));
        channel = Channel.CreateBounded<RunEvent>(
            new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite },
            _ => Interlocked.Increment(ref dropped));

        // Runs synchronously up to its first await, so the timer exists, and its first heartbeat is one period away, when
        // the constructor returns.
        consumer = ConsumeAsync();
    }

    public bool IsDebug => verbosity == Verbosity.Debug;

    /// <summary>Events waiting for the writer; a test waits for 0.</summary>
    internal int Queued => channel.Reader.Count;

    public void Phase(string name, int total, long totalWeight, PhaseBound? bound = null)
    {
        long now = time.GetTimestamp();
        Publish(new Snapshot(name, total, totalWeight, bound, now, Done: 0, DoneWeight: 0, Item: null, ItemStart: now, ItemWeight: 0));
        channel.Writer.TryWrite(new RunEvent.PhaseStarted(now, name, total, totalWeight, bound));
    }

    public void Item(string identity, long weight)
    {
        long now = time.GetTimestamp();
        Publish(snapshot with { Item = identity, ItemStart = now, ItemWeight = weight });
        channel.Writer.TryWrite(new RunEvent.ItemStarted(now, identity, weight));
    }

    public void ItemDone(string outcome)
    {
        long now = time.GetTimestamp();
        Publish(snapshot with { Done = snapshot.Done + 1, DoneWeight = snapshot.DoneWeight + snapshot.ItemWeight, Item = null });
        channel.Writer.TryWrite(new RunEvent.ItemFinished(now, outcome));
    }

    public void Detail(string text)
    {
        if (IsDebug)
        {
            channel.Writer.TryWrite(new RunEvent.DetailWritten(time.GetTimestamp(), text));
        }
    }

    public void PhaseDone()
    {
        long now = time.GetTimestamp();
        Publish(Snapshot.Idle(now));
        channel.Writer.TryWrite(new RunEvent.PhaseFinished(now));
    }

    /// <summary>
    /// Completes the channel and waits at most <see cref="DrainTimeout"/> for the writer to finish. A writer stuck on its
    /// output is left behind: the run's result does not wait for its progress lines.
    /// </summary>
    public void Dispose()
    {
        channel.Writer.TryComplete();
        _ = consumer.Wait(DrainTimeout);
    }

    private static TimeSpan? Worst(PhaseBound? bound, int total, int done) =>
        bound is null ? null : EtaEstimator.WorstCase(Math.Min(bound.SolverItems, total - done), bound.TimeoutMs, bound.Rungs);

    private void Publish(Snapshot next) => Interlocked.Exchange(ref snapshot, next);

    private async Task ConsumeAsync()
    {
        using PeriodicTimer timer = new(IsDebug ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(60), time);
        Task<bool> read = channel.Reader.WaitToReadAsync().AsTask();
        Task<bool> tick = timer.WaitForNextTickAsync().AsTask();
        while (true)
        {
            // The timer is disposed only after this loop, so every tick is a real one.
            if (await Task.WhenAny(read, tick).ConfigureAwait(false) == tick)
            {
                Heartbeat();
                tick = timer.WaitForNextTickAsync().AsTask();
                continue;
            }

            if (!await read.ConfigureAwait(false))
            {
                return;
            }

            while (channel.Reader.TryRead(out RunEvent? next))
            {
                Handle(next);
            }

            read = channel.Reader.WaitToReadAsync().AsTask();
        }
    }

    private void Handle(RunEvent next)
    {
        switch (next)
        {
            case RunEvent.PhaseStarted started:
                phase = new PhaseState(started);
                Write(Verbosity.Normal, () => Line(started.Timestamp, phase.Started.Timestamp).Format());
                break;
            case RunEvent.ItemStarted item:
                phase.Item = item;
                break;
            case RunEvent.ItemFinished finished:
                Finish(finished);
                break;
            case RunEvent.DetailWritten detail:
                Write(Verbosity.Debug, () => RunLogLine.Detail(At(detail.Timestamp), phase.Started.Name, detail.Text));
                break;
            default:
                Write(Verbosity.Normal, () => RunLogLine.PhaseEnd(At(next.Timestamp), phase.Started.Name, time.GetElapsedTime(phase.Started.Timestamp, next.Timestamp), phase.Estimator, Interlocked.Read(ref dropped)));
                break;
        }
    }

    private void Finish(RunEvent.ItemFinished finished)
    {
        TimeSpan took = time.GetElapsedTime(phase.Item.Timestamp, finished.Timestamp);
        phase.Done++;
        phase.DoneWeight += phase.Item.Weight;
        phase.Durations.Add(took);
        TimeSpan? eta = phase.Estimator.Observe(time.GetElapsedTime(phase.Started.Timestamp, finished.Timestamp), phase.DoneWeight, phase.Done, Worst(phase.Started.Bound, phase.Started.Total, phase.Done));
        long bucket = phase.Started.TotalWeight <= 0 ? Buckets : phase.DoneWeight * Buckets / phase.Started.TotalWeight;
        if (IsDebug)
        {
            Write(Verbosity.Debug, () => (Line(finished.Timestamp, phase.Started.Timestamp) with { Item = phase.Item.Identity, Outcome = finished.Outcome, Took = took, Eta = eta }).Format());
        }
        else if (bucket > phase.Bucket)
        {
            Write(Verbosity.Normal, () => (Line(finished.Timestamp, phase.Started.Timestamp) with { Eta = eta }).Format());
        }

        phase.Bucket = bucket;
    }

    /// <summary>A line from the snapshot, not from the events, so it is current even while the producer is stuck in one item.</summary>
    private void Heartbeat()
    {
        Snapshot now = Volatile.Read(ref snapshot);
        if (now.Phase.Length == 0)
        {
            return;
        }

        long at = time.GetTimestamp();
        TimeSpan elapsed = time.GetElapsedTime(now.PhaseStart, at);
        TimeSpan? took = now.Item is null ? null : time.GetElapsedTime(now.ItemStart, at);
        TimeSpan? worst = Worst(now.Bound, now.Total, now.Done);
        bool slow = took is { } running
            && string.Equals(now.Phase, phase.Started.Name, StringComparison.Ordinal)
            && phase.Durations.Count > 0
            && running > SlowFactor * Median(phase.Durations);
        Write(Verbosity.Normal, () => new RunLogLine(At(at), now.Phase, now.Done, now.Total, now.DoneWeight, now.TotalWeight)
        {
            Item = now.Item,
            Took = took,
            Eta = EtaEstimator.Estimate(elapsed, now.DoneWeight, now.TotalWeight, now.Done, worst),
            Worst = worst,
            Rate = Rate(now.Done, elapsed),
            Slow = slow,
        }.Format());
    }

    private static TimeSpan Median(List<TimeSpan> durations)
    {
        List<TimeSpan> sorted = [.. durations.Order()];
        return sorted[sorted.Count / 2];
    }

    private static double? Rate(int done, TimeSpan elapsed) => elapsed > TimeSpan.Zero ? done / elapsed.TotalSeconds : null;

    private RunLogLine Line(long at, long phaseStart)
    {
        TimeSpan elapsed = time.GetElapsedTime(phaseStart, at);
        return new RunLogLine(At(at), phase.Started.Name, phase.Done, phase.Started.Total, phase.DoneWeight, phase.Started.TotalWeight)
        {
            Worst = Worst(phase.Started.Bound, phase.Started.Total, phase.Done),
            Rate = Rate(phase.Done, elapsed),
        };
    }

    private TimeSpan At(long timestamp) => time.GetElapsedTime(started, timestamp);

    private void Write(Verbosity level, Func<string> line)
    {
        if (verbosity < level)
        {
            return;
        }

        string text = line();
        foreach (TextWriter output in outputs)
        {
            output.WriteLine(text);
        }
    }

    /// <summary>What the producer last said: the phase, its counts, and the item it is in, if any.</summary>
    private sealed record Snapshot(string Phase, int Total, long TotalWeight, PhaseBound? Bound, long PhaseStart, int Done, long DoneWeight, string? Item, long ItemStart, long ItemWeight)
    {
        public static Snapshot Idle(long now) => new(string.Empty, 0, 0, Bound: null, now, Done: 0, DoneWeight: 0, Item: null, ItemStart: now, ItemWeight: 0);
    }

    /// <summary>The consumer's view of the phase it is writing: counts, item times, the estimator, and the last 5% bucket written.</summary>
    private sealed class PhaseState(RunEvent.PhaseStarted started)
    {
        public RunEvent.PhaseStarted Started { get; } = started;

        public EtaEstimator Estimator { get; } = new(started.TotalWeight);

        public List<TimeSpan> Durations { get; } = [];

        public RunEvent.ItemStarted Item { get; set; } = new(started.Timestamp, string.Empty, 0);

        public int Done { get; set; }

        public long DoneWeight { get; set; }

        public long Bucket { get; set; }
    }
}
