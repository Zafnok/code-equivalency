using System.CommandLine;
using System.Globalization;
using System.Text.RegularExpressions;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// Ticket M4-012 criteria 1 to 3 against the fakes: <c>--verbosity</c> and <c>--log</c>, the phases
/// <see cref="CompareCommand.Run"/> reports (<c>verify</c>, <c>execute</c>, <c>write</c>) with each item's
/// weight and outcome, progress on stderr and the <c>--log</c> file only, and stdout and the SARIF file unchanged by the
/// verbosity. The run on <c>samples/business-layer</c> is <c>CompareProgressSampleTests</c> in <c>Equiv.Tests.Integration</c>.
/// </summary>
[Collection("Console")]
public sealed partial class CompareCommandProgressTests
{
    private static readonly ProcedureIdentity Congruent = new("T::Congruent()");
    private static readonly ProcedureIdentity Unbound = new("T::Unbound()");
    private static readonly ProcedureIdentity Mismatch = new("T::Mismatch()");
    private static readonly ProcedureIdentity Same = new("T::Same()");
    private static readonly ProcedureIdentity Differs = new("T::Differs()");
    private static readonly ProcedureIdentity Unsure = new("T::Unsure()");
    private static readonly ProcedureIdentity Crashes = new("T::Crashes()");
    private static readonly ProcedureIdentity Loops = new("T::Loops()");

    private static readonly Counterexample Candidate =
        new(new IrInputs([]), new IrRun(new IrReturned(new IrBitVecValue(32, 1)), [], []), new IrRun(new IrReturned(new IrBitVecValue(32, 2)), [], []));

    [Fact]
    public void Create_Defaults_Verbosity_To_Normal_And_Leaves_Log_Unset()
    {
        ParseResult parseResult = CompareCommand.Create([], Backend()).Parse(["--legacy", "a.sln", "--modern", "b.sln"]);

        Assert.Empty(parseResult.Errors);
        Assert.Equal("normal", parseResult.GetValue<string>("--verbosity"));
        Assert.Null(parseResult.GetValue<string?>("--log"));
    }

    [Theory]
    [InlineData("quiet")]
    [InlineData("normal")]
    [InlineData("debug")]
    public void Create_Accepts_Each_Verbosity(string verbosity)
    {
        ParseResult parseResult = CompareCommand.Create([], Backend()).Parse(["--legacy", "a.sln", "--modern", "b.sln", "--verbosity", verbosity, "--log", "p.log"]);

        Assert.Empty(parseResult.Errors);
        Assert.Equal("p.log", parseResult.GetValue<string?>("--log"));
    }

    [Theory]
    [InlineData("loud")]
    [InlineData("1")]
    [InlineData("Normal")]
    public void Any_Other_Verbosity_Is_A_Usage_Error(string verbosity)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        int exitCode = 0;

