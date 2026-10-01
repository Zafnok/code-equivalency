using System.CommandLine;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// <c>equiv compare --execute</c> against fakes (ADR 0035 decision 2; ticket M4-009): the Windows check, the stderr note, and
/// a replay that is recorded on each Divergent without changing its verdict, rule id, fingerprint or the exit code.
/// </summary>
[Collection("Console")]
public sealed class CompareExecuteTests
{
    private const string Threw = "[\"Threw\",\"System.ArgumentNullException\"]";

    private const string OtherThrew = "[\"Threw\",\"System.NullReferenceException\"]";

    private const string Returned = "[\"Returned\",1]";

    private static readonly ProcedureIdentity DivergentIdentity = new("T::Divergent()");

    private static readonly ProcedureIdentity EquivalentIdentity = new("T::Equivalent()");

    [Fact]
    public void Create_ParsesExecuteAndLeavesItOffByDefault()
    {
        Command command = CompareCommand.Create([], new FakeBackend(new Dictionary<string, Verdict>(StringComparer.Ordinal)));

        Assert.False(command.Parse(["--legacy", "a.sln", "--modern", "b.sln"]).GetValue<bool>("--execute"));
        Assert.True(command.Parse(["--legacy", "a.sln", "--modern", "b.sln", "--execute"]).GetValue<bool>("--execute"));
    }

    [Fact]
    public void Execute_OffByDefault_NoSnapshotChanges()
    {
        FakeReplay replay = new(Threw, OtherThrew);

        (int exitCode, string error, SarifLog? log) = Compare(execute: false, replay, new ExecutionEnvironment(IsWindows: true, replay));

        Assert.Equal(ExitCodes.Divergent, exitCode);
        Assert.Empty(error);
        Assert.Empty(replay.Creates);
        Assert.Empty(replay.Starts);
        Assert.All(log!.Runs[0].Results, static r => Assert.False(r.TryGetProperty("replay", out string? _)));
    }

    /// <summary>Ticket P2-056 criterion 3: off Windows, a side on .NET Framework stops the run once loaded, naming the project and its runtime.</summary>
    [Fact]
    public void Execute_NonWindows_AFrameworkSideExitsThree()
    {
        FakeReplay replay = new(Threw, OtherThrew);
        FakeFrontend frontend = new(
            "csharp", _ => true, Match(), replay: replay, legacyRuntimes: [("App", "net8.0", "attribute"), ("Old", "net472", "attribute")], modernRuntimes: [("App", "net8.0", "attribute")]);
        InMemoryReportSink sink = new();
        using TempFile legacy = new();
        using TempFile modern = new();
        int exitCode = 0;

        string error = CaptureStdErr(() => exitCode = CompareCommand.Run(
            Options(legacy.Path, modern.Path, execute: true), [frontend], Backend(), sink, NullRunLog.Instance, new ExecutionEnvironment(IsWindows: false, replay)));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal("error: --execute: Old runs on net472, and .NET Framework needs Windows (ADR 0040)" + Environment.NewLine, error);
        Assert.Null(sink.Log);
        Assert.Empty(replay.Creates);
    }

    [Fact]
    public void Execute_PrintsNote()
    {
        (int exitCode, string error, SarifLog? log) = Compare(execute: true, replay: null, new ExecutionEnvironment(IsWindows: true, new FakeReplay(Threw, OtherThrew)));

        Assert.Equal(ExitCodes.Divergent, exitCode);
        Assert.Equal(
            "note: --execute runs code from both solutions on this machine, in a temporary working directory; it is not sandboxed, "
            + "so absolute paths, the registry and the network are still reachable" + Environment.NewLine,
            error);

        // A frontend that cannot replay leaves every result as it was.
        Assert.All(log!.Runs[0].Results, static r => Assert.False(r.TryGetProperty("replay", out string? _)));
    }

    /// <summary>With no environment given, <c>--execute</c> runs on this machine; with no side on .NET Framework it prints the note on any OS.</summary>
    [Fact]
    public void Execute_WithoutAnEnvironment_UsesThisMachine()
    {
        (int exitCode, string error, _) = Compare(execute: true, replay: null, execution: null);

        Assert.Equal(ExitCodes.Divergent, exitCode);
        Assert.StartsWith(ExecutionEnvironment.Note, error, StringComparison.Ordinal);
    }

