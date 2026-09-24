using System.Collections.Immutable;
using System.CommandLine;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Cli.Tests;

/// <summary>
/// <see cref="CompareCommand.Run"/> against fakes: routing (ARCHITECTURE.md's router bullet),
/// dry-run, <c>--lower-only</c>, the pipeline that turns a <see cref="MatchResult"/> into a SARIF log, the
/// run properties (lowering census, analysed line counts; ticket M3-014), and the exit codes
/// VERIFICATION-MODEL.md section 6 documents. A test that asserts on console output redirects it and
/// restores it in a `finally`; xUnit runs the [Fact]s in one class sequentially, so the ones that do
/// redirect it never race each other. The "Console" collection also serializes this class against
/// <see cref="ProgramTests"/>, which redirects the console too.
/// </summary>
[Collection("Console")]
public sealed class CompareCommandTests
{
    private static readonly ProcedureIdentity PairIdentity = new("T::Pair()");
    private static readonly ImmutableDictionary<string, Verdict> NoVerdicts = [];

    [Fact]
    public void Create_RejectsNullFrontends()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Create(null!, new FakeBackend(NoVerdicts)));
    }

    [Fact]
    public void Create_RejectsNullBackend()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Create([], null!));
    }

    [Fact]
    public void Create_RequiresLegacyAndModern()
    {
        ParseResult parseResult = CompareCommand.Create([], new FakeBackend(NoVerdicts)).Parse([]);

        Assert.Contains(parseResult.Errors, e => e.Message.Contains("--legacy", StringComparison.Ordinal));
        Assert.Contains(parseResult.Errors, e => e.Message.Contains("--modern", StringComparison.Ordinal));
    }

    /// <summary><c>--fail-on</c> has no parsed default, so <c>--lower-only</c> can tell an explicit one apart; unset means divergent.</summary>
    [Fact]
    public void Create_DefaultsOutAndLeavesFailOnAndLowerOnlyUnset()
    {
        ParseResult parseResult = CompareCommand.Create([], new FakeBackend(NoVerdicts)).Parse(["--legacy", "a.sln", "--modern", "b.sln"]);

        Assert.Empty(parseResult.Errors);
        Assert.Equal("equiv.sarif", parseResult.GetValue<string>("--out"));
        Assert.Null(parseResult.GetValue<string?>("--fail-on"));
        Assert.False(parseResult.GetValue<bool>("--lower-only"));
    }

    [Fact]
    public void Create_ParsesLowerOnly()
    {
        ParseResult parseResult = CompareCommand.Create([], new FakeBackend(NoVerdicts)).Parse(["--legacy", "a.sln", "--modern", "b.sln", "--lower-only"]);

        Assert.Empty(parseResult.Errors);
        Assert.True(parseResult.GetValue<bool>("--lower-only"));
    }

    [Theory]
    [InlineData("divergent", true)]
    [InlineData("unknown", true)]
    [InlineData("never", false)]
    public void Create_FailOnAcceptsOnlyDivergentOrUnknown(string failOn, bool accepted)
    {
        ParseResult parseResult = CompareCommand.Create([], new FakeBackend(NoVerdicts)).Parse(["--legacy", "a.sln", "--modern", "b.sln", "--fail-on", failOn]);

        Assert.Equal(accepted, parseResult.Errors.Count == 0);
    }

    [Fact]
    public void Run_RejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(
            null!, [], new FakeBackend(NoVerdicts), new InMemoryReportSink()));
    }

    [Fact]
    public void Run_RejectsNullFrontends()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(
            new CompareOptions("a.sln", "b.sln", "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            null!, new FakeBackend(NoVerdicts), new InMemoryReportSink()));
    }

    [Fact]
    public void Run_RejectsNullBackend()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(
            new CompareOptions("a.sln", "b.sln", "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [], null!, new InMemoryReportSink()));
    }

    [Fact]
    public void Run_RejectsNullSink()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(
            new CompareOptions("a.sln", "b.sln", "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [], new FakeBackend(NoVerdicts), null!));
    }

    [Fact]
    public void Router_PicksFrontendSupportingBothPaths()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend legacyOnly = new("legacy-only", path => string.Equals(path, legacy.Path, StringComparison.Ordinal));
        FakeFrontend both = new("both", _ => true);

        string output = CaptureStdOut(() =>
        {
            int exitCode = CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: true),
                [legacyOnly, both], new FakeBackend(NoVerdicts), new InMemoryReportSink());
            Assert.Equal(ExitCodes.Success, exitCode);
        });

        Assert.StartsWith($"route: both legacy={legacy.Path} modern={modern.Path} out=out.sarif{Environment.NewLine}", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Router_RejectsWhenNoFrontend()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal($"error: no frontend supports both legacy={legacy.Path} and modern={modern.Path}{Environment.NewLine}", errorOutput, StringComparer.Ordinal);
    }

    [Fact]
    public void Router_RejectsWhenFrontendsDiffer()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend legacyOnly = new("legacy-only", path => string.Equals(path, legacy.Path, StringComparison.Ordinal));
        FakeFrontend modernOnly = new("modern-only", path => string.Equals(path, modern.Path, StringComparison.Ordinal));

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [legacyOnly, modernOnly], new FakeBackend(NoVerdicts), new InMemoryReportSink());

        Assert.Equal(ExitCodes.UsageError, exitCode);
    }

    [Fact]
    public void Compare_MissingFileExits3()
    {
        string legacy = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sln");
        string modern = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sln");
        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy, modern, "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal($"error: file not found (legacy={legacy}, modern={modern}){Environment.NewLine}", errorOutput, StringComparer.Ordinal);
    }

    [Fact]
    public void Compare_MissingModernOnlyExits3()
    {
        using TempFile legacy = new();
        string modern = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sln");
        FakeFrontend frontend = new("csharp", _ => true);

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern, "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink());

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal(0, frontend.AnalyzeCallCount);
    }

    /// <summary>Ticket M3-014 acceptance criterion 10: a dry run loads both sides to count their lines, then stops before verifying or writing.</summary>
    [Fact]
    public void Compare_DryRunPrintsRouteAndLineCountsAndExits0()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []), lines: new AnalysedLines(1200, 1300));
        FakeBackend backend = new(NoVerdicts);
        InMemoryReportSink sink = new();

        string output = CaptureStdOut(() =>
        {
            int exitCode = CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: true),
                [frontend], backend, sink);
            Assert.Equal(ExitCodes.Success, exitCode);
        });

        Assert.Equal(
            $"route: csharp legacy={legacy.Path} modern={modern.Path} out=equiv.sarif{Environment.NewLine}analysed lines of code: legacy=1200 modern=1300{Environment.NewLine}",
            output,
            StringComparer.Ordinal);
        Assert.Equal(1, frontend.AnalyzeCallCount);
        Assert.Empty(backend.Calls);
        Assert.Null(sink.Log);
    }

    /// <summary>Ticket M3-014 acceptance criteria 7, 8 and 11: two counts, printed and in the run properties, never summed, even with no verdicts.</summary>
    [Fact]
    public void Compare_ReportsEachSidesLineCountIndependentlyEvenWithNoResults()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, lines: new AnalysedLines(49_000, 52_000));
        InMemoryReportSink sink = new();
        int exitCode = ExitCodes.UsageError;

        string output = CaptureStdOut(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
            [frontend], new FakeBackend(NoVerdicts), sink));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal($"analysed lines of code: legacy=49000 modern=52000{Environment.NewLine}", output, StringComparer.Ordinal);
        Assert.DoesNotContain("101000", output, StringComparison.Ordinal);
        Run run = sink.Log!.Runs[0];
        Assert.Empty(run.Results);
        Assert.True(run.TryGetSerializedPropertyValue("analysedLinesOfCode", out string? counts));
        Assert.Equal("""{"legacy":49000,"modern":52000}""", counts);
        Assert.True(run.TryGetSerializedPropertyValue("loweringCensus", out string? _));
    }

    [Fact]
    public void LowerOnlyNeverCallsTheBackend()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity added = new("T::Added()");
        ProcedureIdentity removed = new("T::Removed()");
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [added], [removed], []), lines: new AnalysedLines(10, 20));
        FakeBackend backend = new(NoVerdicts);
        InMemoryReportSink sink = new();
        int exitCode = ExitCodes.UsageError;

        string output = CaptureStdOut(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false, LowerOnly: true),
            [frontend], backend, sink));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(backend.Calls);
        Assert.Equal($"analysed lines of code: legacy=10 modern=20{Environment.NewLine}", output, StringComparer.Ordinal);
        Run run = sink.Log!.Runs[0];
        Assert.Equal(["EQ004", "EQ005"], run.Results.Select(static r => r.RuleId), StringComparer.Ordinal);
        Assert.True(run.TryGetSerializedPropertyValue("loweringCensus", out string? census));
        Assert.Equal(
            """{"procedures":{"legacy":2,"modern":2},"matchedPairs":1,"pairsWithoutOpaque":1,"pairsWholeBodyOpaque":0,"pairsCongruent":0,"projectsSkipped":{"legacy":0,"modern":0},"opaqueByReason":{}}""",
            census);
        Assert.True(run.TryGetSerializedPropertyValue("analysedLinesOfCode", out string? _));
    }

    [Fact]
    public void LowerOnlyExits0EvenWhenARegularRunWouldFail()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Divergent(Counterexample()) });

        CompareOptions regular = new(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false);
        Assert.Equal(ExitCodes.Divergent, CompareCommand.Run(regular, [frontend], backend, new InMemoryReportSink()));
        Assert.Equal(ExitCodes.Success, CompareCommand.Run(regular with { LowerOnly = true }, [frontend], backend, new InMemoryReportSink()));
    }

    [Fact]
    public void LowerOnlyStillExits4WhenLoadingFails()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, throwOnAnalyze: new FrontendLoadException(legacy.Path, "workspace failed to open"));
        int exitCode = ExitCodes.Success;

        CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false, LowerOnly: true),
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.LoadFailure, exitCode);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "divergent")]
    [InlineData(false, "unknown")]
    [InlineData(true, "unknown")]
    public void LowerOnlyRejectsBaselineAndFailOn(bool withBaseline, string? failOn)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        using TempFile baseline = new();
        FakeFrontend frontend = new("csharp", _ => true);
        InMemoryReportSink sink = new();
        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", withBaseline ? baseline.Path : null, ConfigPath: null, failOn, DryRun: false, LowerOnly: true),
            [frontend], new FakeBackend(NoVerdicts), sink));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal($"error: --lower-only cannot be combined with --baseline or --fail-on{Environment.NewLine}", errorOutput, StringComparer.Ordinal);
        Assert.Equal(0, frontend.AnalyzeCallCount);
        Assert.Null(sink.Log);
    }

    [Fact]
    public async Task Compare_WritesSarifAndExits0WhenAllEquivalent()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity pairA = new("T::A()");
        ProcedureIdentity pairB = new("T::B()");
        ProcedureIdentity added = new("T::Added()");
        ProcedureIdentity removed = new("T::Removed()");
        MatchResult matchResult = new(
            [Pair(pairA), Pair(pairB)],
            [added],
            [removed],
            []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult, lines: new AnalysedLines(120, 135));
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [pairA.Value] = new Equivalent(ProofMethod.Bounded),
            [pairB.Value] = new Equivalent(ProofMethod.Bounded),
        });
        InMemoryReportSink sink = new();

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [frontend], backend, sink);

        Assert.Equal(ExitCodes.Success, exitCode);
        await VerifyJson(Serialize(sink.Log!));
    }

    [Fact]
    public void Compare_Exits1OnDivergent()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [PairIdentity.Value] = new Divergent(Counterexample()),
        });

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [frontend], backend, new InMemoryReportSink());

        Assert.Equal(ExitCodes.Divergent, exitCode);
    }

    [Fact]
    public void Compare_Exits2OnUnknownWhenFailOnUnknown()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [PairIdentity.Value] = new Unknown(UnknownReason.Timeout, "gave up"),
        });

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "unknown", DryRun: false),
            [frontend], backend, new InMemoryReportSink());

        Assert.Equal(ExitCodes.UnknownPresent, exitCode);
    }

    [Fact]
    public void Compare_Exits0OnUnknownByDefault()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [PairIdentity.Value] = new Unknown(UnknownReason.Timeout, "gave up"),
        });

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [frontend], backend, new InMemoryReportSink());

        Assert.Equal(ExitCodes.Success, exitCode);
    }

    [Fact]
    public void Compare_PairWithoutBodyIsAFrontendBug()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedurePair lowered = Pair(PairIdentity);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded) });

        foreach (ProcedurePair pair in new[] { lowered with { OldBody = null }, lowered with { NewBody = null } })
        {
            FakeFrontend frontend = new("csharp", _ => true, new MatchResult([pair], [], [], []));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
                [frontend], backend, new InMemoryReportSink()));

            Assert.Contains(PairIdentity.Value, exception.Message, StringComparison.Ordinal);
        }

        Assert.Empty(backend.Calls);
    }

    /// <summary>
    /// Ticket M3-013 acceptance criteria 2, 3 and 5 (ADR 0023): a crash on one pair does not end the run.
    /// </summary>
    [Fact]
    public void Compare_PairThatThrows_IsReportedAsNotificationAndOtherPairsVerified()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity ok = new("T::Ok()");
        ProcedureIdentity throwing = new("T::Throws()");
        MatchResult matchResult = new([Pair(ok), Pair(throwing)], [], [], []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        InvalidOperationException exception = new("encoder bug");
        FakeBackend backend = new(
            new Dictionary<string, Verdict>(StringComparer.Ordinal) { [ok.Value] = new Equivalent(ProofMethod.Bounded) },
            new Dictionary<string, Exception>(StringComparer.Ordinal) { [throwing.Value] = exception });
        InMemoryReportSink sink = new();
        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [frontend], backend, sink));

        Assert.Equal(ExitCodes.InternalError, exitCode);
        Run run = sink.Log!.Runs[0];
        Assert.Equal("EQ001", Assert.Single(run.Results).RuleId);
        Assert.Equal([throwing.Value], run.GetProperty<List<string>>("unverified"), StringComparer.Ordinal);
        Invocation invocation = Assert.Single(run.Invocations);
        Assert.False(invocation.ExecutionSuccessful);
        Notification notification = Assert.Single(invocation.ToolExecutionNotifications);
        Assert.Equal(FailureLevel.Error, notification.Level);
        Assert.Equal($"Verifying {throwing.Value} against {throwing.Value} failed: encoder bug", notification.Message.Text);
        Assert.Equal("encoder bug", notification.Exception.Message);
        Assert.Contains($"error: Verifying {throwing.Value} against {throwing.Value} failed: encoder bug", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_PairThatThrows_OutranksNewDivergent()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity divergent = new("T::Divergent()");
        ProcedureIdentity throwing = new("T::Throws()");
        MatchResult matchResult = new([Pair(divergent), Pair(throwing)], [], [], []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        FakeBackend backend = new(
            new Dictionary<string, Verdict>(StringComparer.Ordinal) { [divergent.Value] = new Divergent(Counterexample()) },
            new Dictionary<string, Exception>(StringComparer.Ordinal) { [throwing.Value] = new InvalidOperationException("boom") });

        int exitCode = ExitCodes.Success;
        CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [frontend], backend, new InMemoryReportSink()));

        Assert.Equal(ExitCodes.InternalError, exitCode);
    }

    [Fact]
    public void Compare_PairThatThrows_OutranksNewUnknownWithFailOnUnknown()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity unknown = new("T::Unknown()");
        ProcedureIdentity throwing = new("T::Throws()");
        MatchResult matchResult = new([Pair(unknown), Pair(throwing)], [], [], []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        FakeBackend backend = new(
            new Dictionary<string, Verdict>(StringComparer.Ordinal) { [unknown.Value] = new Unknown(UnknownReason.Timeout, "gave up") },
            new Dictionary<string, Exception>(StringComparer.Ordinal) { [throwing.Value] = new InvalidOperationException("boom") });
        int exitCode = ExitCodes.Success;

        CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "unknown", DryRun: false),
            [frontend], backend, new InMemoryReportSink()));

        Assert.Equal(ExitCodes.InternalError, exitCode);
    }

    [Fact]
    public void Compare_OperationCanceled_Propagates()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        FakeBackend backend = new(NoVerdicts, new Dictionary<string, Exception>(StringComparer.Ordinal) { [PairIdentity.Value] = new OperationCanceledException() });

        Assert.Throws<OperationCanceledException>(() => CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [frontend], backend, new InMemoryReportSink()));
    }

    /// <summary>
    /// CA2201 forbids constructing <see cref="OutOfMemoryException"/> directly (it is reserved for the runtime), so
    /// this uses <see cref="InsufficientMemoryException"/>, a genuine <see cref="OutOfMemoryException"/> subclass the
    /// BCL provides for exactly this: raising the same family of exception from ordinary code.
    /// </summary>
    [Fact]
    public void Compare_OutOfMemory_Propagates()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        FakeBackend backend = new(NoVerdicts, new Dictionary<string, Exception>(StringComparer.Ordinal) { [PairIdentity.Value] = new InsufficientMemoryException() });

        Assert.Throws<InsufficientMemoryException>(() => CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [frontend], backend, new InMemoryReportSink()));
    }

    [Fact]
    public void Compare_PairThatThrows_KeepsBaselineDivergentAsUnchanged()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string baselinePath = Path.GetTempFileName();
        try
        {
            SarifReportWriter.Write([new VerificationResult(PairIdentity, new Divergent(Counterexample()))]).Save(baselinePath);
            MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
            FakeFrontend frontend = new("csharp", _ => true, matchResult);
            FakeBackend backend = new(NoVerdicts, new Dictionary<string, Exception>(StringComparer.Ordinal) { [PairIdentity.Value] = new InvalidOperationException("boom") });
            InMemoryReportSink sink = new();

            int exitCode = CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", baselinePath, ConfigPath: null, "divergent", DryRun: false),
                [frontend], backend, sink);

            Assert.Equal(ExitCodes.InternalError, exitCode);
            Result carried = Assert.Single(sink.Log!.Runs[0].Results);
            Assert.Equal(BaselineState.Unchanged, carried.BaselineState);
            Assert.Equal("EQ002", carried.RuleId);
            Assert.True(carried.GetProperty<bool>("unverified"));
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void Compare_Exits4OnFrontendLoadException()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FrontendLoadException exception = new(legacy.Path, "workspace failed to open");
        FakeFrontend frontend = new("csharp", _ => true, throwOnAnalyze: exception);

        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.LoadFailure, exitCode);
        Assert.Equal(exception.Message + Environment.NewLine, errorOutput, StringComparer.Ordinal);
    }

    [Fact]
    public void Compare_BaselineSuppressesUnchangedDivergent()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string baselinePath = Path.GetTempFileName();
        try
        {
            Divergent divergent = new(Counterexample());
            SarifLog baseline = SarifReportWriter.Write([new VerificationResult(PairIdentity, divergent)]);
            baseline.Save(baselinePath);

            MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
            FakeFrontend frontend = new("csharp", _ => true, matchResult);
            FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = divergent });
            InMemoryReportSink sink = new();

            int exitCode = CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", baselinePath, ConfigPath: null, "divergent", DryRun: false),
                [frontend], backend, sink);

            Assert.Equal(ExitCodes.Success, exitCode);
            Result result = Assert.Single(sink.Log!.Runs[0].Results);
            Assert.Equal(BaselineState.Unchanged, result.BaselineState);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void Compare_BaselineFixedDivergenceExits0()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string baselinePath = Path.GetTempFileName();
        try
        {
            SarifLog baseline = SarifReportWriter.Write([new VerificationResult(PairIdentity, new Divergent(Counterexample()))]);
            baseline.Save(baselinePath);

            // A rule-id change (Divergent -> Equivalent) for an identity already in the baseline is
            // always `new` per M1-004's BaselineComputer, but it must not exit 1: only a *new*
            // Divergent counts, and this one is now Equivalent.
            MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
            FakeFrontend frontend = new("csharp", _ => true, matchResult);
            FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded) });

            int exitCode = CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", baselinePath, ConfigPath: null, "divergent", DryRun: false),
                [frontend], backend, new InMemoryReportSink());

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void Compare_UsesConfigBoundAndTimeout()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string configPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(configPath, """{ "bound": 7, "timeoutMs": 12000, "callIdentityRenames": { "Old::M": "New::M" } }""");
            MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
            FakeFrontend frontend = new("csharp", _ => true, matchResult);
            FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded) });

            int exitCode = CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, configPath, "divergent", DryRun: false),
                [frontend], backend, new InMemoryReportSink());

            Assert.Equal(ExitCodes.Success, exitCode);
            VerificationOptions options = Assert.Single(backend.Calls);
            Assert.Equal(7, options.Bound);
            Assert.Equal(12000, options.TimeoutMs);
            Assert.Equal("New::M", options.CallIdentityMap["Old::M"]);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Compare_MissingConfigFileExits3()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string configPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        FakeFrontend frontend = new("csharp", _ => true);

        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, configPath, "divergent", DryRun: false),
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal($"error: file not found (config={configPath}){Environment.NewLine}", errorOutput, StringComparer.Ordinal);
        Assert.Equal(0, frontend.AnalyzeCallCount);
    }

    [Fact]
    public void Compare_MissingBaselineFileExits3()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string baselinePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sarif");
        FakeFrontend frontend = new("csharp", _ => true);

        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", baselinePath, ConfigPath: null, "divergent", DryRun: false),
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal($"error: file not found (baseline={baselinePath}){Environment.NewLine}", errorOutput, StringComparer.Ordinal);
        Assert.Equal(0, frontend.AnalyzeCallCount);
    }

    [Fact]
    public void Compare_InvalidConfigJsonExits3()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string configPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(configPath, "{ not json");
            FakeFrontend frontend = new("csharp", _ => true);

            int exitCode = ExitCodes.Success;

            string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, configPath, "divergent", DryRun: false),
                [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

            Assert.Equal(ExitCodes.UsageError, exitCode);
            Assert.False(string.IsNullOrWhiteSpace(errorOutput));
            Assert.Equal(0, frontend.AnalyzeCallCount);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Compare_InvalidBaselineJsonExits3()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string baselinePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(baselinePath, "{ not json");
            FakeFrontend frontend = new("csharp", _ => true);

            int exitCode = ExitCodes.Success;

            string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", baselinePath, ConfigPath: null, "divergent", DryRun: false),
                [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

            Assert.Equal(ExitCodes.UsageError, exitCode);
            Assert.StartsWith($"error: '{baselinePath}' is not a valid SARIF log: ", errorOutput, StringComparison.Ordinal);
            Assert.Equal(0, frontend.AnalyzeCallCount);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void Compare_WarnsOnInvalidConfigValues()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string configPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(configPath, """{ "bound": 0 }""");
            MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
            FakeFrontend frontend = new("csharp", _ => true, matchResult);
            FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded) });
            int exitCode = ExitCodes.Success;

            string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, configPath, "divergent", DryRun: false),
                [frontend], backend, new InMemoryReportSink()));

            Assert.Equal(ExitCodes.Success, exitCode);
            Assert.Contains("CFG002", errorOutput, StringComparison.Ordinal);
            Assert.Equal(EquivConfig.Default.Bound, Assert.Single(backend.Calls).Bound);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void AnUnboundMethodIsUnknownUnbound()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedurePair pair = Pair(PairIdentity) with { NewBody = UnboundBody(PairIdentity) };
        FakeBackend backend = new(NoVerdicts);
        InMemoryReportSink sink = new();

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "unknown", DryRun: false),
            [new FakeFrontend("csharp", _ => true, new MatchResult([pair], [], [], []))], backend, sink);

        Assert.Equal(ExitCodes.UnknownPresent, exitCode);
        Assert.Empty(backend.Calls);
        Result result = Assert.Single(sink.Log!.Runs[0].Results);
        Assert.Equal("EQ003", result.RuleId);
        Assert.Equal("unbound", result.GetProperty<string>("unknownReason"));
        Assert.Contains("modern: unbound at a.cs 3:5; modern: unbound at a.cs 4:1", result.Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnboundLegacyBodyIsUnknownUnbound()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedurePair pair = Pair(PairIdentity) with { OldBody = UnboundBody(PairIdentity) };
        InMemoryReportSink sink = new();

        CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [new FakeFrontend("csharp", _ => true, new MatchResult([pair], [], [], []))], new FakeBackend(NoVerdicts), sink);

        Assert.Contains("legacy: unbound at a.cs 3:5", Assert.Single(sink.Log!.Runs[0].Results).Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SkippedProjectProceduresAreUnverifiedNotAddedOrRemoved()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new MatchResult([Pair(PairIdentity)], [], [], []) with
        {
            LegacySkipped = [new UnverifiedProject("Lib", "Lib.Assembly", IsCSharp: true, ["CS0246: missing", "CS0012: other"], [new ProcedureIdentity("L::X()")])],
            ModernSkipped =
            [
                new UnverifiedProject("Native", "Native", IsCSharp: false, ["Cannot open project 'Native.vcxproj'"], []),
                new UnverifiedProject(string.Empty, string.Empty, IsCSharp: true, ["project file could not be evaluated"], []),
            ],
        };
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded) });
        InMemoryReportSink sink = new();
        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [new FakeFrontend("csharp", _ => true, matchResult)], backend, sink));

        Assert.Equal(ExitCodes.LoadFailure, exitCode);
        Run run = sink.Log!.Runs[0];
        Assert.Equal("EQ001", Assert.Single(run.Results).RuleId);
        Assert.Equal(["L::X()"], run.GetProperty<List<string>>("unverified"), StringComparer.Ordinal);
        Invocation invocation = Assert.Single(run.Invocations);
        Assert.False(invocation.ExecutionSuccessful);
        Assert.Equal(
            [
                (FailureLevel.Error, "legacy project 'Lib' (assembly 'Lib.Assembly') was skipped: CS0246: missing; CS0012: other"),
                (FailureLevel.Warning, "modern project 'Native' (assembly 'Native') was skipped: Cannot open project 'Native.vcxproj'"),
                (FailureLevel.Error, "a modern project the workspace did not name was skipped: project file could not be evaluated"),
            ],
            invocation.ToolExecutionNotifications.Select(static n => (n.Level, n.Message.Text)));
        Assert.Equal(
            """{"legacy":1,"modern":2}""",
            Newtonsoft.Json.JsonConvert.SerializeObject(run.GetProperty<Dictionary<string, object>>("loweringCensus")["projectsSkipped"]));
        Assert.Contains("error: legacy project 'Lib'", errorOutput, StringComparison.Ordinal);
        Assert.Contains("warning: modern project 'Native'", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonCSharpProjectAloneDoesNotChangeTheExitCode()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new MatchResult([Pair(PairIdentity)], [], [], []) with
        {
            LegacySkipped = [new UnverifiedProject("Native", "Native", IsCSharp: false, ["Cannot open project 'Native.vcxproj'"], [])],
        };
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded) });
        InMemoryReportSink sink = new();
        int exitCode = -1;

        CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [new FakeFrontend("csharp", _ => true, matchResult)], backend, sink));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(Assert.Single(sink.Log!.Runs[0].Invocations).ExecutionSuccessful);
    }

    [Theory]
    [InlineData("divergent", true)]
    [InlineData("unknown", false)]
    public void ExitCodePrecedenceIsFourThenVerdicts(string failOn, bool divergent)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        Verdict verdict = divergent ? new Divergent(Counterexample()) : new Unknown(UnknownReason.Timeout, "gave up");
        MatchResult matchResult = new MatchResult([Pair(PairIdentity)], [], [], []) with
        {
            ModernSkipped = [new UnverifiedProject("Lib", "Lib", IsCSharp: true, ["CS0246: missing"], [])],
        };
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = verdict });
        int exitCode = -1;

        CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, failOn, DryRun: false),
            [new FakeFrontend("csharp", _ => true, matchResult)], backend, new InMemoryReportSink()));

        Assert.Equal(ExitCodes.LoadFailure, exitCode);
    }

    [Fact]
    public void LowerOnlyExits4WhenACSharpProjectWasSkipped()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new MatchResult([Pair(PairIdentity)], [], [], []) with
        {
            LegacySkipped = [new UnverifiedProject("Lib", "Lib", IsCSharp: true, ["CS0246: missing"], [])],
        };
        FakeBackend backend = new(NoVerdicts);
        int exitCode = -1;

        CaptureStdErr(() => CaptureStdOut(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false, LowerOnly: true),
            [new FakeFrontend("csharp", _ => true, matchResult)], backend, new InMemoryReportSink())));

        Assert.Equal(ExitCodes.LoadFailure, exitCode);
        Assert.Empty(backend.Calls);
    }

    [Fact]
    public void ABaselineResultInASkippedProjectIsCarriedUnchanged()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string baselinePath = Path.GetTempFileName();
        try
        {
            ProcedureIdentity skippedIdentity = new("L::X()");
            SarifReportWriter.Write([new VerificationResult(skippedIdentity, new Divergent(Counterexample()))]).Save(baselinePath);
            MatchResult matchResult = new MatchResult([], [], [], []) with
            {
                LegacySkipped = [new UnverifiedProject("Lib", "Lib", IsCSharp: true, ["CS0246: missing"], [skippedIdentity])],
            };
            InMemoryReportSink sink = new();

            CaptureStdErr(() => CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", baselinePath, ConfigPath: null, "divergent", DryRun: false),
                [new FakeFrontend("csharp", _ => true, matchResult)], new FakeBackend(NoVerdicts), sink));

            Result carried = Assert.Single(sink.Log!.Runs[0].Results);
            Assert.Equal(BaselineState.Unchanged, carried.BaselineState);
            Assert.True(carried.GetProperty<bool>("unverified"));
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    private static IrProcedure UnboundBody(ProcedureIdentity identity) => new(
        identity,
        [],
        ReturnType: null,
        [
            new IrBlock(
                new IrBlockId(0),
                [
                    new IrOpaque(Target: null, Unknown.UnboundOpaqueReason, new SourceSpan("a.cs", 3, 5, 3, 9)),
                    new IrOpaque(Target: null, "Invalid", new SourceSpan("a.cs", 3, 7, 3, 8)),
                    new IrOpaque(Target: null, Unknown.UnboundOpaqueReason, new SourceSpan("a.cs", 4, 1, 4, 2)),
                ],
                new IrReturn(Value: null, [])),
        ],
        new IrBlockId(0));

    private static ProcedurePair Pair(ProcedureIdentity identity)
    {
        IrProcedure body = IrText.Parse($"proc \"{identity.Value}\" () entry B0 B0: ret");
        return new ProcedurePair(identity, identity, body, body);
    }

    private static Counterexample Counterexample() =>
        new(new IrInputs([new IrBitVecValue(32, 0)]), Run(1), Run(2));

    private static IrRun Run(int returned) => new(new IrReturned(new IrBitVecValue(32, (ulong)returned)), [], []);

    private static string CaptureStdOut(Action action)
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

        return writer.ToString();
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

    private static string Serialize(SarifLog log)
    {
        string path = Path.GetTempFileName();
        try
        {
            log.Save(path);
            return File.ReadAllText(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