        string error = CaptureStdErr(() => exitCode = Program.Run(["compare", "--legacy", legacy.Path, "--modern", modern.Path, "--verbosity", verbosity], [Frontend()], Backend()));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains(verbosity, error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterion 2 on the fakes: phase lines on stderr at <c>normal</c> and none at <c>quiet</c>, the pre-existing
    /// <c>error:</c> line unchanged, and the same stdout and SARIF bytes.
    /// </summary>
    [Fact]
    public void Normal_Writes_Each_Phase_On_Stderr_Only()
    {
        (string quietOut, string quietError, byte[] quietSarif) = Compare("quiet");
        (string normalOut, string normalError, byte[] normalSarif) = Compare("normal");

        Assert.Equal(quietOut, normalOut);
        Assert.Equal(quietSarif, normalSarif);
        Assert.DoesNotContain("equiv: ", normalOut, StringComparison.Ordinal);
        Assert.DoesNotContain("equiv: ", quietError, StringComparison.Ordinal);
        Assert.Contains("error: Verifying T::Crashes() against T::Crashes() failed: crash", quietError, StringComparison.Ordinal);
        Assert.Equal(Lines(quietError), Lines(normalError).Where(static line => !line.StartsWith("equiv: ", StringComparison.Ordinal)), StringComparer.Ordinal);
        string[] lines = Progress(normalError);
        Assert.All(lines, static line => Assert.Matches(ProgressLine, line));
        foreach (string phase in (string[])["verify", "write"])
        {
            Assert.Single(lines, line => Regex.IsMatch(line, $@"^equiv: \+\d\d:\d\d:\d\d {phase} 0/\d+ \(0%\) eta=\?", RegexOptions.None, TimeSpan.FromSeconds(1)));
            Assert.Single(lines, line => Regex.IsMatch(line, $@"^equiv: \+\d\d:\d\d:\d\d {phase} done in \d\d:\d\d:\d\d\.\d{{3}}; ", RegexOptions.None, TimeSpan.FromSeconds(1)));
        }
    }

    /// <summary>Criterion 3: the <c>--log</c> file holds the lines stderr got.</summary>
    [Fact]
    public void Log_File_Holds_The_Stderr_Lines()
    {
        string logPath = Path.Combine(Path.GetTempPath(), $"equiv-M4-012-{Guid.NewGuid():N}.log");
        try
        {
            (_, string error, _) = Compare("debug", logPath);

            string[] lines = Progress(error);
            Assert.Contains(lines, static line => line.Contains(" item=T::Same() outcome=equivalent ", StringComparison.Ordinal));
            Assert.Equal(lines, File.ReadAllLines(logPath));
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    /// <summary>
    /// The events <see cref="CompareCommand.Run"/> sends: one <c>verify</c> item per lowered pair
    /// with its <see cref="PairWeight"/> and outcome, bounded by the pairs the solver decides, and one <c>write</c> item.
    /// The backend gets the same log.
    /// </summary>
    [Fact]
    public void Run_Reports_Verify_And_Write()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        RecordingLog log = new();
        FakeBackend backend = Backend();

        _ = CaptureStdErr(() => CaptureStdOut(() => CompareCommand.Run(
            Options(legacy.Path, modern.Path), [Frontend()], backend, new InMemoryReportSink(), log)));

        long size = PairWeight.Of(Body(Same), Body(Same), solver: true);
        long loop = PairWeight.Of(LoopBody(Loops), LoopBody(Loops), solver: true);
        Assert.Equal(
            [
                Invariant($"phase verify 8 {3 + (4 * size) + loop} (5, 60000, 5)"),
                "item T::Congruent() 1", "done congruent",
                "item T::Unbound() 1", "done unbound",
                "item T::Mismatch() 1", "done async-mismatch",
                Invariant($"item T::Same() {size}"), "done equivalent",
                Invariant($"item T::Differs() {size}"), "done divergent",
                Invariant($"item T::Unsure() {size}"), "done unknown",
                Invariant($"item T::Crashes() {size}"), "done failed",
                Invariant($"item T::Loops() {loop}"), "done equivalent",
                "phase-done",
                "phase write 1 1", "item equiv.sarif 1", "done written", "phase-done",
            ],
            log.Events);
        Assert.All(backend.Calls, options => Assert.Same(log, options.Log));
    }

    [Fact]
    public void A_Verify_Phase_Without_Solver_Pairs_Is_Bounded_By_Nothing()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        RecordingLog log = new();

        _ = CaptureStdOut(() => CompareCommand.Run(
            Options(legacy.Path, modern.Path), [new FakeFrontend("csharp", _ => true, new MatchResult([CongruentPair()], [], [], []))], Backend(), new InMemoryReportSink(), log));

        Assert.Contains("phase verify 1 1 (0, 60000, 1)", log.Events, StringComparer.Ordinal);
    }

    [Fact]
    public void A_Load_Failure_Reports_Nothing_Itself()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        RecordingLog log = new();
        FakeFrontend frontend = new("csharp", _ => true, throwOnAnalyze: new FrontendLoadException("error: cannot load"));

        int exitCode = 0;
        _ = CaptureStdErr(() => exitCode = CompareCommand.Run(Options(legacy.Path, modern.Path), [frontend], Backend(), new InMemoryReportSink(), log));

        Assert.Equal(ExitCodes.LoadFailure, exitCode);
        Assert.Empty(log.Events);
    }

    [Fact]
    public void Execute_Is_A_Phase_With_One_Item_Per_Result()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        RecordingLog log = new();
        FakeReplay replay = new("[\"Returned\",1]", "[\"Returned\",1]");
        MatchResult match = new([Pair(Differs), Pair(Unsure), Pair(Same)], [], [], []);

        _ = CaptureStdErr(() => CaptureStdOut(() => CompareCommand.Run(
            Options(legacy.Path, modern.Path) with { Execute = true },
            [new FakeFrontend("csharp", _ => true, match, replay: replay)],
            Backend(),
            new InMemoryReportSink(),
            log,
            new ExecutionEnvironment(IsWindows: true, replay))));