    /// <summary>Both sides throwing where the model's runs return means the driver's inputs are not the model's (ticket P2-038).</summary>
    [Theory]
    [InlineData(Threw, OtherThrew, "reproduced")]
    [InlineData(Returned, Returned, "not-reproduced")]
    [InlineData(Threw, Threw, "not-constructible")]
    public void Replay_NeverChangesVerdictOrFingerprint(string legacyAnswer, string modernAnswer, string replayed)
    {
        FakeReplay replay = new(legacyAnswer, modernAnswer);

        (int plainExit, _, SarifLog? plain) = Compare(execute: false, replay: null, execution: null);
        (int executedExit, _, SarifLog? executed) = Compare(execute: true, replay, new ExecutionEnvironment(IsWindows: true, replay));

        Assert.Equal(plainExit, executedExit);
        Assert.Equal(
            plain!.Runs[0].Results.Select(static r => (r.RuleId, r.BaselineState, r.PartialFingerprints["resultFingerprint/v1"])),
            executed!.Runs[0].Results.Select(static r => (r.RuleId, r.BaselineState, r.PartialFingerprints["resultFingerprint/v1"])));

        // Only the Divergent is replayed, once, with its own pair and counterexample, and each side's driver runs once.
        (ProcedurePair pair, Counterexample counterexample, string directory) = Assert.Single(replay.Creates);
        Assert.Equal(DivergentIdentity, pair.New);
        Assert.Equal(Counterexample(), counterexample);
        Assert.False(Directory.Exists(directory));
        Assert.StartsWith("equiv-execute-", Path.GetFileName(directory), StringComparison.Ordinal);

        // The drivers start under that temporary folder, not in the caller's working directory (ticket P2-040).
        Assert.Equal([directory], replay.Hosts);
        Assert.Equal(["legacy.exe", "modern.dll"], replay.Starts);
        Result divergent = executed.Runs[0].Results.Single(static r => string.Equals(r.RuleId, "EQ002", StringComparison.Ordinal));
        Assert.Equal(replayed, divergent.GetProperty<string>("replay"));
        Assert.False(executed.Runs[0].Results.Single(static r => string.Equals(r.RuleId, "EQ001", StringComparison.Ordinal)).TryGetProperty("replay", out string? _));
    }

    private static (int ExitCode, string Error, SarifLog? Log) Compare(bool execute, FakeReplay? replay, ExecutionEnvironment? execution)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        InMemoryReportSink sink = new();
        int exitCode = 0;
        string error = CaptureStdErr(() => CaptureStdOut(() => exitCode = CompareCommand.Run(
            Options(legacy.Path, modern.Path, execute), [new FakeFrontend("csharp", _ => true, Match(), replay: replay)], Backend(), sink, NullRunLog.Instance, execution)));
        return (exitCode, error, sink.Log);
    }

    /// <summary>A temporary folder that deletes at once is deleted once.</summary>
    [Fact]
    public void DeleteTemporary_DeletesOnce()
    {
        List<string> calls = [];

        CompareCommand.DeleteTemporary("dir", calls.Add, TimeSpan.Zero);

        Assert.Equal(["dir"], calls);
    }

    /// <summary>A driver that still holds its <c>.exe</c> for a moment only delays the delete (IOException or access denied).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteTemporary_RetriesWhileTheFolderIsHeld(bool denied)
    {
        int calls = 0;

        CompareCommand.DeleteTemporary(
            "dir",
            _ =>
            {
                if (++calls < 3)
                {
                    throw denied ? new UnauthorizedAccessException("Access to the path 'EquivReplay1.exe' is denied.") : new IOException("in use");
                }
            },
            TimeSpan.Zero);

        Assert.Equal(3, calls);
    }

    /// <summary>A folder that stays held is left behind after the last attempt instead of failing the run.</summary>
    [Fact]
    public void DeleteTemporary_GivesUpAfterTheLastAttempt()
    {
        int calls = 0;

        CompareCommand.DeleteTemporary("dir", _ => throw new IOException(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"attempt {++calls}")), TimeSpan.Zero);

        Assert.Equal(CompareCommand.DeleteAttempts, calls);
    }

    /// <summary>Anything but a held folder is still a crash.</summary>
    [Fact]
    public void DeleteTemporary_RethrowsOtherExceptions()
    {
        Assert.Throws<InvalidOperationException>(() => CompareCommand.DeleteTemporary("dir", static _ => throw new InvalidOperationException(), TimeSpan.Zero));
    }

    private static CompareOptions Options(string legacy, string modern, bool execute) =>
        new(legacy, modern, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false, Execute: execute);

    private static MatchResult Match() => new([Pair(DivergentIdentity), Pair(EquivalentIdentity)], [], [], []);

    private static FakeBackend Backend() => new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
    {
        [DivergentIdentity.Value] = new Divergent(Counterexample()),
        [EquivalentIdentity.Value] = new Equivalent(ProofMethod.Bounded),
    });

    /// <summary>A pair the backend must verify: it has no fingerprints, so it is never congruent.</summary>
    private static ProcedurePair Pair(ProcedureIdentity identity)
    {
        IrProcedure body = IrText.Parse($"proc \"{identity.Value}\" () entry B0 B0: ret");
        return new ProcedurePair(identity, identity, body, body);
    }

    private static Counterexample Counterexample() =>
        new(new IrInputs([]), new IrRun(new IrReturned(new IrBitVecValue(32, 1)), [], []), new IrRun(new IrReturned(new IrBitVecValue(32, 2)), [], []));

    private static void CaptureStdOut(Action action)
    {
        TextWriter original = Console.Out;
        using StringWriter writer = new();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static string CaptureStdErr(Action action)
    {
        TextWriter original = Console.Error;
        using StringWriter writer = new();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return writer.ToString();
    }
}
