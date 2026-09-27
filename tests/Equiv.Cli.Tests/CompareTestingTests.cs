using System.CommandLine;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Execute.Testing;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// <c>equiv compare --execute</c> on an Unknown pair against fakes (ADR 0035 decision 3; ticket P1-008): the pair is tested
/// on generated inputs, stays Unknown with <c>differentialTesting</c>, or becomes EQ002 observed when the runtimes differ;
/// <c>--test-target</c> and <c>--test-budget</c> are validated and do nothing without <c>--execute</c>.
/// </summary>
[Collection("Console")]
public sealed class CompareTestingTests
{
    private const string One = "[\"Returned\",1]";

    private const string Two = "[\"Returned\",2]";

    private static readonly ProcedureIdentity UnknownIdentity = new("T::Unknown()");

    private static readonly ProcedureIdentity EquivalentIdentity = new("T::Equivalent()");

    private static readonly Counterexample Candidate =
        new(new IrInputs([]), new IrRun(new IrReturned(new IrBitVecValue(32, 1)), [], []), new IrRun(new IrReturned(new IrBitVecValue(32, 2)), [], []));

    [Fact]
    public void Options_NoOpWithoutExecute()
    {
        FakeReplay replay = new(One, Two);

        (int plainExit, _, SarifLog? plain) = Compare(execute: false, replay, testing: null);
        (int optionsExit, _, SarifLog? options) = Compare(execute: false, replay, new TestingOptions(0.5, 3, TimeSpan.FromSeconds(1)));

        Assert.Equal(plainExit, optionsExit);
        Assert.Equal(Normalized(plain!), Normalized(options!));
        Assert.Empty(replay.Plans);
        Assert.Empty(replay.Starts);
    }

    [Fact]
    public void Execute_TestsEachUnknownAndLeavesItUnknown()
    {
        FakeReplay replay = new(One, One);

        (int plainExit, _, SarifLog? plain) = Compare(execute: false, replay: null, testing: null);
        (int exitCode, _, SarifLog? log) = Compare(execute: true, replay, testing: null);

        Assert.Equal(plainExit, exitCode);
        (ProcedurePair pair, Counterexample? candidate, string directory) = Assert.Single(replay.Plans);
        Assert.Equal(UnknownIdentity, pair.New);
        Assert.Equal(Candidate, candidate);
        Assert.False(Directory.Exists(directory));
        Result unknown = log!.Runs[0].Results.Single(static r => string.Equals(r.RuleId, "EQ003", StringComparison.Ordinal));
        Dictionary<string, object> testing = unknown.GetProperty<Dictionary<string, object>>("differentialTesting");
        Assert.Equal((1_000L, 1L, 0L, "target"), ((long)testing["inputs"], (long)testing["species"], (long)testing["singletons"], (string)testing["stoppedBy"]));
        Assert.EndsWith("Tested on 1000 inputs; estimated chance the next input shows new behaviour: 0 (equiv generators, not a proof).", unknown.Message.Text, StringComparison.Ordinal);
        Assert.Equal(
            plain!.Runs[0].Results.Select(static r => (r.RuleId, r.PartialFingerprints["resultFingerprint/v1"])),
            log.Runs[0].Results.Select(static r => (r.RuleId, r.PartialFingerprints["resultFingerprint/v1"])));
        Assert.Equal(1, (int)log.Runs[0].GetProperty<Newtonsoft.Json.Linq.JObject>("loweringCensus")["unknownByScope"]!["method"]!);
    }

