using System.Collections.Immutable;
using System.CommandLine;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;
using Equiv.Verify.Z3;

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

    /// <summary><c>--chc-int-mode</c> is on unless given as false (ticket P1-001).</summary>
    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "--chc-int-mode", "false" }, false)]
    [InlineData(new[] { "--chc-int-mode", "true" }, true)]
    public void Create_ParsesChcIntModeOnByDefault(string[] flag, bool expected)
    {
        ParseResult parseResult = CompareCommand.Create([], new FakeBackend(NoVerdicts)).Parse(["--legacy", "a.sln", "--modern", "b.sln", .. flag]);

        Assert.Empty(parseResult.Errors);
        Assert.Equal(expected, parseResult.GetValue<bool>("--chc-int-mode"));
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
            null!, [], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));
    }

    [Fact]
    public void Run_RejectsNullFrontends()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(
            new CompareOptions("a.sln", "b.sln", "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            null!, new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));
    }

    [Fact]
    public void Run_RejectsNullBackend()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(
            new CompareOptions("a.sln", "b.sln", "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [], null!, new InMemoryReportSink(), NullRunLog.Instance));
    }

    [Fact]
    public void Run_RejectsNullSink()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(
            new CompareOptions("a.sln", "b.sln", "out.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [], new FakeBackend(NoVerdicts), null!, NullRunLog.Instance));
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
                [legacyOnly, both], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance);
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
            [], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));

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
            [legacyOnly, modernOnly], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance);

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
            [], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));

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
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance);

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
                [frontend], backend, sink, NullRunLog.Instance);
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
            [frontend], new FakeBackend(NoVerdicts), sink, NullRunLog.Instance));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            $"analysed lines of code: legacy=49000 modern=52000{Environment.NewLine}review list: 0 groups for 0 flagged results{Environment.NewLine}",
            output,
            StringComparer.Ordinal);
        Assert.DoesNotContain("101000", output, StringComparison.Ordinal);
        Run run = sink.Log!.Runs[0];
        Assert.Empty(run.Results);
        Assert.True(run.TryGetSerializedPropertyValue("analysedLinesOfCode", out string? counts));
        Assert.Equal("""{"legacy":49000,"modern":52000}""", counts);
        Assert.True(run.TryGetSerializedPropertyValue("loweringCensus", out string? _));
    }

    [Fact]
    public void Compare_ListsEachSidesProjectsNotBuiltOnceInARunProperty()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, legacyNotBuilt: ["Example.Site", "_build"]);
        InMemoryReportSink sink = new();
        int exitCode = ExitCodes.UsageError;

        _ = CaptureStdOut(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
            [frontend], new FakeBackend(NoVerdicts), sink, NullRunLog.Instance));

        // P2-013: a project the solution does not build is neither loaded nor skipped, so it does not fail the run.
        Assert.Equal(ExitCodes.Success, exitCode);
        Run run = sink.Log!.Runs[0];
        Assert.Empty(run.Invocations?.SelectMany(static i => i.ToolExecutionNotifications ?? []) ?? []);
        Assert.True(run.TryGetSerializedPropertyValue("projectsNotBuilt", out string? notBuilt));
        Assert.Equal("""{"legacy":["Example.Site","_build"],"modern":[]}""", notBuilt);
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
            [frontend], backend, sink, NullRunLog.Instance));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(backend.Calls);
        Assert.Equal($"analysed lines of code: legacy=10 modern=20{Environment.NewLine}", output, StringComparer.Ordinal);
        Run run = sink.Log!.Runs[0];
        Assert.Equal(["EQ004", "EQ005"], run.Results.Select(static r => r.RuleId), StringComparer.Ordinal);
        Assert.True(run.TryGetSerializedPropertyValue("loweringCensus", out string? census));
        Assert.Equal(
            """{"procedures":{"legacy":2,"modern":2},"matchedPairs":1,"pairsWithoutOpaque":1,"pairsWholeBodyOpaque":0,"pairsCongruent":0,"projectsSkipped":{"legacy":0,"modern":0},"opaqueByReason":{},"changedPairs":1,"changedPairsWithoutOpaque":1,"changedPairsWholeBodyOpaque":0,"changedReasonSets":{"":1},"runtimeChangeCalls":{"callSites":{"legacy":0,"modern":0},"distinctMembers":{"legacy":0,"modern":0},"pairsWithAny":{"legacy":0,"modern":0}},"externalCallees":{"legacy":[],"modern":[]}}""",
            census);
        Assert.True(run.TryGetSerializedPropertyValue("analysedLinesOfCode", out string? _));

        // Ticket P2-064 criterion 3: --lower-only reaches no verdict, so it lists no review groups and prints none.
        Assert.False(run.TryGetSerializedPropertyValue("reviewList", out string? _));
    }

    /// <summary>
    /// Ticket P2-064 criteria 3 and 4: after the line counts, one line with the group and flagged-result counts and one
    /// line per group, highest rank first, and the same groups in <c>run.properties.reviewList</c>.
    /// </summary>
    [Fact]
    public void Prints_review_list_after_line_counts()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity[] swapped = [new("T::A()"), new("T::B()"), new("T::C()")];
        ProcedureIdentity slow = new("T::Slow()");
        Counterexample swap = Counterexample() with
        {
            Old = Run(1) with { Trace = [new IrCallRecord(new CallIdentity("N::Old()"), [])] },
            New = Run(2) with { Trace = [new IrCallRecord(new CallIdentity("N::New()"), [])] },
        };
        Dictionary<string, Verdict> verdicts = new(StringComparer.Ordinal)
        {
            [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded),
            [slow.Value] = new Unknown(UnknownReason.Timeout, "slow"),
        };
        foreach (ProcedureIdentity identity in swapped)
        {
            verdicts[identity.Value] = new Divergent(swap);
        }

        FakeFrontend frontend = new(
            "csharp", _ => true, new MatchResult([Pair(PairIdentity), Pair(slow), .. swapped.Select(Pair)], [new ProcedureIdentity("T::Added()")], [], []), lines: new AnalysedLines(10, 20));
        InMemoryReportSink sink = new();
        int exitCode = ExitCodes.Success;

        string output = CaptureStdOut(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
            [frontend], new FakeBackend(verdicts), sink, NullRunLog.Instance));

        Assert.Equal(ExitCodes.Divergent, exitCode);
        Assert.Equal(
            [
                "analysed lines of code: legacy=10 modern=20",
                "review list: 2 groups for 4 flagged results",
                "  EQ002 count=3 rank=60.006 calls:N::New()|N::Old()",
                "  EQ003 count=1 rank=0.002 timeout",
                string.Empty,
            ],
            output.Split(Environment.NewLine),
            StringComparer.Ordinal);
        Run run = sink.Log!.Runs[0];
        Assert.True(run.TryGetSerializedPropertyValue("reviewList", out string? list));
        Assert.Equal(
            """[{"group":"calls:N::New()|N::Old()","ruleId":"EQ002","rank":60.006,"count":3,"identities":["T::A()","T::B()","T::C()"]},{"group":"timeout","ruleId":"EQ003","rank":0.002,"count":1,"identities":["T::Slow()"]}]""",
            list);
        Assert.Equal(
            [-1.0, 0.002, 60.006, 60.006, 60.006, -1.0],
            run.Results.Select(static r => r.Rank));
    }

    [Fact]
    public void LowerOnlyExits0EvenWhenARegularRunWouldFail()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Divergent(Counterexample()) });

        CompareOptions regular = new(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false);
        Assert.Equal(ExitCodes.Divergent, CompareCommand.Run(regular, [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance));
        Assert.Equal(ExitCodes.Success, CompareCommand.Run(regular with { LowerOnly = true }, [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance));
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
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));

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
            [frontend], new FakeBackend(NoVerdicts), sink, NullRunLog.Instance));

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
        FakeFrontend frontend = new(
            "csharp",
            _ => true,
            matchResult,
            lines: new AnalysedLines(120, 135),
            legacyRuntimes: [("App", "net48", "attribute"), ("Shared", "net48", "host")],
            modernRuntimes: [("App", "net8.0", "attribute"), ("Shared", "netstandard2.0", "unhosted")]);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [pairA.Value] = new Equivalent(ProofMethod.Bounded),
            [pairB.Value] = new Equivalent(ProofMethod.Bounded),
        });
        InMemoryReportSink sink = new();

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [frontend], backend, sink, NullRunLog.Instance);

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
            [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance);

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
            [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance);

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
            [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance);

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
                [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance));

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
            [frontend], backend, sink, NullRunLog.Instance));

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

    /// <summary>
    /// P2-011: a pair the frontend could not lower takes M3-013's failure path, in <c>--lower-only</c> and full runs
    /// alike. It is matched, so <c>matchedPairs</c> counts it, but it has no lowered body for <c>pairsWithoutOpaque</c>
    /// or <c>pairsWholeBodyOpaque</c> to count; the other pair is counted and verified as usual.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compare_PairThatFailedToLower_IsReportedAsNotificationAndOtherPairsCounted(bool lowerOnly)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity ok = new("T::Ok()");
        ProcedureIdentity throwing = new("T::Throws()");
        MatchResult matchResult = new([Pair(ok)], [], [], [])
        {
            LoweringFailures = [new LoweringFailure(throwing, throwing, new KeyNotFoundException("lowering bug"))],
        };
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [ok.Value] = new Equivalent(ProofMethod.Bounded) });
        InMemoryReportSink sink = new();
        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false, LowerOnly: lowerOnly),
            [frontend], backend, sink, NullRunLog.Instance));

        Assert.Equal(ExitCodes.InternalError, exitCode);
        Run run = sink.Log!.Runs[0];
        Assert.Equal(lowerOnly ? 0 : 1, run.Results.Count);
        Assert.Equal([throwing.Value], run.GetProperty<List<string>>("unverified"), StringComparer.Ordinal);
        Assert.True(run.TryGetSerializedPropertyValue("loweringCensus", out string? census));
        Assert.Equal(
            """{"procedures":{"legacy":2,"modern":2},"matchedPairs":2,"pairsWithoutOpaque":1,"pairsWholeBodyOpaque":0,"pairsCongruent":0,"projectsSkipped":{"legacy":0,"modern":0},"opaqueByReason":{},"changedPairs":1,"changedPairsWithoutOpaque":1,"changedPairsWholeBodyOpaque":0,"changedReasonSets":{"":1},"runtimeChangeCalls":{"callSites":{"legacy":0,"modern":0},"distinctMembers":{"legacy":0,"modern":0},"pairsWithAny":{"legacy":0,"modern":0}},"externalCallees":{"legacy":[],"modern":[]}""" + (lowerOnly ? "}" : ""","unknownByScope":{"line":0,"method":0}}"""),
            census);
        Invocation invocation = Assert.Single(run.Invocations);
        Assert.False(invocation.ExecutionSuccessful);
        Notification notification = Assert.Single(invocation.ToolExecutionNotifications);
        Assert.Equal(FailureLevel.Error, notification.Level);
        Assert.Equal($"Lowering {throwing.Value} against {throwing.Value} failed: lowering bug", notification.Message.Text);
        Assert.Equal(typeof(KeyNotFoundException).FullName, notification.Exception.Kind);
        Assert.Contains($"error: Lowering {throwing.Value} against {throwing.Value} failed: lowering bug", errorOutput, StringComparison.Ordinal);
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
            [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance));

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
            [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance));

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
            [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance));
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
            [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance));
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
                [frontend], backend, sink, NullRunLog.Instance);

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
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));

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
                [frontend], backend, sink, NullRunLog.Instance);

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
                [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance);

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
                [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance);

            Assert.Equal(ExitCodes.Success, exitCode);
            VerificationOptions options = Assert.Single(backend.Calls);
            Assert.Equal(7, options.Bound);
            Assert.Equal(12000, options.TimeoutMs);
            Assert.Equal("New::M", options.CallIdentityMap["Old::M"]);
            Assert.True(options.ChcIntMode);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    /// <summary>Ticket P1-001: <c>--chc-int-mode false</c> reaches the backend.</summary>
    [Fact]
    public void Compare_PassesChcIntModeToTheBackend()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent(ProofMethod.Chc) });

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false, ChcIntMode: false),
            [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.False(Assert.Single(backend.Calls).ChcIntMode);
    }

    /// <summary>Ticket P1-002 criterion 4: <c>--invariant-model</c> is off unless given.</summary>
    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "--invariant-model", "claude-test" }, "claude-test")]
    public void Create_ParsesInvariantModelOffByDefault(string[] flag, string? expected)
    {
        ParseResult parseResult = CompareCommand.Create([], new FakeBackend(NoVerdicts)).Parse(["--legacy", "a.sln", "--modern", "b.sln", .. flag]);

        Assert.Empty(parseResult.Errors);
        Assert.Equal(expected, parseResult.GetValue<string?>("--invariant-model"));
    }

    /// <summary>
    /// Ticket P1-002 criterion 4: with <c>--invariant-model</c> the model reaches the backend and stderr says, once and
    /// before any pair is verified, that loop IR text goes to it; without it, neither.
    /// </summary>
    [Theory]
    [InlineData("claude-test")]
    [InlineData(null)]
    public void Cli_PrintsNoteWhenEnabled(string? model)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent(ProofMethod.LlmInvariant) });
        int exitCode = 0;

        string stderr = CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false, InvariantModel: model),
            [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(model, Assert.Single(backend.Calls).InvariantModel);
        Assert.Equal(model is null ? "" : "note: sending loop IR text to claude-test" + Environment.NewLine, stderr);
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
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));

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
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));

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
                [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));

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
                [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));

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
                [frontend], backend, new InMemoryReportSink(), NullRunLog.Instance));

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
            [new FakeFrontend("csharp", _ => true, new MatchResult([pair], [], [], []))], backend, sink, NullRunLog.Instance);

        Assert.Equal(ExitCodes.UnknownPresent, exitCode);
        Assert.Empty(backend.Calls);
        Result result = Assert.Single(sink.Log!.Runs[0].Results);
        Assert.Equal("EQ003", result.RuleId);
        Assert.Equal("unbound", result.GetProperty<string>("unknownReason"));

        // Ticket P2-085: the errors are the result's causes, and it points at the first one; the message holds no position.
        Assert.Equal("T::Pair() is unknown (Unbound): the modern body does not bind", result.Message.Text);
        Assert.Equal([(3, 5, "unbound"), (4, 1, "unbound")], result.RelatedLocations.Select(Position));
        Assert.Equal((3, 5), (result.Locations[0].PhysicalLocation.Region.StartLine, result.Locations[0].PhysicalLocation.Region.StartColumn));
    }

    /// <summary>Ticket P2-085: when neither body binds, both sides' errors are causes, legacy first, and the result points at the modern side's first.</summary>
    [Fact]
    public void AnUnboundPairsCausesAreTheErrorsOfBothSides()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedurePair pair = Pair(PairIdentity) with { OldBody = UnboundBody(PairIdentity, "old.cs"), NewBody = UnboundBody(PairIdentity) };
        FakeBackend backend = new(NoVerdicts);
        InMemoryReportSink sink = new();

        CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
            [new FakeFrontend("csharp", _ => true, new MatchResult([pair], [], [], []))], backend, sink, NullRunLog.Instance);

        Assert.Empty(backend.Calls);
        Result result = Assert.Single(sink.Log!.Runs[0].Results);
        Assert.Equal("T::Pair() is unknown (Unbound): the legacy body does not bind; the modern body does not bind", result.Message.Text);
        Assert.Equal(["old.cs", "old.cs", "a.cs", "a.cs"], result.RelatedLocations.Select(static l => l.PhysicalLocation.ArtifactLocation.Uri.OriginalString), StringComparer.Ordinal);
        Assert.Equal("a.cs", result.Locations[0].PhysicalLocation.ArtifactLocation.Uri.OriginalString);
    }

    /// <summary>
    /// Ticket P2-085: the fingerprint hashes the detail, so the detail holds no path or position, and an unbound result is
    /// <c>unchanged</c> against a baseline taken from another checkout or before the error moved.
    /// </summary>
    [Fact]
    public void AnUnboundResultsFingerprintDoesNotDependOnWhereTheErrorIs()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        InMemoryReportSink here = new();
        InMemoryReportSink elsewhere = new();

        foreach ((InMemoryReportSink sink, string path) in new[] { (here, @"C:\here\a.cs"), (elsewhere, "/home/elsewhere/a.cs") })
        {
            CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
                [new FakeFrontend("csharp", _ => true, new MatchResult([Pair(PairIdentity) with { NewBody = UnboundBody(PairIdentity, path) }], [], [], []))],
                new FakeBackend(NoVerdicts),
                sink,
                NullRunLog.Instance);
        }

        Assert.Equal(
            Assert.Single(here.Log!.Runs[0].Results).PartialFingerprints["resultFingerprint/v1"],
            Assert.Single(elsewhere.Log!.Runs[0].Results).PartialFingerprints["resultFingerprint/v1"]);
    }

    private static (int Line, int Column, string Message) Position(Location location) =>
        (location.PhysicalLocation.Region.StartLine, location.PhysicalLocation.Region.StartColumn, location.Message.Text);

    /// <summary>
    /// Ticket M4-006 acceptance criterion 2: a pair the frontend marked <c>async-mismatch</c> is Unknown with that detail, both
    /// marks as related locations, without calling the backend, even when the bound fingerprints agree.
    /// </summary>
    [Fact]
    public void AsyncMismatchIsUnknown()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        BodyFingerprint fingerprint = new("same", RuntimeSensitive: false);
        ProcedurePair pair = Pair(PairIdentity) with
        {
            OldBody = MismatchBody(PairIdentity, new SourceSpan("old.cs", 7, 20, 7, 21)),
            NewBody = MismatchBody(PairIdentity, new SourceSpan("new.cs", 9, 32, 9, 33)),
            OldFingerprint = fingerprint,
            NewFingerprint = fingerprint,
        };
        FakeBackend backend = new(NoVerdicts);
        InMemoryReportSink sink = new();

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "unknown", DryRun: false),
            [new FakeFrontend("csharp", _ => true, new MatchResult([pair], [], [], []))], backend, sink, NullRunLog.Instance);

        Assert.Equal(ExitCodes.UnknownPresent, exitCode);
        Assert.Empty(backend.Calls);
        Result result = Assert.Single(sink.Log!.Runs[0].Results);
        Assert.Equal("EQ003", result.RuleId);
        Assert.Equal("opaque", result.GetProperty<string>("unknownReason"));
        Assert.Contains("async-mismatch", result.Message.Text, StringComparison.Ordinal);
        Assert.Equal(["old.cs", "new.cs"], result.RelatedLocations.Select(static l => l.PhysicalLocation.ArtifactLocation.Uri.OriginalString), StringComparer.Ordinal);
    }

    /// <summary>Ticket M3-009 acceptance criterion 4: a pair's applied catalogue entries reach its SARIF result, whatever the verdict.</summary>
    [Fact]
    public void ThePairsEquivalencesAppliedReachTheResult()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity unbound = new("N.C::U()");
        ProcedurePair verified = Pair(PairIdentity) with { EquivalencesApplied = ["webapi.not-found", "webapi.ok-of-int"] };
        ProcedurePair unboundPair = Pair(unbound) with { NewBody = UnboundBody(unbound), EquivalencesApplied = ["webapi.ok"] };
        FakeBackend backend = new(ImmutableDictionary<string, Verdict>.Empty.Add(PairIdentity.Value, new Equivalent(ProofMethod.Bounded)));
        InMemoryReportSink sink = new();

        CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "unknown", DryRun: false),
            [new FakeFrontend("csharp", _ => true, new MatchResult([verified, unboundPair], [], [], []))], backend, sink, NullRunLog.Instance);

        Result[] results = [.. sink.Log!.Runs[0].Results];
        Assert.Equal(["webapi.not-found", "webapi.ok-of-int"], results.Single(static r => string.Equals(r.RuleId, "EQ001", StringComparison.Ordinal)).GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
        Assert.Equal(["webapi.ok"], results.Single(static r => string.Equals(r.RuleId, "EQ003", StringComparison.Ordinal)).GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
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
            [new FakeFrontend("csharp", _ => true, new MatchResult([pair], [], [], []))], new FakeBackend(NoVerdicts), sink, NullRunLog.Instance);

        Result result = Assert.Single(sink.Log!.Runs[0].Results);
        Assert.Equal("T::Pair() is unknown (Unbound): the legacy body does not bind", result.Message.Text);
        Assert.Equal([(3, 5, "unbound"), (4, 1, "unbound")], result.RelatedLocations.Select(Position));
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
            [new FakeFrontend("csharp", _ => true, matchResult)], backend, sink, NullRunLog.Instance));

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
            [new FakeFrontend("csharp", _ => true, matchResult)], backend, sink, NullRunLog.Instance));

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
            [new FakeFrontend("csharp", _ => true, matchResult)], backend, new InMemoryReportSink(), NullRunLog.Instance));

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
            [new FakeFrontend("csharp", _ => true, matchResult)], backend, new InMemoryReportSink(), NullRunLog.Instance)));

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
                [new FakeFrontend("csharp", _ => true, matchResult)], new FakeBackend(NoVerdicts), sink, NullRunLog.Instance));

            Result carried = Assert.Single(sink.Log!.Runs[0].Results);
            Assert.Equal(BaselineState.Unchanged, carried.BaselineState);
            Assert.True(carried.GetProperty<bool>("unverified"));
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    /// <summary>Ticket M3-015 acceptance criteria 5 and 6 (ADR 0024).</summary>
    [Fact]
    public void CongruentPairSkipsTheBackend()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        BodyFingerprint fingerprint = new("ab", RuntimeSensitive: false);
        ProcedurePair pair = Pair(PairIdentity) with { OldFingerprint = fingerprint, NewFingerprint = fingerprint, EquivalencesApplied = ["webapi.ok"] };
        FakeBackend backend = new(NoVerdicts);
        InMemoryReportSink sink = new();

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: "unknown", DryRun: false),
            [new FakeFrontend("csharp", _ => true, new MatchResult([pair], [], [], []))], backend, sink, NullRunLog.Instance);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(backend.Calls);
        Run run = sink.Log!.Runs[0];
        Result result = Assert.Single(run.Results);
        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
        Assert.Equal(["webapi.ok"], result.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
        Assert.True(run.TryGetSerializedPropertyValue("loweringCensus", out string? census));
        Assert.Contains("\"pairsCongruent\":1", census, StringComparison.Ordinal);
        Assert.Contains("\"changedPairs\":0", census, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P1-016 criterion 1: <c>--il-fallback</c> is off unless given, the frontend hears it through the config, and a run
    /// without it writes no <c>lowering</c> and no fallback counts.
    /// </summary>
    [Fact]
    public void IlFallbackIsOffByDefault()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        Command command = CompareCommand.Create([], new FakeBackend(NoVerdicts));
        FakeFrontend off = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        FakeFrontend on = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        InMemoryReportSink sink = new();
        CompareOptions options = new(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false);
        FakeBackend backend = new(ImmutableDictionary<string, Verdict>.Empty.Add(PairIdentity.Value, new Equivalent(ProofMethod.Bounded)));

        CompareCommand.Run(options, [off], backend, sink, NullRunLog.Instance);
        CompareCommand.Run(options with { IlFallback = true }, [on], backend, new InMemoryReportSink(), NullRunLog.Instance);

        Assert.False(command.Parse(["--legacy", "a.sln", "--modern", "b.sln"]).GetValue<bool>("--il-fallback"));
        Assert.True(command.Parse(["--legacy", "a.sln", "--modern", "b.sln", "--il-fallback"]).GetValue<bool>("--il-fallback"));
        Assert.False(off.LastConfig!.IlFallback);
        Assert.True(on.LastConfig!.IlFallback);
        Run run = sink.Log!.Runs[0];
        Assert.False(Assert.Single(run.Results).TryGetProperty("lowering", out string? _));
        Assert.True(run.TryGetSerializedPropertyValue("loweringCensus", out string? census));
        Assert.DoesNotContain("IlFallback", census, StringComparison.Ordinal);
        Assert.DoesNotContain("LoweredFromIl", census, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P1-016 criterion 4: under <c>--il-fallback</c> every result on a matched pair carries the lowering its pair kept,
    /// whether the solver or congruence decided it, and the census counts the pairs tried and the pairs lowered from IL.
    /// </summary>
    [Fact]
    public void IlFallbackMarksEachResultsLowering()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        BodyFingerprint fingerprint = new("ab", RuntimeSensitive: false);
        ProcedureIdentity kept = new("T::Kept()");
        ProcedureIdentity congruent = new("T::Congruent()");
        ProcedurePair fromIl = Pair(PairIdentity) with { Lowering = "il", IlFallbackTried = true };
        ProcedurePair keptOperation = Pair(kept) with { Lowering = "operation", IlFallbackTried = true };
        ProcedurePair notTried = Pair(congruent) with { Lowering = "operation", OldFingerprint = fingerprint, NewFingerprint = fingerprint };
        FakeBackend backend = new(ImmutableDictionary<string, Verdict>.Empty
            .Add(PairIdentity.Value, new Equivalent(ProofMethod.Bounded))
            .Add(kept.Value, new Unknown(UnknownReason.Opaque, "new: Conversion")));
        InMemoryReportSink sink = new();

        CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false) { IlFallback = true },
            [new FakeFrontend("csharp", _ => true, new MatchResult([fromIl, keptOperation, notTried], [new ProcedureIdentity("T::Added()")], [], []))],
            backend,
            sink,
            NullRunLog.Instance);

        Run run = sink.Log!.Runs[0];
        Dictionary<string, Result> results = run.Results.ToDictionary(static r => r.PartialFingerprints["procedureIdentity/v1"], StringComparer.Ordinal);
        Assert.Equal("il", results[PairIdentity.Value].GetProperty<string>("lowering"));
        Assert.Equal("operation", results[kept.Value].GetProperty<string>("lowering"));
        Assert.Equal("operation", results[congruent.Value].GetProperty<string>("lowering"));
        Assert.False(results["T::Added()"].TryGetProperty("lowering", out string? _));
        Assert.True(run.TryGetSerializedPropertyValue("loweringCensus", out string? census));
        Assert.Contains("\"pairsIlFallbackTried\":2,\"pairsLoweredFromIl\":1", census, StringComparison.Ordinal);
    }

    /// <summary>Ticket M3-015 acceptance criterion 5: every other pair goes to the backend as before.</summary>
    [Theory]
    [InlineData("ab", false, "cd", false)]
    [InlineData("ab", true, "ab", true)]
    [InlineData("ab", false, "ab", true)]
    [InlineData(null, false, "ab", false)]
    [InlineData("ab", false, null, false)]
    public void APairWhoseFingerprintsDifferOrAreRuntimeSensitiveGoesToTheBackend(string? oldHash, bool oldSensitive, string? newHash, bool newSensitive)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedurePair pair = Pair(PairIdentity) with
        {
            OldFingerprint = oldHash is null ? null : new BodyFingerprint(oldHash, oldSensitive),
            NewFingerprint = newHash is null ? null : new BodyFingerprint(newHash, newSensitive),
        };
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded) });
        InMemoryReportSink sink = new();

        CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
            [new FakeFrontend("csharp", _ => true, new MatchResult([pair], [], [], []))], backend, sink, NullRunLog.Instance);

        Assert.Single(backend.Calls);
        Assert.Equal("bounded", Assert.Single(sink.Log!.Runs[0].Results).GetProperty<string>("proofMethod"));
    }

    /// <summary>Ticket M3-015 acceptance criterion 5 (ADR 0029 decision 2): equal fingerprints do not make unbound code congruent.</summary>
    [Fact]
    public void AnUnboundMethodIsNeverCongruent()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        BodyFingerprint fingerprint = new("ab", RuntimeSensitive: false);
        ProcedureIdentity other = new("T::Other()");
        ProcedurePair unboundLegacy = Pair(PairIdentity) with { OldBody = UnboundBody(PairIdentity), OldFingerprint = fingerprint, NewFingerprint = fingerprint };
        ProcedurePair unboundModern = Pair(other) with { NewBody = UnboundBody(other), OldFingerprint = fingerprint, NewFingerprint = fingerprint };
        FakeBackend backend = new(NoVerdicts);
        InMemoryReportSink sink = new();

        CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
            [new FakeFrontend("csharp", _ => true, new MatchResult([unboundLegacy, unboundModern], [], [], []))], backend, sink, NullRunLog.Instance);

        Assert.Empty(backend.Calls);
        Assert.All(sink.Log!.Runs[0].Results, static r => Assert.Equal("unbound", r.GetProperty<string>("unknownReason")));
        Assert.True(sink.Log.Runs[0].TryGetSerializedPropertyValue("loweringCensus", out string? census));
        Assert.Contains("\"pairsCongruent\":0", census, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket M3-025 criterion 6 (ADR 0029 decision 4): a run with verdicts counts its Unknowns by scope, an unbound one
    /// under <c>method</c>; <c>--lower-only</c> has no verdicts, so no such count.
    /// </summary>
    [Fact]
    public void CensusCountsUnknownsByScope()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity line = new("T::Line()");
        ProcedureIdentity method = new("T::Method()");
        ProcedureIdentity unbound = new("T::Unbound()");
        MatchResult match = new([Pair(PairIdentity), Pair(line), Pair(method), Pair(unbound) with { NewBody = UnboundBody(unbound) }], [], [], []);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded),
            [line.Value] = new Unknown(UnknownReason.Opaque, "new: Await") { Scope = UnknownScope.Line },
            [method.Value] = new Unknown(UnknownReason.Timeout, "gave up"),
        });

        string Census(bool lowerOnly)
        {
            InMemoryReportSink sink = new();
            _ = CaptureStdOut(() => CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false, LowerOnly: lowerOnly),
                [new FakeFrontend("csharp", _ => true, match)], backend, sink, NullRunLog.Instance));
            Assert.True(sink.Log!.Runs[0].TryGetSerializedPropertyValue("loweringCensus", out string? census));
            return census!;
        }

        Assert.Contains("\"unknownByScope\":{\"line\":1,\"method\":2}", Census(lowerOnly: false), StringComparison.Ordinal);
        Assert.DoesNotContain("unknownByScope", Census(lowerOnly: true), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P1-013 criterion 3 (ADR 0037): the census reports how many Unknown pairs ran the failure-refinement queries
    /// and their total time; a run where none did has no such entry.
    /// </summary>
    [Fact]
    public void CensusReportsFailureRefinementTime()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity first = new("T::First()");
        ProcedureIdentity second = new("T::Second()");
        FailureRefinement refinement = new(RefinementResult.NoneProved, RefinementResult.Unknown);

        string Census(Verdict verdict)
        {
            FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
            {
                [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded),
                [first.Value] = verdict,
                [second.Value] = new Unknown(UnknownReason.Opaque, "new: Await") { FailureRefinement = refinement with { Elapsed = TimeSpan.FromMilliseconds(250) } },
            });
            InMemoryReportSink sink = new();
            _ = CaptureStdOut(() => CompareCommand.Run(
                new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
                [new FakeFrontend("csharp", _ => true, new MatchResult([Pair(PairIdentity), Pair(first), Pair(second)], [], [], []))], backend, sink, NullRunLog.Instance));
            Assert.True(sink.Log!.Runs[0].TryGetSerializedPropertyValue("loweringCensus", out string? census));
            return census!;
        }

        Assert.EndsWith(
            "\"failureRefinement\":{\"pairs\":2,\"milliseconds\":350}}",
            Census(new Unknown(UnknownReason.Abstraction, "opaque:f") { FailureRefinement = refinement with { Elapsed = TimeSpan.FromMilliseconds(100) } }),
            StringComparison.Ordinal);
        Assert.Contains("\"failureRefinement\":{\"pairs\":1,", Census(new Unknown(UnknownReason.Timeout, "gave up")), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P1-013 (ADR 0037): through the real backend, an unbound pair (decided without the solver) and a pair the solver
    /// times out on carry no <c>failureRefinement</c>; an opaque Unknown does.
    /// </summary>
    [Fact]
    public void UnboundAndTimeoutPairs_AreNotQueried()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity unbound = new("T::Unbound()");
        ProcedureIdentity hard = new("T::Rebuild(ulong,ulong)");
        ProcedureIdentity opaque = new("T::Opaque(int)");
        IrProcedure rebuild = IrText.Parse("""
            proc "T::Rebuild(ulong,ulong)" (%a: bv64, %b: bv64) -> bv64 entry B0
            B0:
              %q: bv64 = udiv %a, %b
              %m: bv64 = mul %q, %b
              %r: bv64 = urem %a, %b
              %s: bv64 = add %m, %r
              ret %s
            """);
        MatchResult match = new(
            [
                Pair(unbound) with { NewBody = UnboundBody(unbound) },
                new ProcedurePair(hard, hard, rebuild, IrText.Parse("""proc "T::Rebuild(ulong,ulong)" (%a: bv64, %b: bv64) -> bv64 entry B0 B0: ret %a""")),
                new ProcedurePair(
                    opaque,
                    opaque,
                    IrText.Parse("""proc "T::Opaque(int)" (%a: bv32) -> bv32 entry B0 B0: ret %a"""),
                    IrText.Parse("""proc "T::Opaque(int)" (%a: bv32) -> bv32 entry B0 B0: %s: sort "string" = opaque "InterpolatedString" at "New.cs" 5:9-5:30 ret %a""")),
            ],
            [],
            [],
            []);
        InMemoryReportSink sink = new();

        _ = CaptureStdOut(() => CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false) { TimeoutMs = 50 },
            [new FakeFrontend("csharp", _ => true, match)],
            new Z3Backend(),
            sink,
            NullRunLog.Instance));

        Dictionary<string, Result> results = sink.Log!.Runs[0].Results.ToDictionary(static r => r.PartialFingerprints["procedureIdentity/v1"], StringComparer.Ordinal);
        Assert.Equal("unbound", results[unbound.Value].GetProperty<string>("unknownReason"));
        Assert.Equal("timeout", results[hard.Value].GetProperty<string>("unknownReason"));
        Assert.False(results[unbound.Value].TryGetProperty("failureRefinement", out Dictionary<string, object>? _));
        Assert.False(results[hard.Value].TryGetProperty("failureRefinement", out Dictionary<string, object>? _));
        Assert.True(results[opaque.Value].TryGetProperty("failureRefinement", out Dictionary<string, object>? _));
    }

    /// <summary>Ticket M3-015 acceptance criteria 9 and 10 (ADR 0019).</summary>
    [Fact]
    public void Assumptions_ListMatchedCalleesOnly()
    {
        Dictionary<string, Result> results = AssumptionRun(
            [
                Caller("T::Total()", "T::Tax()", "System.Math::Abs(int)", "T::Broken()", "T::Tax()"),
                Caller("T::Tax()"),
            ],
            new Dictionary<string, Verdict>(StringComparer.Ordinal) { ["T::Total()"] = new Equivalent(ProofMethod.Bounded), ["T::Tax()"] = new Equivalent(ProofMethod.Bounded) },
            failures: [new LoweringFailure(new ProcedureIdentity("T::Broken()"), new ProcedureIdentity("T::Broken()"), new InvalidOperationException("boom"))]);

        Assert.Equal(["T::Broken()", "T::Tax()"], results["T::Total()"].GetProperty<List<string>>("assumedCallees"), StringComparer.Ordinal);
        Assert.Equal(["T::Broken()"], results["T::Total()"].GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
        Assert.False(results["T::Tax()"].TryGetProperty("assumedCallees", out List<string> _));
    }

    /// <summary>Ticket M3-015 acceptance criterion 9: a self-call is not an assumption (ADR 0019 clarification).</summary>
    [Fact]
    public void Assumptions_ExcludeSelfRecursion()
    {
        Dictionary<string, Result> results = AssumptionRun(
            [Caller("T::Fact()", "T::Fact()")],
            new Dictionary<string, Verdict>(StringComparer.Ordinal) { ["T::Fact()"] = new Unknown(UnknownReason.Recursion, "self-call") });

        Assert.False(results["T::Fact()"].TryGetProperty("assumedCallees", out List<string> _));
    }

    /// <summary>Ticket M3-015 acceptance criterion 10.</summary>
    [Fact]
    public void UnprovenAssumptions_AreCalleesNotEquivalent()
    {
        Dictionary<string, Result> results = AssumptionRun(
            [Caller("T::Total()", "T::Proved()", "T::Open()", "T::Changed()"), Caller("T::Proved()"), Caller("T::Open()"), Caller("T::Changed()")],
            new Dictionary<string, Verdict>(StringComparer.Ordinal)
            {
                ["T::Total()"] = new Equivalent(ProofMethod.Bounded),
                ["T::Proved()"] = new Equivalent(ProofMethod.LockstepInduction),
                ["T::Open()"] = new Unknown(UnknownReason.Timeout, "5000ms"),
                ["T::Changed()"] = new Divergent(Counterexample()),
            });

        Assert.Equal(["T::Changed()", "T::Open()", "T::Proved()"], results["T::Total()"].GetProperty<List<string>>("assumedCallees"), StringComparer.Ordinal);
        Assert.Equal(["T::Changed()", "T::Open()"], results["T::Total()"].GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
        Assert.Equal("T::Total() is equivalent. Assumes callees equivalent; not proved for: T::Changed(), T::Open().", results["T::Total()"].Message.Text);
    }

    /// <summary>
    /// Ticket M3-015: a congruent result assumes its callees like any other (ADR 0024 decision 1), and the assumption changes
    /// neither its verdict nor the exit code, which the callee's own Divergent sets (ADR 0019).
    /// </summary>
    [Fact]
    public void CongruentResult_ListsAssumedCallees()
    {
        BodyFingerprint fingerprint = new("ab", RuntimeSensitive: false);
        int exitCode = ExitCodes.UsageError;
        Dictionary<string, Result> results = AssumptionRun(
            [Caller("T::Total()", "T::Tax()") with { OldFingerprint = fingerprint, NewFingerprint = fingerprint }, Caller("T::Tax()")],
            new Dictionary<string, Verdict>(StringComparer.Ordinal) { ["T::Tax()"] = new Divergent(Counterexample()) },
            onExit: code => exitCode = code);

        Result total = results["T::Total()"];
        Assert.Equal("congruence", total.GetProperty<string>("proofMethod"));
        Assert.Equal(["T::Tax()"], total.GetProperty<List<string>>("assumedCallees"), StringComparer.Ordinal);
        Assert.Equal(["T::Tax()"], total.GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
        Assert.Equal("EQ002", results["T::Tax()"].RuleId);
        Assert.Equal(ExitCodes.Divergent, exitCode);
    }

    /// <summary>
    /// Ticket P1-010 criterion 4 (ADR 0036 decision 2): C calls f, f calls g, and both f and g are Divergent. The backend
    /// proves C again with a contract for f, so f leaves C's unproven assumptions and g, which f's contract assumed, joins
    /// them; C's proof method ends in <c>+contract</c> and names the contract.
    /// </summary>
    [Fact]
    public void ContractCallee_UnprovenAssumptionsAreInherited()
    {
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            ["T::C()"] = new Equivalent(ProofMethod.Congruence),
            ["T::F()"] = new Divergent(Counterexample()),
            ["T::G()"] = new Divergent(Counterexample()),
        })
        {
            Contracts = new Dictionary<string, Equivalent>(StringComparer.Ordinal)
            {
                ["T::C()"] = new Equivalent(ProofMethod.Bounded) { ContractsUsed = [new ContractUse("T::F()", "(= threw.old threw.new)", "observed-predicates")] },
            },
        };

        (Dictionary<string, Result> results, _) = ContractRun([Caller("T::C()", "T::F()"), Caller("T::F()", "T::G()"), Caller("T::G()")], backend);

        Result c = results["T::C()"];
        Assert.Equal("bounded+contract", c.GetProperty<string>("proofMethod"));
        Assert.Equal(["T::F()", "T::G()"], c.GetProperty<List<string>>("assumedCallees"), StringComparer.Ordinal);
        Assert.Equal(["T::G()"], c.GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
        Assert.Equal("T::F()", Assert.Single(c.GetProperty<List<Dictionary<string, string>>>("contractsUsed"))["callee"]);
        Assert.Equal([("T::C()", "T::F()")], backend.ContractCalls.Select(static call => (call.Caller, string.Join(',', call.Callees))));
        Assert.Equal("EQ002", results["T::F()"].RuleId);
    }

    /// <summary>
    /// Ticket P1-010: only an Equivalent result with a lowered unproven callee goes back to the backend, and one the backend
    /// finds no contract for keeps its verdict and assumptions.
    /// </summary>
    [Fact]
    public void AResultWithoutAContractKeepsItsAssumptions()
    {
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            ["T::C()"] = new Equivalent(ProofMethod.Bounded),
            ["T::D()"] = new Divergent(Counterexample()),
            ["T::F()"] = new Divergent(Counterexample()),
        });

        (Dictionary<string, Result> results, _) = ContractRun([Caller("T::C()", "T::F()"), Caller("T::D()", "T::F()"), Caller("T::F()")], backend);

        Assert.Equal("bounded", results["T::C()"].GetProperty<string>("proofMethod"));
        Assert.Equal(["T::F()"], results["T::C()"].GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
        Assert.Equal(["T::C()"], backend.ContractCalls.Select(static call => call.Caller), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P1-010: a contract step that throws leaves the caller's sound verdict and says so on stderr; a contract for a
    /// callee whose own verification crashed inherits nothing, since that callee has no result.
    /// </summary>
    [Fact]
    public void AFailedContractStepKeepsTheVerdictAndACrashedCalleeLeavesNothingToInherit()
    {
        Dictionary<string, Verdict> verdicts = new(StringComparer.Ordinal) { ["T::C()"] = new Equivalent(ProofMethod.Bounded) };
        Dictionary<string, Exception> crashes = new(StringComparer.Ordinal) { ["T::F()"] = new InvalidOperationException("encoder bug") };
        FakeBackend failing = new(verdicts, crashes) { ContractFailure = new InvalidOperationException("contract bug") };
        FakeBackend proving = new(verdicts, crashes)
        {
            Contracts = new Dictionary<string, Equivalent>(StringComparer.Ordinal)
            {
                ["T::C()"] = new Equivalent(ProofMethod.Bounded) { ContractsUsed = [new ContractUse("T::F()", "true", "observed-predicates")] },
            },
        };

        (Dictionary<string, Result> kept, string stderr) = ContractRun([Caller("T::C()", "T::F()"), Caller("T::F()")], failing);
        (Dictionary<string, Result> proved, _) = ContractRun([Caller("T::C()", "T::F()"), Caller("T::F()")], proving);

        Assert.Equal("bounded", kept["T::C()"].GetProperty<string>("proofMethod"));
        Assert.Contains("warning: Verifying T::C() under callee contracts failed, so it keeps its verdict: contract bug", stderr, StringComparison.Ordinal);
        Assert.Equal("bounded+contract", proved["T::C()"].GetProperty<string>("proofMethod"));
        Assert.False(proved["T::C()"].TryGetProperty("unprovenAssumptions", out List<string> _));
    }

    private static (Dictionary<string, Result> Results, string StdErr) ContractRun(ImmutableArray<ProcedurePair> pairs, FakeBackend backend)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        InMemoryReportSink sink = new();
        string stderr = CaptureStdErr(() => CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
            [new FakeFrontend("csharp", _ => true, new MatchResult(pairs, [], [], []))], backend, sink, NullRunLog.Instance));
        return (sink.Log!.Runs[0].Results.ToDictionary(static r => r.PartialFingerprints["procedureIdentity/v1"], StringComparer.Ordinal), stderr);
    }

    private static Dictionary<string, Result> AssumptionRun(
        ImmutableArray<ProcedurePair> pairs, IReadOnlyDictionary<string, Verdict> verdicts, ImmutableArray<LoweringFailure> failures = default, Action<int>? onExit = null)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new MatchResult(pairs, [], [], []) with { LoweringFailures = failures.IsDefault ? [] : failures };
        InMemoryReportSink sink = new();
        int exitCode = ExitCodes.UsageError;
        CaptureStdErr(() => exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
            [new FakeFrontend("csharp", _ => true, matchResult)], new FakeBackend(verdicts), sink, NullRunLog.Instance));
        onExit?.Invoke(exitCode);
        return sink.Log!.Runs[0].Results.ToDictionary(static r => r.PartialFingerprints["procedureIdentity/v1"], StringComparer.Ordinal);
    }

    /// <summary>A pair whose bodies call each of <paramref name="callees"/> once, in order, and return nothing.</summary>
    private static ProcedurePair Caller(string identity, params string[] callees)
    {
        string calls = string.Concat(callees.Select(static (callee, i) => $"  %c{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}: bv32 = call \"{callee}\"()\n"));
        IrProcedure body = IrText.Parse($"proc \"{identity}\" () entry B0\nB0:\n{calls}  ret\n");
        return new ProcedurePair(new ProcedureIdentity(identity), new ProcedureIdentity(identity), body, body);
    }

    private static IrProcedure MismatchBody(ProcedureIdentity identity, SourceSpan span) => new(
        identity,
        [],
        ReturnType: null,
        [new IrBlock(new IrBlockId(0), [new IrOpaque(Target: null, Unknown.AsyncMismatchReason, span) { WholeBody = true }], new IrReturn(Value: null, []))],
        new IrBlockId(0));

    private static IrProcedure UnboundBody(ProcedureIdentity identity, string path = "a.cs") => new(
        identity,
        [],
        ReturnType: null,
        [
            new IrBlock(
                new IrBlockId(0),
                [
                    new IrOpaque(Target: null, "Invalid", new SourceSpan(path, 2, 7, 2, 8)),
                    new IrOpaque(Target: null, Unknown.UnboundOpaqueReason, new SourceSpan(path, 3, 5, 3, 9)),
                    new IrOpaque(Target: null, "Invalid", new SourceSpan(path, 3, 7, 3, 8)),
                    new IrOpaque(Target: null, Unknown.UnboundOpaqueReason, new SourceSpan(path, 4, 1, 4, 2)),
                ],
                new IrReturn(Value: null, [])),
        ],
        new IrBlockId(0));

    /// <summary>
    /// Ticket M3-003 acceptance criterion 7: <see cref="MatchResult.Ambiguous"/> was deferred by M1-003 to
    /// "M1-004 or M1-005", then again by both without either naming it; this is the ticket that finally assembles a
    /// real <c>MatchResult</c> end to end, so it is where an unmatched overload group stops being left out of the SARIF.
    /// </summary>
    [Fact]
    public void AnAmbiguousIdentityIsUnknownUnmatchedOverload()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity ambiguous = new("T::Overload()");
        MatchResult matchResult = new([], [], [], [ambiguous]);
        InMemoryReportSink sink = new();

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "unknown", DryRun: false),
            [new FakeFrontend("csharp", _ => true, matchResult)], new FakeBackend(NoVerdicts), sink, NullRunLog.Instance);

        Assert.Equal(ExitCodes.UnknownPresent, exitCode);
        Result result = Assert.Single(sink.Log!.Runs[0].Results);
        Assert.Equal("EQ003", result.RuleId);
        Assert.Equal("unmatched-overload", result.GetProperty<string>("unknownReason"));
        Assert.Contains(ambiguous.Value, result.Message.Text, StringComparison.Ordinal);
    }

    /// <summary>An ambiguous identity does not, by itself, change the exit code without <c>--fail-on unknown</c>.</summary>
    [Fact]
    public void AnAmbiguousIdentityExits0ByDefault()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new([], [], [], [new ProcedureIdentity("T::Overload()")]);

        int exitCode = CompareCommand.Run(
            new CompareOptions(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, "divergent", DryRun: false),
            [new FakeFrontend("csharp", _ => true, matchResult)], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance);

        Assert.Equal(ExitCodes.Success, exitCode);
    }

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
