using System.Collections.Concurrent;

using Equiv.Core;
using Equiv.Core.Configuration;

using Xunit;

namespace Equiv.Verify.Cvc5.Tests;

/// <summary>
/// Ticket P1-039 criterion 2: a script cvc5 answered <c>unknown</c>, split into partitions each asked in a process of its
/// own, with the process stood in for. The stand-in writes the partitions a test gives when it is asked to split, answers
/// each partition by the partition its script asserts, and answers the cover check as the test says.
/// </summary>
public sealed class Cvc5PartitionsTests
{
    private const string Script = "(set-logic QF_BV)\n(declare-fun x () (_ BitVec 1))\n(assert (= x x))\n(check-sat)\n(get-value (x))\n";

    private const string SplitOption = "--write-partitions-to=";

    private const string CoverMark = "(assert (not (or false";

    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(1_500);

    [Fact]
    public void OneSatisfiablePartition_IsSatWithItsValues()
    {
        Fake fake = new("p0\np1\np2\n", static partition => string.Equals(partition, "p1", StringComparison.Ordinal) ? "sat\n((x #b1))\n" : "unsat\n", cover: "unsat\n");

        SmtSat sat = Assert.IsType<SmtSat>(fake.Solver.AskInPartitions(Script, 3, Limit));

        Assert.Equal("#b1", sat.Values["x"]);
        Assert.DoesNotContain(fake.Runs, static r => r.Script!.Contains(CoverMark, StringComparison.Ordinal));
    }

    /// <summary>Every partition is waited for, and the first satisfiable one in the order written gives the values, whichever process ended first.</summary>
    [Fact]
    public void TwoSatisfiablePartitions_TheFirstWrittenGivesTheValues()
    {
        Fake fake = new("p0\np1\np2\n", partition => partition switch
        {
            "p0" => "unknown\n",
            "p1" => Slowly("sat\n((x #b0))\n"),
            _ => "sat\n((x #b1))\n",
        }, cover: "unsat\n");

        Cvc5Solver.Partitioned split = fake.Solver.Split(Script, 3, Limit);

        Assert.Equal("#b0", Assert.IsType<SmtSat>(split.Answer).Values["x"]);
        Assert.Equal(["p0", "p1", "p2"], split.Partitions);
        Assert.Equal(3, split.Answers.Length);
        Assert.Null(split.Cover);
    }

