using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Configuration;

namespace Equiv.Verify.Cvc5;

/// <summary>
/// <see cref="ISmtSolver"/> over the <c>cvc5</c> executable, run as a process once per script (ADR 0050 decisions 1, 5
/// and 6; ticket P1-033). cvc5 is never linked and never shipped: its release binary links LGPL libraries (ADR 0017), so
/// <c>equiv</c> runs the executable <c>equiv.config.json</c> names (<c>solvers.cvc5.path</c>). Each script gets
/// <c>--arrays-exp</c>, which reads the constant arrays of the product encoding, <c>--rlimit</c>
/// (<see cref="ResourceLimit"/>), cvc5's own count of its steps, so the same script gives up at the same point on any
/// machine, and <c>--tlimit</c>, the wall-clock limit behind it. A process still running a little after that limit is
/// killed, and the script is unknown.
/// </summary>
public sealed class Cvc5Solver : ISmtSolver
{
    /// <summary>
    /// The <c>--rlimit</c> of each script, chosen from <c>docs/runs/2026-10-04-cvc5-budget.md</c> (ticket P1-033
    /// criterion 7).
    /// </summary>
    public const long DefaultResourceLimit = 2_000_000;

    /// <summary>
    /// The one strategy a query is split with (ticket P1-039): of cvc5's six, it decided the most of the queries
    /// <c>docs/runs/2026-10-04-cvc5-partitioning.md</c> split, and it writes the partitions asked for, where a cube
    /// strategy writes a power of two.
    /// </summary>
    public const string PartitionStrategy = "decision-scatter";

    /// <summary>How long past <c>--tlimit</c> the process may run before it is killed: cvc5 checks its own limit between steps.</summary>
    internal static readonly TimeSpan KillAfter = TimeSpan.FromSeconds(5);

    private static readonly SmtUnknown Killed = new("wall-clock limit: the process was killed");

    private static readonly SmtUnknown Stopped = new("stopped: an earlier partition is satisfiable");

    private readonly string path;
    private readonly Cvc5Process.Runner run;
    private readonly Lazy<string> version;
    private readonly ProcessSlots slots = new(EquivConfig.Default.Jobs);

    public Cvc5Solver(string path)
        : this(path, Cvc5Process.Run)
    {
    }

    /// <summary>For tests: <paramref name="run"/> stands in for the process.</summary>
    internal Cvc5Solver(string path, Cvc5Process.Runner run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
        this.run = run;
        version = new Lazy<string>(() => SmtOutput.Version(run(path, ["--version"], script: null, KillAfter, CancellationToken.None)));
    }

    public string Name => "cvc5";

    /// <summary>The version <c>cvc5 --version</c> prints, read once; <c>unknown</c> when it prints none.</summary>
    public string Version => version.Value;

    /// <summary>The <c>--rlimit</c> each script gets.</summary>
    public long ResourceLimit { get; init; } = DefaultResourceLimit;

    /// <summary>
    /// The most cvc5 processes <see cref="AskInPartitions"/> runs at once, over every query it splits at the same time
    /// (ticket P1-039): <c>equiv compare</c> gives it the run's <c>jobs</c>. The processor count, up to 24, unless set.
    /// </summary>
    public int Processes
    {
        get => slots.Total;
        init => slots = new ProcessSlots(value);
    }

