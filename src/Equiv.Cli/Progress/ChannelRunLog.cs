using System.Collections.Immutable;
using System.Threading.Channels;

using Equiv.Core.Progress;

namespace Equiv.Cli.Progress;

/// <summary>
/// The CLI's <see cref="IRunLog"/> (ADR 0038). A producer call stamps a <see cref="TimeProvider"/> timestamp, swaps in a
/// new <see cref="Snapshot"/> of the current phase and the items in flight with a compare-and-swap, and
/// does at most one <see cref="ChannelWriter{T}.TryWrite"/> to a bounded channel that drops (and counts) what does not fit. It
/// never awaits and never locks, so the pipeline never waits for the log. One consumer task turns events into
/// <see cref="RunLogLine"/>s on <paramref name="error"/> and, with <c>--log</c>, <paramref name="file"/>. The same task
/// wakes on a <see cref="PeriodicTimer"/> and writes a heartbeat from the snapshot, one line for each item in flight, so
/// an item that never finishes is still named, with how long it has run and <c>slow</c> once that is ten times the
/// phase's median item. Several threads may each have an item in flight (ticket P2-077): the log remembers each
/// thread's item, so an item's end and its details name the item the calling thread started.
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

    // The item the calling thread started and has not finished.
    private readonly ThreadLocal<InFlight?> current = new();
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
        Publish(new Snapshot(name, total, totalWeight, bound, now, Done: 0, DoneWeight: 0, []));
        channel.Writer.TryWrite(new RunEvent.PhaseStarted(now, name, total, totalWeight, bound));
    }

    public void Item(string identity, long weight)
    {
        InFlight item = new(time.GetTimestamp(), identity, weight);
        current.Value = item;
        ImmutableInterlocked.Update(ref snapshot, static (now, started) => now with { Items = now.Items.Add(started) }, item);
    }

    /// <summary>Finishes the calling thread's item; a thread that started none finishes an unnamed item of no weight.</summary>
    public void ItemDone(string outcome)
    {
        long now = time.GetTimestamp();
        InFlight item = current.Value ?? new InFlight(now, string.Empty, 0);
        current.Value = null;
        ImmutableInterlocked.Update(
            ref snapshot,
            static (now, done) => now with { Done = now.Done + 1, DoneWeight = now.DoneWeight + done.Weight, Items = now.Items.Remove(done) },
            item);
        channel.Writer.TryWrite(new RunEvent.ItemFinished(now, item, outcome));
    }

    public void Detail(string text)
    {
        if (IsDebug)
        {
            channel.Writer.TryWrite(new RunEvent.DetailWritten(time.GetTimestamp(), text, current.Value?.Identity));
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
        current.Dispose();
    }

    private static TimeSpan? Worst(PhaseBound? bound, int total, int done) =>
        bound is null ? null : EtaEstimator.WorstCase(Math.Min(bound.SolverItems, total - done), bound.TimeoutMs, bound.Rungs);

    private void Publish(Snapshot next) => Interlocked.Exchange(ref snapshot, next);

    private async Task ConsumeAsync()
    {
        using PeriodicTimer timer = new(IsDebug ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(60), time);
        Task<bool> read = channel.Reader.WaitToReadAsync().AsTask();
        Task<bool> tick = NextTick();
        while (true)
        {
            // The timer is disposed only after this loop, so every tick is a real one.
            if (await Task.WhenAny(read, tick).ConfigureAwait(false) == tick)
            {
                Heartbeat();
                tick = NextTick();
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

        // Each call makes a fresh ValueTask and converts it at once, so none is consumed twice.
        Task<bool> NextTick() => timer.WaitForNextTickAsync().AsTask();
    }

    private void Handle(RunEvent next)
    {
        switch (next)
        {
            case RunEvent.PhaseStarted phaseStarted:
                phase = new PhaseState(phaseStarted);
                Write(Verbosity.Normal, () => Line(phaseStarted.Timestamp, phase.Started.Timestamp).Format());
                break;
            case RunEvent.ItemFinished finished:
                Finish(finished);
                break;
            case RunEvent.DetailWritten detail:
                Write(Verbosity.Debug, () => RunLogLine.Detail(At(detail.Timestamp), phase.Started.Name, detail.Text, detail.Item));
                break;
            default:
                Write(Verbosity.Normal, () => RunLogLine.PhaseEnd(At(next.Timestamp), phase.Started.Name, time.GetElapsedTime(phase.Started.Timestamp, next.Timestamp), phase.Estimator, Interlocked.Read(ref dropped)));
                break;
        }
    }

    private void Finish(RunEvent.ItemFinished finished)
    {
        TimeSpan took = time.GetElapsedTime(finished.Item.Started, finished.Timestamp);
        phase.Done++;
        phase.DoneWeight += finished.Item.Weight;
        phase.Durations.Add(took);
        TimeSpan? eta = phase.Estimator.Observe(time.GetElapsedTime(phase.Started.Timestamp, finished.Timestamp), phase.DoneWeight, phase.Done, Worst(phase.Started.Bound, phase.Started.Total, phase.Done));
        long bucket = phase.Started.TotalWeight <= 0 ? Buckets : phase.DoneWeight * Buckets / phase.Started.TotalWeight;
        if (IsDebug)
        {
            Write(Verbosity.Debug, () => (Line(finished.Timestamp, phase.Started.Timestamp) with { Item = finished.Item.Identity, Outcome = finished.Outcome, Took = took, Eta = eta }).Format());
        }
        else if (bucket > phase.Bucket)
        {
            Write(Verbosity.Normal, () => (Line(finished.Timestamp, phase.Started.Timestamp) with { Eta = eta }).Format());
        }

        phase.Bucket = bucket;
    }

    /// <summary>
    /// Lines from the snapshot, not from the events, so they are current even while a producer is stuck in an item: one
    /// for each item in flight, the one started first written first, or one without an item when none is.
    /// </summary>
    private void Heartbeat()
    {
        Snapshot now = Volatile.Read(ref snapshot);
        if (now.Phase.Length == 0)
        {
            return;
        }

        long at = time.GetTimestamp();
        TimeSpan elapsed = time.GetElapsedTime(now.PhaseStart, at);
        TimeSpan? worst = Worst(now.Bound, now.Total, now.Done);
        RunLogLine line = new(At(at), now.Phase, now.Done, now.Total, now.DoneWeight, now.TotalWeight)
        {
            Eta = EtaEstimator.Estimate(elapsed, now.DoneWeight, now.TotalWeight, now.Done, worst),
            Worst = worst,
            Rate = Rate(now.Done, elapsed),
        };
        TimeSpan? slowAfter = string.Equals(now.Phase, phase.Started.Name, StringComparison.Ordinal) && phase.Durations.Count > 0
            ? SlowFactor * Median(phase.Durations)
            : null;
        IEnumerable<RunLogLine> lines = now.Items.IsEmpty
            ? [line]
            : now.Items.Select(item => (Item: item, Took: time.GetElapsedTime(item.Started, at))).Select(running => line with { Item = running.Item.Identity, Took = running.Took, Slow = running.Took > slowAfter });
        foreach (RunLogLine next in lines)
        {
            Write(Verbosity.Normal, next.Format);
        }
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

    /// <summary>What the producers last said: the phase, its counts, and the items in flight, in the order they started.</summary>
    private sealed record Snapshot(string Phase, int Total, long TotalWeight, PhaseBound? Bound, long PhaseStart, int Done, long DoneWeight, ImmutableList<InFlight> Items)
    {
        public static Snapshot Idle(long now) => new(string.Empty, 0, 0, Bound: null, now, Done: 0, DoneWeight: 0, []);
    }

    /// <summary>The consumer's view of the phase it is writing: counts, item times, the estimator, and the last 5% bucket written.</summary>
    private sealed class PhaseState(RunEvent.PhaseStarted started)
    {
        public RunEvent.PhaseStarted Started { get; } = started;

        public EtaEstimator Estimator { get; } = new(started.TotalWeight);

        public List<TimeSpan> Durations { get; } = [];

        public int Done { get; set; }

        public long DoneWeight { get; set; }

        public long Bucket { get; set; }
    }
}