    /// <summary>
    /// A satisfiable partition stops the partitions written after it, running or not, and not those before it: the one
    /// before it that ends later is still waited for.
    /// </summary>
    [Fact]
    public void ASatisfiablePartition_StopsTheLaterOnes_AndWaitsForTheEarlierOnes()
    {
        Fake fake = new("p0\np1\np2\np3\n", static (partition, stop) => partition switch
        {
            "p0" => Slowly("unsat\n"),
            "p1" => "sat\n((x #b1))\n",
            _ => stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)) ? null : "unknown\n",
        }, cover: "unsat\n", processes: 4);

        Cvc5Solver.Partitioned split = fake.Solver.Split(Script, 4, Limit);

        Assert.Equal("#b1", Assert.IsType<SmtSat>(split.Answer).Values["x"]);
        Assert.IsType<SmtUnsat>(split.Answers[0]);
        Assert.All(split.Answers[2..], static a => Assert.Equal("stopped: an earlier partition is satisfiable", Assert.IsType<SmtUnknown>(a).Reason));
    }

    /// <summary>A partition a satisfiable one stopped before it had a process never starts one.</summary>
    [Fact]
    public void APartitionStoppedBeforeItStarts_RunsNoProcess()
    {
        Fake fake = new("p0\np1\np2\n", static partition => string.Equals(partition, "p0", StringComparison.Ordinal) ? "sat\n((x #b0))\n" : "unsat\n", cover: "unsat\n", processes: 1);

        Cvc5Solver.Partitioned split = fake.Solver.Split(Script, 3, Limit);

        Assert.Equal("#b0", Assert.IsType<SmtSat>(split.Answer).Values["x"]);
        Assert.Equal(2, fake.Runs.Count);
        Assert.All(split.Answers[1..], static a => Assert.IsType<SmtUnknown>(a));
    }

    [Fact]
    public void EveryPartitionUnsat_AndTheCoverUnsat_IsUnsat()
    {
        Fake fake = new("p0\np1\n", static _ => "unsat\n", cover: "unsat\n");

        Cvc5Solver.Partitioned split = fake.Solver.Split(Script, 2, Limit);

        Assert.IsType<SmtUnsat>(split.Answer);
        Assert.IsType<SmtUnsat>(split.Cover);
        string cover = Assert.Single(fake.Runs, static r => r.Script!.Contains(CoverMark, StringComparison.Ordinal)).Script!;
        Assert.Equal("(set-logic QF_BV)\n(declare-fun x () (_ BitVec 1))\n(assert (not (or false p0 p1)))\n(check-sat)\n", cover);
    }

    [Theory]
    [InlineData("sat\n((x #b0))\n")]
    [InlineData("unknown\n")]
    [InlineData(null)]
    public void EveryPartitionUnsat_AndTheCoverNotUnsat_IsUnknown(string? cover)
    {
        Fake fake = new("p0\np1\n", static _ => "unsat\n", cover);

        Cvc5Solver.Partitioned split = fake.Solver.Split(Script, 2, Limit);

        Assert.Equal("the partitions do not cover every case", Assert.IsType<SmtUnknown>(split.Answer).Reason);
        Assert.NotNull(split.Cover);
    }

    /// <summary>A partition that is unknown, or whose process was killed, leaves the query unknown; the splitting run's own <c>unsat</c> beside them is not taken.</summary>
    [Theory]
    [InlineData("unknown\n")]
    [InlineData("cvc5 interrupted by timeout.\n")]
    [InlineData(null)]
    public void APartitionThatIsNotDecided_IsUnknown_WhateverTheSplittingRunAnswered(string? undecided)
    {
        Fake fake = new("p0\np1\np2\n", partition => string.Equals(partition, "p2", StringComparison.Ordinal) ? undecided : "unsat\n", cover: "unsat\n");

        Cvc5Solver.Partitioned split = fake.Solver.Split(Script, 3, Limit);

        Assert.Equal("1 of 3 partitions undecided", Assert.IsType<SmtUnknown>(split.Answer).Reason);
        Assert.Equal("unsat\n", Fake.SplitAnswer);
        Assert.Null(split.Cover);
        Assert.Equal(4, fake.Runs.Count);
    }

    /// <summary>No partition written, in no file or an empty one, is the unknown it was, after the one splitting process.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n\n")]
    public void NoPartitionWritten_IsUnknown(string? written)
    {
        Fake fake = new(written, static _ => "sat\n((x #b0))\n", cover: "unsat\n");

        Cvc5Solver.Partitioned split = fake.Solver.Split(Script, 8, Limit);

        Assert.Equal("no partition written", Assert.IsType<SmtUnknown>(split.Answer).Reason);
        Assert.Empty(split.Partitions);
        Assert.Empty(split.Answers);
        Assert.Single(fake.Runs);
    }

    /// <summary>The splitting run gets the plain limits and the partitioning options; each partition, the plain limits and the script with one more assertion.</summary>
    [Fact]
    public void TheProcessesGetTheirOptionsAndScripts()
    {
        Fake fake = new("(= x #b0)\n(= x #b0)\n(not (= x #b0))\n", static _ => "unsat\n", cover: "unsat\n");

        Assert.IsType<SmtUnsat>(fake.Solver.AskInPartitions(Script, 24, Limit));

        (string path, string[] arguments, string? script, TimeSpan killAfter) = fake.Runs.Single(static r => r.Arguments.Any(static a => a.StartsWith(SplitOption, StringComparison.Ordinal)));
        Assert.Equal("cvc5", path);
        Assert.Equal(Script, script);
        Assert.Equal(TimeSpan.FromMilliseconds(6_500), killAfter);
        Assert.Equal(
            ["--arrays-exp", "--rlimit=2000000", "--tlimit=1500", "--compute-partitions=24", "--partition-strategy=decision-scatter", "--partition-when=climit", "--checks-before-partition=1", "--checks-between-partitions=1"],
            arguments[..^1]);
        Assert.Equal("decision-scatter", Cvc5Solver.PartitionStrategy);
        Assert.False(File.Exists(arguments[^1][SplitOption.Length..]));

        string[] partitions = [.. fake.Runs.Where(static r => !r.Arguments.Any(static a => a.StartsWith(SplitOption, StringComparison.Ordinal)) && !r.Script!.Contains(CoverMark, StringComparison.Ordinal)).Select(static r => r.Script!).Order(StringComparer.Ordinal)];
        Assert.Equal(
            [Script.Replace("(check-sat)", "(assert (= x #b0))\n(check-sat)", StringComparison.Ordinal), Script.Replace("(check-sat)", "(assert (not (= x #b0)))\n(check-sat)", StringComparison.Ordinal)],
            partitions);
        Assert.All(fake.Runs.Where(static r => !r.Arguments.Any(static a => a.StartsWith(SplitOption, StringComparison.Ordinal))), static r => Assert.Equal(["--arrays-exp", "--rlimit=2000000", "--tlimit=1500"], r.Arguments));
    }

    /// <summary>A process that throws is not swallowed here: the backend's caller turns it into an unknown, as for a plain script.</summary>
    [Fact]
    public void APartitionProcessThatThrows_Throws()
    {
        Fake fake = new("p0\np1\n", static partition => string.Equals(partition, "p1", StringComparison.Ordinal) ? throw new InvalidOperationException("cannot start cvc5") : "unsat\n", cover: "unsat\n");

        Assert.Equal("cannot start cvc5", Assert.Throws<InvalidOperationException>(() => fake.Solver.AskInPartitions(Script, 2, Limit)).Message);
    }

    /// <summary>At most <see cref="Cvc5Solver.Processes"/> processes run at once, whatever the partitions asked for, and over two queries split at once.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void NoMoreProcessesRunAtOnceThanAllowed(int processes)
    {
        int running = 0;
        int most = 0;
        Fake fake = new("p0\np1\np2\np3\np4\np5\np6\np7\n", _ =>
        {
            int now = Interlocked.Increment(ref running);
            InterlockedMax(ref most, now);
            Thread.Sleep(20);
            Interlocked.Decrement(ref running);
            return "unsat\n";
        }, cover: "unsat\n", processes);

        Parallel.Invoke(() => fake.Solver.AskInPartitions(Script, 8, Limit), () => fake.Solver.AskInPartitions(Script, 8, Limit));

        Assert.Equal(processes, fake.Solver.Processes);
        Assert.InRange(most, 1, processes);
    }

    [Fact]
    public void ProcessesAreTheDefaultJobsUnlessSet_AndAtLeastOne()
    {
        Assert.Equal(EquivConfig.Default.Jobs, new Cvc5Solver("cvc5").Processes);
        Assert.Throws<ArgumentOutOfRangeException>(static () => new Cvc5Solver("cvc5") { Processes = 0 });
    }

    [Fact]
    public void AScriptAndAtLeastTwoPartitions_AreRequired()
    {
        Cvc5Solver solver = new("cvc5", static (_, _, _, _, _) => throw new InvalidOperationException("not run"));

        Assert.Throws<ArgumentNullException>(() => solver.AskInPartitions(null!, 2, Limit));
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.AskInPartitions(Script, 1, Limit));
    }

    private static string Slowly(string answer)
    {
        Thread.Sleep(50);
        return answer;
    }

    private static void InterlockedMax(ref int most, int now)
    {
        int seen = Volatile.Read(ref most);
        while (now > seen && Interlocked.CompareExchange(ref most, now, seen) is var before && before != seen)
        {
            seen = before;
        }
    }

    /// <summary>
    /// The stand-in for cvc5: <paramref name="written"/> is what a splitting run writes (no file when null), a partition's
    /// script is answered by <paramref name="partition"/> of the partition it asserts, and the cover check by <paramref name="cover"/>.
    /// A null answer is a process killed at its limit.
    /// </summary>
    private sealed class Fake
    {
        public Fake(string? written, Func<string, string?> partition, string? cover, int processes = 4)
            : this(written, (p, _) => partition(p), cover, processes)
        {
        }

        /// <summary>A stand-in whose partitions also hear the token that stops them.</summary>
        public Fake(string? written, Func<string, CancellationToken, string?> partition, string? cover, int processes)
        {
            Solver = new Cvc5Solver("cvc5", (path, arguments, script, killAfter, stop) =>
            {
                Runs.Add((path, [.. arguments], script, killAfter));
                if (arguments.FirstOrDefault(static a => a.StartsWith(SplitOption, StringComparison.Ordinal)) is { } split)
                {
                    if (written is not null)
                    {
                        File.WriteAllText(split[SplitOption.Length..], written);
                    }

                    return SplitAnswer;
                }

                if (script!.Contains(CoverMark, StringComparison.Ordinal))
                {
                    return cover;
                }

                int at = script.LastIndexOf("(assert ", StringComparison.Ordinal) + "(assert ".Length;
                return partition(script[at..script.IndexOf(")\n(check-sat)", at, StringComparison.Ordinal)], stop);
            })
            {
                Processes = processes,
            };
        }

        public Cvc5Solver Solver { get; }

        public static string SplitAnswer => "unsat\n";

        public ConcurrentBag<(string Path, string[] Arguments, string? Script, TimeSpan KillAfter)> Runs { get; } = [];
    }
}