    public SmtAnswer Ask(string script, TimeSpan limit)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Plain(script, limit, CancellationToken.None);
    }

    /// <summary>
    /// cvc5 is asked to split <paramref name="script"/> (<see cref="Split"/>): the answer is <c>sat</c> with the values of
    /// the first satisfiable partition, <c>unsat</c> when every partition is unsatisfiable and they cover every case, and
    /// unknown otherwise.
    /// </summary>
    public SmtAnswer AskInPartitions(string script, int partitions, TimeSpan limit) => Split(script, partitions, limit).Answer;

    /// <summary>
    /// Splits <paramref name="script"/> into <paramref name="partitions"/> partitions (ticket P1-039). One cvc5 process,
    /// under the limits a plain script gets, is asked to write them at its first check, with <see cref="PartitionStrategy"/>.
    /// Its own answer is never read: once it has written partitions, cvc5 prints <c>unsat</c> whatever the query is. Each
    /// partition is then the script with one more assertion, asked as a plain script in a process of its own, as many at
    /// once as <see cref="Processes"/> allows (<see cref="Answers"/>). The answer is the first satisfiable partition in the
    /// order written, so which values come back does not depend on which process ended first. When all are
    /// unsatisfiable, one more process checks the cover
    /// (<see cref="SmtPartitions.Cover"/>). No partition written leaves the script the unknown it was.
    /// </summary>
    internal Partitioned Split(string script, int partitions, TimeSpan limit)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentOutOfRangeException.ThrowIfLessThan(partitions, EquivConfig.MinCvc5Partitions);
        ImmutableArray<string> written = Written(script, partitions, limit);
        if (written.IsEmpty)
        {
            return new Partitioned(new SmtUnknown("no partition written"), [], [], Cover: null);
        }

        ImmutableArray<SmtAnswer> answers = Answers(script, written, limit);
        if (answers.OfType<SmtSat>().FirstOrDefault() is { } sat)
        {
            return new Partitioned(sat, written, answers, Cover: null);
        }

        int undecided = answers.Count(static a => a is not SmtUnsat);
        if (undecided > 0)
        {
            return new Partitioned(new SmtUnknown(string.Create(CultureInfo.InvariantCulture, $"{undecided} of {written.Length} partitions undecided")), written, answers, Cover: null);
        }

        SmtAnswer cover = Slotted(() => Plain(SmtPartitions.Cover(script, written), limit, CancellationToken.None));
        return new Partitioned(cover is SmtUnsat ? cover : new SmtUnknown("the partitions do not cover every case"), written, answers, cover);
    }

    private SmtAnswer Plain(string script, TimeSpan limit, CancellationToken stop) =>
        run(path, Limits(limit), script, limit + KillAfter, stop) is { } output ? SmtOutput.Parse(output) : stop.IsCancellationRequested ? Stopped : Killed;

    private string[] Limits(TimeSpan limit) =>
    [
        "--arrays-exp",
        "--rlimit=" + ResourceLimit.ToString(CultureInfo.InvariantCulture),
        "--tlimit=" + ((long)limit.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
    ];

    /// <summary>The partitions cvc5 writes for <paramref name="script"/>, each once and in order; none when it writes none in its limits.</summary>
    private ImmutableArray<string> Written(string script, int partitions, TimeSpan limit)
    {
        string file = Path.Combine(Path.GetTempPath(), $"equiv-{Guid.NewGuid():N}.partitions");
        try
        {
            string[] arguments =
            [
                .. Limits(limit),
                "--compute-partitions=" + partitions.ToString(CultureInfo.InvariantCulture),
                "--partition-strategy=" + PartitionStrategy,
                "--partition-when=climit",
                "--checks-before-partition=1",
                "--checks-between-partitions=1",
                "--write-partitions-to=" + file,
            ];
            _ = Slotted(() => run(path, arguments, script, limit + KillAfter, CancellationToken.None));
            return File.Exists(file) ? SmtPartitions.Read(File.ReadAllText(file)) : [];
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Each partition's answer, in the partitions' order. A satisfiable partition stops every partition written after it:
    /// one that runs is killed and one that has not started never starts, and each is <see cref="Stopped"/>. Every
    /// partition before it runs to its end, so the first satisfiable partition in the order written is the same whichever
    /// process ends first. A partition's script is made once its process has a slot, so no more copies of a large script
    /// are held than processes run.
    /// </summary>
    private ImmutableArray<SmtAnswer> Answers(string script, ImmutableArray<string> partitions, TimeSpan limit)
    {
        CancellationTokenSource[] stops = [.. partitions.Select(static _ => new CancellationTokenSource())];
        try
        {
            Task<SmtAnswer>[] asked = new Task<SmtAnswer>[partitions.Length];
            for (int i = 0; i < partitions.Length; i++)
            {
                slots.Acquire();
                if (stops[i].IsCancellationRequested)
                {
                    slots.Release();
                    asked[i] = Task.FromResult<SmtAnswer>(Stopped);
                    continue;
                }

                int index = i;
                asked[i] = Task.Factory.StartNew(
                    () =>
                    {
                        try
                        {
                            SmtAnswer answer = Plain(SmtPartitions.With(script, partitions[index]), limit, stops[index].Token);
                            if (answer is SmtSat)
                            {
                                foreach (CancellationTokenSource later in stops.AsSpan(index + 1))
                                {
                                    later.Cancel();
                                }
                            }

                            return answer;
                        }
                        finally
                        {
                            slots.Release();
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            return [.. Task.WhenAll(asked).GetAwaiter().GetResult()];
        }
        finally
        {
            foreach (CancellationTokenSource stop in stops)
            {
                stop.Dispose();
            }
        }
    }

    private T Slotted<T>(Func<T> work)
    {
        slots.Acquire();
        try
        {
            return work();
        }
        finally
        {
            slots.Release();
        }
    }

    /// <summary>
    /// What splitting a script came to (ticket P1-039): the answer, the partitions written, each partition's answer in the
    /// same order, and the cover check's answer when it was asked.
    /// </summary>
    internal sealed record Partitioned(SmtAnswer Answer, ImmutableArray<string> Partitions, ImmutableArray<SmtAnswer> Answers, SmtAnswer? Cover);
}