    [Fact]
    public void Execute_AnObservedDivergenceIsEq002Observed()
    {
        FakeReplay replay = new(One, Two);
        SarifLog baseline = Compare(execute: false, replay: null, testing: null).Log!;

        (int exitCode, _, SarifLog? log) = Compare(execute: true, replay, testing: null, baseline);

        Assert.Equal(ExitCodes.Divergent, exitCode);
        Result observed = log!.Runs[0].Results.Single(static r => string.Equals(r.RuleId, "EQ002", StringComparison.Ordinal));
        Assert.Equal("observed", observed.GetProperty<string>("proofMethod"));
        Assert.Equal("inputs() culture(invariant)", observed.GetProperty<string>("model"));
        Assert.Equal(BaselineState.New, observed.BaselineState);
        Assert.EndsWith("legacy(returned 1) modern(returned 2)", observed.Message.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Runs[0].Results, static r => string.Equals(r.RuleId, "EQ003", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_ParsesTheTestingOptionsAndHandsThemToTheTester()
    {
        FakeReplay replay = new(One, One);
        using TempFile legacy = new();
        using TempFile modern = new();
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P1-008-{Guid.NewGuid():N}.sarif");
        Command command = CompareCommand.Create(
            [new FakeFrontend("csharp", _ => true, Match(), replay: replay)], Backend(), new ExecutionEnvironment(IsWindows: true, replay));
        try
        {
            int exitCode = 0;
            _ = CaptureStdErr(() => CaptureStdOut(() => exitCode = command.Parse(
                ["--legacy", legacy.Path, "--modern", modern.Path, "--out", outPath, "--execute", "--test-target", "0.5", "--test-budget", "7,30"]).Invoke()));

            Assert.Equal(ExitCodes.Success, exitCode);
            Result unknown = SarifLog.Load(outPath).Runs[0].Results.Single(static r => string.Equals(r.RuleId, "EQ003", StringComparison.Ordinal));
            Dictionary<string, object> testing = unknown.GetProperty<Dictionary<string, object>>("differentialTesting");
            Assert.Equal((7L, "budget"), ((long)testing["inputs"], (string)testing["stoppedBy"]));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Theory]
    [InlineData("--test-target", "2", TestingOptions.TargetUsage)]
    [InlineData("--test-target", "nope", TestingOptions.TargetUsage)]
    [InlineData("--test-budget", "0", TestingOptions.BudgetUsage)]
    [InlineData("--test-budget", "10,-1", TestingOptions.BudgetUsage)]
    public void Options_RejectNonsenseWithExitThree(string option, string value, string usage)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        int exitCode = 0;

        string error = CaptureStdErr(() => exitCode = Program.Run(
            ["compare", "--legacy", legacy.Path, "--modern", modern.Path, option, value], [], new FakeBackend(new Dictionary<string, Verdict>(StringComparer.Ordinal))));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains(usage, error, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Error, SarifLog? Log) Compare(bool execute, FakeReplay? replay, TestingOptions? testing, SarifLog? baseline = null)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        using TempFile baselineFile = new();
        baseline?.Save(baselineFile.Path);

        InMemoryReportSink sink = new();
        int exitCode = 0;
        CompareOptions options = new(legacy.Path, modern.Path, "equiv.sarif", baseline is null ? null : baselineFile.Path, ConfigPath: null, FailOn: null, DryRun: false, Execute: execute)
        {
            Testing = testing ?? TestingOptions.Default,
        };
        string error = CaptureStdErr(() => CaptureStdOut(() => exitCode = CompareCommand.Run(
            options, [new FakeFrontend("csharp", _ => true, Match(), replay: replay)], Backend(), sink, new ExecutionEnvironment(IsWindows: true, replay ?? new FakeReplay(One, One)))));
        return (exitCode, error, sink.Log);
    }

    private static List<(string RuleId, string Message, string Fingerprint)> Normalized(SarifLog log) =>
        [.. log.Runs[0].Results.Select(static r => (r.RuleId, r.Message.Text, r.PartialFingerprints["resultFingerprint/v1"]))];

    private static MatchResult Match() => new([Pair(UnknownIdentity), Pair(EquivalentIdentity)], [], [], []);

    private static FakeBackend Backend() => new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
    {
        [UnknownIdentity.Value] = new Unknown(UnknownReason.Abstraction, "the divergence depends on opaque:f") { Candidate = Candidate },
        [EquivalentIdentity.Value] = new Equivalent(ProofMethod.Bounded),
    });

    /// <summary>A pair the backend must verify: it has no fingerprints, so it is never congruent.</summary>
    private static ProcedurePair Pair(ProcedureIdentity identity)
    {
        IrProcedure body = IrText.Parse($"proc \"{identity.Value}\" () entry B0 B0: ret");
        return new ProcedurePair(identity, identity, body, body);
    }

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
