using System.CommandLine;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// Ticket P2-077 against the fakes: <c>--jobs</c> and the config's <c>jobs</c>, the <c>verify</c> and <c>contracts</c>
/// phases on several threads with everything they write in the pairs' order, a crash that stays one pair's, and the
/// wall-clock backstop that threads sharing a processor do not reach sooner.
/// </summary>
public sealed partial class CompareCommandTests
{
    private const string Identity = "procedureIdentity/v1";

    /// <summary>
    /// Criterion 1: the config's <c>jobs</c> replaces the default of one thread and <c>--jobs</c> replaces both; a value
    /// that is not positive is a usage error. Three pairs reach the backend, so the backstop it hears, the configured
    /// timeout times the threads, says how many threads there were, and never more than the pairs.
    /// </summary>
    [Fact]
    public void JobsOptionIsValidated()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        using TempFile config = new();
        using TempFile outFile = new();
        File.WriteAllText(config.Path, """{ "jobs": 2 }""");
        ProcedureIdentity[] identities = Identities(3);
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([.. identities.Select(Pair)], [], [], []));
        FakeBackend backend = new(identities.ToDictionary(static i => i.Value, static Verdict (_) => new Equivalent(ProofMethod.Bounded), StringComparer.Ordinal));
        Command command = CompareCommand.Create([frontend], backend);
        string[] paths = ["--legacy", legacy.Path, "--modern", modern.Path, "--out", outFile.Path];
        List<int> exitCodes = [];

        _ = CaptureStdOut(() => exitCodes.Add(command.Parse(paths).Invoke()));
        _ = CaptureStdOut(() => exitCodes.Add(command.Parse([.. paths, "--config", config.Path]).Invoke()));
        _ = CaptureStdOut(() => exitCodes.Add(command.Parse([.. paths, "--config", config.Path, "--jobs", "8"]).Invoke()));
        string error = CaptureStdErr(() => exitCodes.Add(command.Parse([.. paths, "--jobs", "0"]).Invoke()));

        Assert.Equal([ExitCodes.Success, ExitCodes.Success, ExitCodes.Success, ExitCodes.UsageError], exitCodes);
        int timeout = EquivConfig.Default.TimeoutMs;
        Assert.Equal(
            [.. Enumerable.Repeat(timeout, 3), .. Enumerable.Repeat(timeout * 2, 3), .. Enumerable.Repeat(timeout * 3, 3)],
            backend.Calls.Select(static o => o.TimeoutMs));
        Assert.Equal($"error: bound, timeoutMs, resourceLimit and jobs must be positive integers{Environment.NewLine}", error, StringComparer.Ordinal);
        Assert.NotEmpty(command.Parse([.. paths, "--jobs", "many"]).Errors);
    }

    /// <summary>
    /// Criterion 2: eight pairs on four threads, the first four in the backend at once and answering last started first. The
    /// results, the unverified identities, the notifications and the lines on stderr are in the pairs' order all the same.
    /// </summary>
    [Fact]
    public void ParallelResultsKeepTheirOrder()
    {
        ProcedureIdentity[] identities = Identities(8);
        using Rendezvous together = new(4);
        ScriptedBackend backend = new((identity, _) =>
        {
            together.Meet();
            int index = Index(identity);
            together.Leave(index);
            return index switch
            {
                1 or 6 => throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"bug {index}")),
                _ when index % 2 == 0 => new Equivalent(ProofMethod.Bounded),
                _ => new Divergent(Counterexample()),
            };
        });

        (int exitCode, Run run, string stderr) = ParallelRun([.. identities.Select(Pair)], backend, jobs: 4);

        Assert.Equal(ExitCodes.InternalError, exitCode);
        Assert.Equal(["T::P0()", "T::P2()", "T::P3()", "T::P4()", "T::P5()", "T::P7()"], run.Results.Select(static r => r.PartialFingerprints[Identity]), StringComparer.Ordinal);
        Assert.Equal(["EQ001", "EQ001", "EQ002", "EQ001", "EQ002", "EQ002"], run.Results.Select(static r => r.RuleId), StringComparer.Ordinal);
        Assert.Equal(["T::P1()", "T::P6()"], run.GetProperty<List<string>>("unverified"), StringComparer.Ordinal);
        string[] failures = ["Verifying T::P1() against T::P1() failed: bug 1", "Verifying T::P6() against T::P6() failed: bug 6"];
        Assert.Equal(failures, Assert.Single(run.Invocations).ToolExecutionNotifications.Select(static n => n.Message.Text), StringComparer.Ordinal);
        Assert.Equal(failures.Select(static f => $"error: {f}"), stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    }

    /// <summary>
    /// Criterion 2 for the <c>contracts</c> phase: four callers go back to the backend at once, and the results and the
    /// warnings of the two whose contract step throws keep the callers' order.
    /// </summary>
    [Fact]
    public void ParallelContractsKeepTheirOrder()
    {
        using Rendezvous together = new(4);
        ScriptedBackend backend = new(static (identity, _) => string.Equals(identity, "T::F()", StringComparison.Ordinal) ? new Divergent(Counterexample()) : new Equivalent(ProofMethod.Bounded))
        {
            Contracts = (identity, _) =>
            {
                together.Meet();
                int index = Index(identity);
                together.Leave(index);
                return index % 2 == 1
                    ? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"contract bug {index}"))
                    : new Equivalent(ProofMethod.Bounded) { ContractsUsed = [new ContractUse("T::F()", "true", "observed-predicates")] };
            },
        };

        (int exitCode, Run run, string stderr) = ParallelRun([.. Identities(4).Select(static i => Caller(i.Value, "T::F()")), Caller("T::F()")], backend, jobs: 4);

        Assert.Equal(ExitCodes.Divergent, exitCode);
        Assert.Equal(["T::P0()", "T::P1()", "T::P2()", "T::P3()", "T::F()"], run.Results.Select(static r => r.PartialFingerprints[Identity]), StringComparer.Ordinal);
        Assert.Equal(["bounded+contract", "bounded", "bounded+contract", "bounded"], run.Results.Take(4).Select(static r => r.GetProperty<string>("proofMethod")), StringComparer.Ordinal);
        Assert.Equal(
            [
                "warning: Verifying T::P1() under callee contracts failed, so it keeps its verdict: contract bug 1",
                "warning: Verifying T::P3() under callee contracts failed, so it keeps its verdict: contract bug 3",
            ],
            stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Criterion 2 (ADR 0023) on four threads: a pair the backend throws on and a pair whose weighing throws are one
    /// <c>error</c> notification and one unverified identity each, and the three other pairs are verified. An exception
    /// that ends a run on one thread ends it here too, as itself.
    /// </summary>
    [Fact]
    public void ParallelCrashIsOnePairOnly()
    {
        ProcedureIdentity[] identities = Identities(5);
        IrProcedure broken = IrText.Parse($"proc \"{identities[3].Value}\" () entry B0 B0: goto B7");
        ProcedurePair[] pairs = [Pair(identities[0]), Pair(identities[1]), Pair(identities[2]), new ProcedurePair(identities[3], identities[3], broken, broken), Pair(identities[4])];
        using Rendezvous together = new(4);
        ScriptedBackend crashing = new((identity, _) =>
        {
            together.Meet();
            return Index(identity) == 1 ? throw new InvalidOperationException("encoder bug") : new Equivalent(ProofMethod.Bounded);
        });
        ScriptedBackend cancelled = new(static (identity, _) => Index(identity) == 1 ? throw new OperationCanceledException("stop") : new Equivalent(ProofMethod.Bounded));

        (int exitCode, Run run, _) = ParallelRun(pairs, crashing, jobs: 4);

        Assert.Equal(ExitCodes.InternalError, exitCode);
        Assert.Equal(["T::P0()", "T::P2()", "T::P4()"], run.Results.Select(static r => r.PartialFingerprints[Identity]), StringComparer.Ordinal);
        Assert.All(run.Results, static r => Assert.Equal("EQ001", r.RuleId));
        Assert.Equal(["T::P1()", "T::P3()"], run.GetProperty<List<string>>("unverified"), StringComparer.Ordinal);
        IList<Notification> notifications = Assert.Single(run.Invocations).ToolExecutionNotifications;
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, static n => Assert.Equal(FailureLevel.Error, n.Level));
        Assert.Equal("Verifying T::P1() against T::P1() failed: encoder bug", notifications[0].Message.Text);
        Assert.StartsWith("Weighing T::P3() against T::P3() failed: ", notifications[1].Message.Text, StringComparison.Ordinal);
        Assert.Equal("stop", Assert.Throws<OperationCanceledException>(() => ParallelRun(pairs, cancelled, jobs: 4)).Message);
    }

    /// <summary>
    /// Criterion 4, on a machine modelled with one processor that every query in flight shares. Each of four queries needs
    /// the whole default backstop of processor time, so alone it just finishes, and with four in flight it takes four times
    /// as long on the clock. Under <c>--jobs 4</c> the backstop the backend hears is four times as long too, so every
    /// query that finishes under <c>--jobs 1</c> finishes, and neither run counts a query the backstop ended.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void JobsDoNotShortenAQuery(int jobs)
    {
        const int Queries = 4;
        int work = EquivConfig.Default.TimeoutMs;
        int inFlight = 0;
        using Barrier all = new(jobs);
        ScriptedBackend oneProcessor = new((_, options) =>
        {
            Interlocked.Increment(ref inFlight);
            Assert.True(all.SignalAndWait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            long onTheClock = (long)work * Volatile.Read(ref inFlight);
            Assert.True(all.SignalAndWait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            Interlocked.Decrement(ref inFlight);
            return onTheClock <= options.TimeoutMs
                ? new Equivalent(ProofMethod.Bounded)
                : new Unknown(UnknownReason.Timeout, "solver returned unknown (timeout)")
                {
                    Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, string.Create(CultureInfo.InvariantCulture, $"solver returned unknown (timeout){QueryEndings.WallClockHit}{options.TimeoutMs} ms hit"))],
                };
        });

        (int exitCode, Run run, _) = ParallelRun([.. Identities(Queries).Select(Pair)], oneProcessor, jobs);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(Enumerable.Repeat("EQ001", Queries), run.Results.Select(static r => r.RuleId), StringComparer.Ordinal);
        Assert.Equal(0L, run.GetProperty<Dictionary<string, long>>("queryEndings")["wallClock"]);
    }

    /// <summary>
    /// Criterion 4's count: <c>run.properties.queryEndings</c> counts the rungs a limit timed out, by the limit, in every
    /// result's ladder, and a <c>--lower-only</c> run, which asks no query, has none.
    /// </summary>
    [Fact]
    public void QueryEndingsCountTheLimitThatEndedEachRung()
    {
        ProcedureIdentity[] identities = Identities(3);
        Verdict[] verdicts =
        [
            new Unknown(UnknownReason.Timeout, "gave up")
            {
                Ladder =
                [
                    new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, "solver returned unknown (canceled): resource limit 5000000 hit"),
                    new LadderStep(ProofMethod.Chc, RungOutcome.Timeout, "Spacer gave up: canceled: resource limit 50000000 hit"),
                ],
            },
            new Divergent(Counterexample())
            {
                Ladder =
                [
                    new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, "solver returned unknown (timeout): wall-clock limit 60000 ms hit"),
                    new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, "solver returned unknown (interrupted)"),
                    new LadderStep(ProofMethod.LockstepInduction, RungOutcome.Refuted, "the note of a rung that did not time out: resource limit 1 hit"),
                ],
            },
            new Equivalent(ProofMethod.Bounded),
        ];
        ScriptedBackend backend = new((identity, _) => verdicts[Index(identity)]);

        (_, Run run, _) = ParallelRun([.. identities.Select(Pair)], backend, jobs: 2);
        (_, Run lowerOnly, _) = ParallelRun([.. identities.Select(Pair)], backend, jobs: 2, lowerOnly: true);

        Dictionary<string, long> endings = run.GetProperty<Dictionary<string, long>>("queryEndings");
        Assert.Equal(["resourceLimit", "wallClock"], endings.Keys, StringComparer.Ordinal);
        Assert.Equal([2L, 1L], endings.Values);
        Assert.False(lowerOnly.TryGetProperty("queryEndings", out Dictionary<string, long>? _));
    }

    /// <summary><c>T::P0()</c> to <c>T::P{count - 1}()</c>.</summary>
    internal static ProcedureIdentity[] Identities(int count) =>
        [.. Enumerable.Range(0, count).Select(static i => new ProcedureIdentity(string.Create(CultureInfo.InvariantCulture, $"T::P{i}()")))];

    /// <summary>The number in an identity <see cref="Identities"/> made.</summary>
    internal static int Index(string identity) => int.Parse(identity.AsSpan(4, identity.Length - 6), CultureInfo.InvariantCulture);

    private static (int ExitCode, Run Run, string StdErr) ParallelRun(ProcedurePair[] pairs, IVerificationBackend backend, int jobs, bool lowerOnly = false)
    {
        (int exitCode, SarifLog log, string stderr) = ParallelLog(pairs, backend, jobs, lowerOnly);
        return (exitCode, log.Runs[0], stderr);
    }

    /// <summary>One run on <paramref name="pairs"/> with <c>--jobs</c> <paramref name="jobs"/>: its exit code, its SARIF log and what it wrote to stderr.</summary>
    internal static (int ExitCode, SarifLog Log, string StdErr) ParallelLog(ProcedurePair[] pairs, IVerificationBackend backend, int jobs, bool lowerOnly = false)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        InMemoryReportSink sink = new();
        int exitCode = ExitCodes.UsageError;
        string stderr = CaptureStdErr(() => CaptureStdOut(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false, LowerOnly: lowerOnly) { Jobs = jobs },
            [new FakeFrontend("csharp", _ => true, new MatchResult([.. pairs], [], [], []))], backend, sink, NullRunLog.Instance)));
        return (exitCode, sink.Log!, stderr);
    }
}