        int start = log.Events.IndexOf("phase execute 3 3");
        Assert.Equal(
            ["item T::Differs() 1", "done replayed", "item T::Unsure() 1", "done tested", "item T::Same() 1", "done skipped", "phase-done"],
            log.Events.GetRange(start + 1, 7),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Run_Rejects_A_Null_Log()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(Options("a.sln", "b.sln"), [], Backend(), new InMemoryReportSink(), null!));
    }

    [GeneratedRegex(@"^equiv: \+\d\d:\d\d:\d\d [a-z]+ (\d+/\d+ \(\d+%\)|done in |detail: )", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProgressLine { get; }

    private static (string Out, string Error, byte[] Sarif) Compare(string verbosity, string? logPath = null)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M4-012-{Guid.NewGuid():N}.sarif");
        try
        {
            string[] log = logPath is null ? [] : ["--log", logPath];
            string error = string.Empty;
            string output = CaptureStdOut(() => error = CaptureStdErr(() => Assert.Equal(
                ExitCodes.InternalError,
                Program.Run(["compare", "--legacy", legacy.Path, "--modern", modern.Path, "--out", outPath, "--verbosity", verbosity, .. log], [Frontend()], Backend()))));
            return (output.Replace(legacy.Path, "legacy", StringComparison.Ordinal).Replace(modern.Path, "modern", StringComparison.Ordinal), error, File.ReadAllBytes(outPath));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    private static CompareOptions Options(string legacy, string modern) =>
        new(legacy, modern, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false);

    /// <summary>One pair for each way the verify phase decides it: three without the solver, five with it.</summary>
    private static FakeFrontend Frontend() => new(
        "csharp",
        _ => true,
        new MatchResult(
            [
                CongruentPair(),
                Pair(Unbound) with { NewBody = UnboundBody(Unbound) },
                Pair(Mismatch) with { OldBody = MismatchBody(Mismatch), NewBody = MismatchBody(Mismatch) },
                Pair(Same),
                Pair(Differs),
                Pair(Unsure),
                Pair(Crashes),
                new ProcedurePair(Loops, Loops, LoopBody(Loops), LoopBody(Loops)),
            ],
            [],
            [],
            []));

    private static FakeBackend Backend() => new(
        new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [Same.Value] = new Equivalent(ProofMethod.Bounded),
            [Differs.Value] = new Divergent(Candidate),
            [Unsure.Value] = new Unknown(UnknownReason.Abstraction, "opaque") { Candidate = Candidate },
            [Loops.Value] = new Equivalent(ProofMethod.Bounded),
        },
        new Dictionary<string, Exception>(StringComparer.Ordinal) { [Crashes.Value] = new InvalidOperationException("crash") });

    private static ProcedurePair CongruentPair()
    {
        BodyFingerprint fingerprint = new("same", RuntimeSensitive: false);
        return Pair(Congruent) with { OldFingerprint = fingerprint, NewFingerprint = fingerprint };
    }

    private static ProcedurePair Pair(ProcedureIdentity identity) => new(identity, identity, Body(identity), Body(identity));

    private static IrProcedure Body(ProcedureIdentity identity) => IrText.Parse($"proc \"{identity.Value}\" () entry B0 B0: ret");

    private static IrProcedure LoopBody(ProcedureIdentity identity) => IrText.Parse($"proc \"{identity.Value}\" () entry B0 B0: goto B1 B1: goto B0");

    private static IrProcedure UnboundBody(ProcedureIdentity identity) =>
        new(identity, [], ReturnType: null, [new IrBlock(new IrBlockId(0), [new IrOpaque(Target: null, Unknown.UnboundOpaqueReason, new SourceSpan("a.cs", 3, 5, 3, 9))], new IrReturn(Value: null, []))], new IrBlockId(0));

    private static IrProcedure MismatchBody(ProcedureIdentity identity) =>
        new(identity, [], ReturnType: null, [new IrBlock(new IrBlockId(0), [new IrOpaque(Target: null, Unknown.AsyncMismatchReason, new SourceSpan("a.cs", 7, 20, 7, 21)) { WholeBody = true }], new IrReturn(Value: null, []))], new IrBlockId(0));

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string[] Lines(string text) => text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private static string[] Progress(string text) => [.. Lines(text).Where(static line => line.StartsWith("equiv: ", StringComparison.Ordinal))];

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

    /// <summary>Every call, as one short string, in order.</summary>
    private sealed class RecordingLog : IRunLog
    {
        public List<string> Events { get; } = [];

        public bool IsDebug => false;

        public void Phase(string name, int total, long totalWeight, PhaseBound? bound = null) =>
            Events.Add(bound is null ? Invariant($"phase {name} {total} {totalWeight}") : Invariant($"phase {name} {total} {totalWeight} ({bound.SolverItems}, {bound.TimeoutMs}, {bound.Rungs})"));

        public void Item(string identity, long weight) => Events.Add(Invariant($"item {identity} {weight}"));

        public void ItemDone(string outcome) => Events.Add($"done {outcome}");

        public void Detail(string text) => Events.Add($"detail {text}");

        public void PhaseDone() => Events.Add("phase-done");
    }
}
