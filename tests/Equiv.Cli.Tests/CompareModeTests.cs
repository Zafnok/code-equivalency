using System.CommandLine;
using System.Globalization;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Cli.Tests;

/// <summary>
/// <c>equiv compare --mode thorough|quick</c> against fakes (ADR 0049; ticket P1-032): which passes run in each mode and
/// with which options, which of two results stands, what a crash in a later pass leaves, and what the run records. The
/// backend here answers by pair and by pass, and a pair it has no answer for in a pass is a crash of that pass.
/// </summary>
[Collection("Console")]
public sealed class CompareModeTests
{
    private const string First = "first";
    private const string Budget = "budget";
    private const string Il = "il";

    /// <summary>The mode a run here is in unless its test names another: most of these tests are about thorough's passes.</summary>
    private const string ThoroughMode = "thorough";

    /// <summary>The callee only a body lowered from IL calls, which is how the backend here tells the IL pass.</summary>
    private const string FromIl = "T::FromIl()";

    private const string DefaultMode = """{"name":"thorough","bound":3,"resourceLimit":2000000,"timeoutMs":60000,"escalation":{"bound":3,"resourceLimit":30000000,"timeoutMs":600000},"explicit":[]}""";

    private static readonly Verdict Proved = new Equivalent(ProofMethod.Bounded);

    private static readonly Unknown Opaque = new(UnknownReason.Opaque, "old: InterpolatedString");

    private static readonly FailureRefinement Refinement = new(RefinementResult.NoneProved, RefinementResult.Unknown);

    /// <summary>
    /// Criterion 1 as ADR 0052 amends it: with no <c>--mode</c> and no config the run is quick, the frontend hears it,
    /// the run records it, and the backend is asked once, at the first pass's values.
    /// </summary>
    [Fact]
    public void Mode_DefaultsToQuick()
    {
        FakeFrontend frontend = new("csharp", _ => true, Match(Plain("T::A()")));
        Command command = CompareCommand.Create([frontend], new FakeBackend(Verdicts(("T::A()", Proved))));

        PassBackend backend = new(Script(("T::A()", First, TimedOut())));

        Ran ran = Run(frontend, backend, mode: null);

        Assert.Null(command.Parse(["--legacy", "a.sln", "--modern", "b.sln"]).GetValue<string?>("--mode"));
        Assert.Equal(ExitCodes.Success, ran.ExitCode);
        Assert.Equal(CompareMode.Quick, frontend.LastConfig!.Mode);
        Assert.Equal(CompareMode.Quick, EquivConfig.Default.Mode);
        Assert.Equal("""{"name":"quick","bound":3,"resourceLimit":2000000,"timeoutMs":60000,"explicit":[]}""", ran.Mode);
        Assert.Equal(First, Assert.Single(backend.Calls).Pass);
        Assert.Empty(ran.StdErr);
    }

    /// <summary>Criterion 1: <c>--mode</c> wins over the config's <c>mode</c>, in both directions, and the config's stands without it.</summary>
    [Theory]
    [InlineData("quick", null, CompareMode.Quick)]
    [InlineData("quick", "thorough", CompareMode.Thorough)]
    [InlineData("thorough", "quick", CompareMode.Quick)]
    [InlineData("thorough", null, CompareMode.Thorough)]
    public void Mode_CommandLineWinsOverConfig(string configured, string? given, CompareMode expected)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        using TempFile config = new();
        using TempFile outFile = new();
        File.WriteAllText(config.Path, $$"""{ "mode": "{{configured}}" }""");
        FakeFrontend frontend = new("csharp", _ => true, Match(Plain("T::A()")));
        Command command = CompareCommand.Create([frontend], new FakeBackend(Verdicts(("T::A()", Proved))));
        string[] arguments = ["--legacy", legacy.Path, "--modern", modern.Path, "--out", outFile.Path, "--config", config.Path, .. given is null ? (string[])[] : ["--mode", given]];
        int exitCode = ExitCodes.UsageError;

        Capture(() => exitCode = command.Parse(arguments).Invoke());

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(expected, frontend.LastConfig!.Mode);
        Assert.Equal(given ?? configured, (string)SarifLog.Load(outFile.Path).Runs[0].GetProperty<JObject>("mode")["name"]!);
    }

    /// <summary>
    /// Criterion 1: a mode that is neither <c>thorough</c> nor <c>quick</c> is exit 3 wherever it is given: on the command
    /// line (the parser's error), in the options a caller builds (the MCP tool), and in the config.
    /// </summary>
    [Fact]
    public void Mode_UnknownValue_IsUsageError()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, Match(Plain("T::A()")));
        PassBackend backend = new(Script());
        int parsed = ExitCodes.Success;

        string parserError = Capture(() => parsed = Program.Run(["compare", "--legacy", legacy.Path, "--modern", modern.Path, "--mode", "fast"], [frontend], backend));
        Ran given = Run(frontend, backend, mode: "fast");
        Ran configured = Run(frontend, backend, config: """{ "mode": "fast" }""");

        Assert.Equal(ExitCodes.UsageError, parsed);
        Assert.Contains("'fast'", parserError, StringComparison.Ordinal);
        Assert.Equal((ExitCodes.UsageError, $"error: mode must be thorough or quick, not 'fast'{Environment.NewLine}"), (given.ExitCode, given.StdErr));
        Assert.Equal(ExitCodes.UsageError, configured.ExitCode);
        Assert.EndsWith($"error: equiv.config.json: \"mode\" must be \"thorough\" or \"quick\"{Environment.NewLine}", configured.StdErr, StringComparison.Ordinal);
        Assert.Null(given.Log);
        Assert.Null(configured.Log);
        Assert.Empty(backend.Calls);
        Assert.Equal(0, frontend.AnalyzeCallCount);
    }

    /// <summary>
    /// ADR 0049's table, the budget pass and IL lowering rows, quick: a pair that timed out, a pair with a loop and a pair
    /// the frontend also lowered from IL are each verified once, at the first pass's values, with rung 5's local proposer
    /// and failure refinement on a timeout both off; no result names a later pass and the run log has no later phase.
    /// </summary>
    [Fact]
    public void Quick_RunsNoLaterPass()
    {
        PassBackend backend = new(Script(("T::A()", First, TimedOut()), ("T::C()", First, Unaligned()), ("T::G()", First, Opaque)));

        Ran ran = Run(Match(Plain("T::A()"), Looping("T::C()"), WithIl("T::G()")), backend, mode: "quick");

        Assert.Equal(ExitCodes.Success, ran.ExitCode);
        Assert.Equal([("T::A()", First), ("T::C()", First), ("T::G()", First)], backend.Calls.Select(static c => (c.Identity, c.Pass)));
        Assert.All(backend.Calls, static c => Assert.Equal((3, 2_000_000, 60_000, false, false), (c.Options.Bound, c.Options.ResourceLimit, c.Options.TimeoutMs, c.Options.LocalProposer, c.Options.RefineTimeouts)));
        Assert.All(ran.Results.Values, static r => Assert.False(r.TryGetProperty("decidedBy", out string? _)));
        Assert.Equal(["verify", "write"], ran.Phases, StringComparer.Ordinal);
        Assert.Equal("""{"name":"quick","bound":3,"resourceLimit":2000000,"timeoutMs":60000,"explicit":[]}""", ran.Mode);
        Assert.Empty(ran.StdErr);
    }

    /// <summary>
    /// ADR 0049's table, the contracts pass row: quick leaves an Equivalent its unproven assumptions and never asks the
    /// backend for a contract; thorough asks, at the first pass's budgets with every query on, and the callee leaves the list.
    /// </summary>
    [Fact]
    public void Quick_SkipsContractsPass()
    {
        MatchResult match = Match(CompareCommandTests.Caller("T::C()", "T::F()"), CompareCommandTests.Caller("T::F()"));
        static FakeBackend Backend() => new(Verdicts(("T::C()", Proved), ("T::F()", Opaque)))
        {
            Contracts = new Dictionary<string, Equivalent>(StringComparer.Ordinal)
            {
                ["T::C()"] = new Equivalent(ProofMethod.Bounded) { ContractsUsed = [new ContractUse("T::F()", "true", "observed-predicates")] },
            },
        };
        FakeBackend quickBackend = Backend();
        FakeBackend thoroughBackend = Backend();

        Ran quick = Run(match, quickBackend, mode: "quick");
        Ran thorough = Run(match, thoroughBackend, mode: "thorough");

        Assert.Empty(quickBackend.ContractCalls);
        Assert.Equal(["T::F()"], quick.Results["T::C()"].GetProperty<List<string>>("unprovenAssumptions"));
        Assert.Equal("bounded", quick.Results["T::C()"].GetProperty<string>("proofMethod"));
        Assert.DoesNotContain("contracts", quick.Phases, StringComparer.Ordinal);
        Assert.Equal("T::C()", Assert.Single(thoroughBackend.ContractCalls).Caller);
        Assert.False(thorough.Results["T::C()"].TryGetProperty("unprovenAssumptions", out List<string> _));
        Assert.Equal("bounded+contract", thorough.Results["T::C()"].GetProperty<string>("proofMethod"));
        Assert.Contains("contracts", thorough.Phases, StringComparer.Ordinal);
        Assert.Equal(quick.Results["T::C()"].PartialFingerprints, thorough.Results["T::C()"].PartialFingerprints);
    }

    /// <summary>
    /// ADR 0049's table, the two <c>--execute</c> rows (criterion 7): given <c>--execute</c>, both modes replay every
    /// Divergent, and only thorough tests an Unknown on generated inputs.
    /// </summary>
    [Theory]
    [InlineData("quick", false)]
    [InlineData("thorough", true)]
    public void Quick_Execute_TestsNoUnknown(string mode, bool testsUnknowns)
    {
        FakeReplay replay = new("[\"Returned\",1]", "[\"Returned\",1]");
        MatchResult match = Match(Plain("T::D()"), Plain("T::U()"), Plain("T::E()"));
        PassBackend backend = new(Script(("T::D()", First, new Divergent(CompareCommandTests.Counterexample())), ("T::U()", First, Opaque), ("T::E()", First, Proved)));

        Ran ran = Run(new FakeFrontend("csharp", _ => true, match, replay: replay), backend, mode, replay: replay);

        Assert.Equal(ExitCodes.Divergent, ran.ExitCode);
        Assert.Equal("T::D()", Assert.Single(replay.Creates).Pair.New.Value);
        Assert.True(ran.Results["T::D()"].TryGetProperty("replay", out string? _));
        Assert.Equal((string[])(testsUnknowns ? ["T::U()"] : []), replay.Plans.Select(static p => p.Pair.New.Value), StringComparer.Ordinal);
        Assert.Equal(testsUnknowns, ran.Results["T::U()"].TryGetProperty("differentialTesting", out Dictionary<string, object> _));
        Assert.Equal("EQ003", ran.Results["T::U()"].RuleId);
        Assert.DoesNotContain("were not tested", ran.StdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR 0049's table, the budget pass row, thorough: of the results the first pass leaves Unknown, the ones verified
    /// again, at bound 3, resource limit 30,000,000 and timeout 600,000 (ADR 0049 as ticket P2-134 clarified it), are those whose ladder holds a step that hit a
    /// budget and those whose pair has a loop or a self-call. An Unknown without either, an Unknown decided without the
    /// solver and a decided result are not. Rung 5's local proposer and failure refinement on a timeout run in that pass
    /// and not in the first.
    /// </summary>
    [Fact]
    public void Thorough_BudgetPass_OnlyOnUnknownsThatHitABudgetOrLoop()
    {
        ProcedureIdentity unbound = new("T::F()");
        PassBackend backend = new(Script(
            ("T::A()", First, TimedOut()), ("T::A()", Budget, Proved with { Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, "every input checked")] }),
            ("T::B()", First, Opaque),
            ("T::C()", First, Unaligned()), ("T::C()", Budget, Unaligned()),
            ("T::D()", First, Proved),
            ("T::E()", First, new Unknown(UnknownReason.Recursion, "calls itself")), ("T::E()", Budget, new Divergent(CompareCommandTests.Counterexample()))));

        Ran ran = Run(
            Match(Plain("T::A()"), Plain("T::B()"), Looping("T::C()"), Plain("T::D()"), CompareCommandTests.Caller("T::E()", "T::E()"), new ProcedurePair(unbound, unbound, CompareCommandTests.UnboundBody(unbound), CompareCommandTests.UnboundBody(unbound))),
            backend);

        Assert.Equal(ExitCodes.Divergent, ran.ExitCode);
        Assert.Equal(
            [("T::A()", First), ("T::B()", First), ("T::C()", First), ("T::D()", First), ("T::E()", First), ("T::A()", Budget), ("T::C()", Budget), ("T::E()", Budget)],
            backend.Calls.Select(static c => (c.Identity, c.Pass)));
        Assert.All(backend.Calls.Where(static c => string.Equals(c.Pass, First, StringComparison.Ordinal)), static c => Assert.Equal((3, 2_000_000, 60_000, false, false), (c.Options.Bound, c.Options.ResourceLimit, c.Options.TimeoutMs, c.Options.LocalProposer, c.Options.RefineTimeouts)));
        Assert.All(backend.Calls.Where(static c => string.Equals(c.Pass, Budget, StringComparison.Ordinal)), static c => Assert.Equal((3, 30_000_000, 600_000, true, true), (c.Options.Bound, c.Options.ResourceLimit, c.Options.TimeoutMs, c.Options.LocalProposer, c.Options.RefineTimeouts)));
        Assert.Equal(("EQ001", "budget-pass"), (ran.Results["T::A()"].RuleId, ran.Results["T::A()"].GetProperty<string>("decidedBy")));
        Assert.Equal(["timeout", "proved"], ran.Results["T::A()"].GetProperty<List<Dictionary<string, string>>>("ladderTrace").Select(static step => step["outcome"]), StringComparer.Ordinal);
        Assert.Equal(("EQ002", "budget-pass"), (ran.Results["T::E()"].RuleId, ran.Results["T::E()"].GetProperty<string>("decidedBy")));
        Assert.All((string[])["T::B()", "T::C()", "T::D()", "T::F()"], identity => Assert.False(ran.Results[identity].TryGetProperty("decidedBy", out string? _)));
        Assert.Single(ran.Results["T::C()"].GetProperty<List<Dictionary<string, string>>>("ladderTrace"));
        Assert.Equal(["verify", "budget", "write"], ran.Phases, StringComparer.Ordinal);
        Assert.Contains("phase budget 3 86 (3, 600000, 5)", ran.Events, StringComparer.Ordinal);
        Assert.Equal(["item T::A() 2", "done equivalent", "item T::C() 80", "done unknown", "item T::E() 4", "done divergent"], ran.Events.SkipWhile(static e => !e.StartsWith("phase budget", StringComparison.Ordinal)).Skip(1).TakeWhile(static e => !string.Equals(e, "phase-done", StringComparison.Ordinal)), StringComparer.Ordinal);
        Assert.Equal(DefaultMode, ran.Mode);
    }

    /// <summary>
    /// ADR 0049's table, the IL lowering row, thorough: a result still Unknown whose pair the frontend also lowered from IL
    /// is verified again from its IL bodies, at the budget pass's budgets. One that is decided, one without IL bodies and
    /// one decided without the solver are not. A result the pass produced names the IL lowering, and its assumptions and
    /// its replay read the IL bodies; a result the pass leaves keeps the lowering it had.
    /// </summary>
    [Fact]
    public void Thorough_IlPass_OnlyOnUnknowns()
    {
        ProcedureIdentity unbound = new("T::I()");
        ProcedurePair decidedWithoutTheSolver = WithIl("T::I()") with { OldBody = CompareCommandTests.UnboundBody(unbound), NewBody = CompareCommandTests.UnboundBody(unbound) };
        FakeReplay replay = new("[\"Returned\",1]", "[\"Returned\",1]");
        PassBackend backend = new(Script(
            ("T::G()", First, Opaque), ("T::G()", Il, new Divergent(CompareCommandTests.Counterexample())),
            ("T::H()", First, Proved),
            ("T::B()", First, Opaque),
            ("T::J()", First, Opaque), ("T::J()", Il, Opaque with { Detail = "new: Lambda" }),
            (FromIl, First, Proved)));
        MatchResult match = Match(WithIl("T::G()") with { EquivalencesApplied = ["bcl.string-contains-char"] }, WithIl("T::H()"), Plain("T::B()"), decidedWithoutTheSolver, WithIl("T::J()"), Plain(FromIl));

        Ran ran = Run(new FakeFrontend("csharp", _ => true, match, replay: replay), backend, replay: replay);

        Assert.Equal(ExitCodes.Divergent, ran.ExitCode);
        Assert.Equal(
            [("T::G()", First), ("T::H()", First), ("T::B()", First), ("T::J()", First), (FromIl, First), ("T::G()", Il), ("T::J()", Il)],
            backend.Calls.Select(static c => (c.Identity, c.Pass)));
        Assert.All(backend.Calls.Where(static c => string.Equals(c.Pass, Il, StringComparison.Ordinal)), static c => Assert.Equal((3, 30_000_000, 600_000, true, true), (c.Options.Bound, c.Options.ResourceLimit, c.Options.TimeoutMs, c.Options.LocalProposer, c.Options.RefineTimeouts)));
        Result decided = ran.Results["T::G()"];
        Assert.Equal(("EQ002", "il-pass", "il"), (decided.RuleId, decided.GetProperty<string>("decidedBy"), decided.GetProperty<string>("lowering")));
        Assert.Equal([FromIl], decided.GetProperty<List<string>>("assumedCallees"));
        Assert.False(decided.TryGetProperty("equivalencesApplied", out List<string> _));
        Assert.Equal("T::Forwarder()", Assert.Single(decided.GetProperty<List<Dictionary<string, string>>>("forwardersResolved"))["forwarder"]);
        Assert.Contains(FromIl, IrText.Dump(Assert.Single(replay.Creates).Pair.OldBody!), StringComparison.Ordinal);
        Result kept = ran.Results["T::J()"];
        Assert.Contains("old: InterpolatedString", kept.Message.Text, StringComparison.Ordinal);
        Assert.False(kept.TryGetProperty("decidedBy", out string? _));
        Assert.False(kept.TryGetProperty("lowering", out string? _));
        Assert.False(kept.TryGetProperty("assumedCallees", out List<string> _));
        Assert.All((string[])["T::H()", "T::B()", "T::I()"], identity => Assert.False(ran.Results[identity].TryGetProperty("lowering", out string? _)));
        Assert.Equal(["verify", "il", "execute", "write"], ran.Phases, StringComparer.Ordinal);
        Assert.Contains("phase il 2 8 (2, 600000, 1)", ran.Events, StringComparer.Ordinal);
    }

    /// <summary>
    /// ADR 0049's table, the IL lowering row, with the values ticket P2-134's measurement chose
    /// (<c>docs/runs/2026-10-08-thorough-budgets.md</c>): the IL pass verifies at the budget pass's values, with every
    /// query on. Those are bound 3, resource limit 30,000,000 and timeout 600,000 unless the config's <c>escalation</c>
    /// names others, and the first pass's when the run has no budget pass.
    /// </summary>
    [Theory]
    [InlineData(null, 3, 30_000_000, 600_000)]
    [InlineData("""{ "escalation": { "bound": 6, "resourceLimit": 5000000 } }""", 6, 5_000_000, 600_000)]
    [InlineData("""{ "resourceLimit": 30000000, "timeoutMs": 600000 }""", 3, 30_000_000, 600_000)]
    [InlineData("""{ "bound": 4, "resourceLimit": 40000000, "timeoutMs": 700000 }""", 4, 40_000_000, 700_000)]
    public void Thorough_IlPass_UsesTheChosenBudgets(string? config, int bound, int resourceLimit, int timeoutMs)
    {
        PassBackend backend = new(Script(("T::G()", First, Opaque), ("T::G()", Il, Proved), (FromIl, First, Proved)));

        Ran ran = Run(Match(WithIl("T::G()"), Plain(FromIl)), backend, config: config);

        (string _, string _, VerificationOptions options) = Assert.Single(backend.Calls, static c => string.Equals(c.Pass, Il, StringComparison.Ordinal));
        Assert.Equal((bound, resourceLimit, timeoutMs, true, true), (options.Bound, options.ResourceLimit, options.TimeoutMs, options.LocalProposer, options.RefineTimeouts));
        Assert.Equal(("EQ001", "il-pass"), (ran.Results["T::G()"].RuleId, ran.Results["T::G()"].GetProperty<string>("decidedBy")));
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"phase il 1 4 (1, {timeoutMs}, 1)"), ran.Events, StringComparer.Ordinal);
    }

    /// <summary>
    /// ADR 0049's table, the failure refinement row (criterion 8): in thorough a pair that times out in both passes keeps
    /// its first result, as decision 2 says, and carries the answers the budget pass asked at its budgets. In quick it
    /// carries none. A later Unknown that is not a timeout replaces an earlier one that is, and one that is does not.
    /// </summary>
    [Fact]
    public void Thorough_TimeoutUnknown_CarriesFailureRefinement()
    {
        (string, string, Verdict)[] script =
        [
            ("T::K()", First, TimedOut()), ("T::K()", Budget, TimedOut("30000000") with { FailureRefinement = Refinement }),
            ("T::L()", First, TimedOut()), ("T::L()", Budget, Opaque with { FailureRefinement = Refinement }),
            ("T::M()", First, new Unknown(UnknownReason.ChcTimeout, "Spacer gave up") { FailureRefinement = Refinement }), ("T::M()", Budget, new Unknown(UnknownReason.NoInvariant, "no candidate was admitted") { FailureRefinement = Refinement }),
            ("T::N()", First, Unaligned() with { FailureRefinement = Refinement }), ("T::N()", Budget, TimedOut("30000000") with { FailureRefinement = Refinement }),
        ];
        MatchResult match = Match(Plain("T::K()"), Plain("T::L()"), Looping("T::M()"), Looping("T::N()"));

        Ran thorough = Run(match, new PassBackend(Script(script)));
        Ran quick = Run(match, new PassBackend(Script(script)), mode: "quick");

        Result timeout = thorough.Results["T::K()"];
        Assert.Contains("resource limit 2000000 hit", timeout.Message.Text, StringComparison.Ordinal);
        Assert.False(timeout.TryGetProperty("decidedBy", out string? _));
        Assert.Equal("none-proved", (string)timeout.GetProperty<JObject>("failureRefinement")["newFailures"]!["outcome"]!);
        Assert.Single(timeout.GetProperty<List<Dictionary<string, string>>>("ladderTrace"));
        Assert.Equal(("EQ003", "timeout"), (quick.Results["T::K()"].RuleId, quick.Results["T::K()"].GetProperty<string>("unknownReason")));
        Assert.False(quick.Results["T::K()"].TryGetProperty("failureRefinement", out JObject? _));
        Assert.Equal(quick.Results["T::K()"].PartialFingerprints, timeout.PartialFingerprints);
        Assert.Equal(("opaque", "budget-pass"), (thorough.Results["T::L()"].GetProperty<string>("unknownReason"), thorough.Results["T::L()"].GetProperty<string>("decidedBy")));
        Assert.Equal(("no-invariant", "budget-pass"), (thorough.Results["T::M()"].GetProperty<string>("unknownReason"), thorough.Results["T::M()"].GetProperty<string>("decidedBy")));
        Assert.Equal("unaligned-loop", thorough.Results["T::N()"].GetProperty<string>("unknownReason"));
        Assert.False(thorough.Results["T::N()"].TryGetProperty("decidedBy", out string? _));
    }

    /// <summary>
    /// Criterion 4 (ADR 0049 decision 2): over generated pairs of results, the one that stands is never Unknown when either
    /// is decided and never the later one when the earlier is decided; and it is the later one exactly when the earlier is
    /// Unknown and the later is decided, or is an Unknown that is neither timeout nor chc-timeout where the earlier was.
    /// </summary>
    [Fact]
    public void LaterPass_NeverReplacesADecidedResult()
    {
        Gen<Verdict> verdicts = Gen.OneOf(
            Gen.Const(Proved),
            Gen.Const<Verdict>(new Divergent(CompareCommandTests.Counterexample())),
            Gen.Enum<UnknownReason>().Select(static reason => (Verdict)new Unknown(reason, "detail")));
        ProcedureIdentity identity = new("T::A()");

        Gen.Select(verdicts, verdicts).Sample(
            pair =>
            {
                VerificationResult earlier = new(identity, pair.Item1);
                VerificationResult later = new(identity, pair.Item2) { DecidedBy = VerificationResult.BudgetPass };

                VerificationResult standing = CompareCommand.Standing(earlier, later);

                bool earlierDecided = earlier.Verdict is not Unknown;
                bool laterDecided = later.Verdict is not Unknown;
                bool replaces = !earlierDecided && (laterDecided || (GaveUp(earlier.Verdict) && !GaveUp(later.Verdict)));
                Assert.Same(replaces ? later : earlier, standing);
                Assert.True(standing.Verdict is not Unknown || !(earlierDecided || laterDecided));
                Assert.True(!earlierDecided || ReferenceEquals(standing, earlier));
            },
            iter: 2_000,
            print: static pair => $"{Describe(pair.Item1)} then {Describe(pair.Item2)}");

        static bool GaveUp(Verdict verdict) => verdict is Unknown { Reason: UnknownReason.Timeout or UnknownReason.ChcTimeout };
        static string Describe(Verdict verdict) => verdict is Unknown unknown ? $"Unknown({unknown.Reason})" : verdict.GetType().Name;
    }

    /// <summary>
    /// Criterion 9 (ADR 0049 decision 5): a later pass that throws on a pair leaves the pair its earlier result, writes one
    /// warning on stderr, and the run has no failed pair: its exit code and its results are quick's, where neither pass ran.
    /// </summary>
    [Fact]
    public void LaterPass_Crash_KeepsEarlierResult()
    {
        (string, string, Verdict)[] script = [("T::A()", First, TimedOut()), ("T::G()", First, Opaque), ("T::D()", First, Proved)];
        MatchResult match = Match(Plain("T::A()"), WithIl("T::G()"), Plain("T::D()"));

        Ran thorough = Run(match, new PassBackend(Script(script)), failOn: "unknown");
        Ran quick = Run(match, new PassBackend(Script(script)), mode: "quick", failOn: "unknown");

        Assert.Equal(ExitCodes.UnknownPresent, quick.ExitCode);
        Assert.Equal(quick.ExitCode, thorough.ExitCode);
        Assert.Equal(
            [
                "warning: Verifying T::A() again in the budget pass failed, so it keeps its result: budget pass crashed on T::A()",
                "warning: Verifying T::G() again in the il pass failed, so it keeps its result: il pass crashed on T::G()",
                "note: the Unknown results were not tested on generated inputs; --execute tests them, and runs the solutions' code to do it",
            ],
            thorough.StdErr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Null(thorough.Log!.Runs[0].Invocations);
        Assert.False(thorough.Log.Runs[0].TryGetProperty("unverified", out List<string> _));
        Assert.Equal(
            quick.Log!.Runs[0].Results.Select(static r => (r.RuleId, r.Message.Text, r.PartialFingerprints["resultFingerprint/v1"])),
            thorough.Log.Runs[0].Results.Select(static r => (r.RuleId, r.Message.Text, r.PartialFingerprints["resultFingerprint/v1"])));
        Assert.All(thorough.Results.Values, static r => Assert.False(r.TryGetProperty("decidedBy", out string? _)));
        Assert.Equal(["done failed"], thorough.Events.SkipWhile(static e => !e.StartsWith("phase budget", StringComparison.Ordinal)).Skip(2).Take(1), StringComparer.Ordinal);
    }

    /// <summary>
    /// Criterion 3 (ADR 0049 decision 4): <c>bound</c>, <c>resourceLimit</c> and <c>timeoutMs</c> from the config, and
    /// <c>--resource-limit</c> over the config's, are the first pass's values in either mode; the config's
    /// <c>escalation</c> is the budget pass's; and the run names what was given explicitly.
    /// </summary>
    [Theory]
    [InlineData("quick")]
    [InlineData("thorough")]
    public void ExplicitBudget_WinsOverMode(string mode)
    {
        const string Config = """{ "bound": 5, "resourceLimit": 9000, "timeoutMs": 7000, "escalation": { "bound": 6, "resourceLimit": 10000, "timeoutMs": 8000 } }""";
        PassBackend backend = new(Script(("T::A()", First, TimedOut()), ("T::A()", Budget, TimedOut())));

        Ran ran = Run(new FakeFrontend("csharp", _ => true, Match(Plain("T::A()"))), backend, mode, Config, with: static options => options with { ResourceLimit = 42 });

        Assert.Equal((5, 42, 7000), (backend.Calls[0].Options.Bound, backend.Calls[0].Options.ResourceLimit, backend.Calls[0].Options.TimeoutMs));
        if (string.Equals(mode, "quick", StringComparison.Ordinal))
        {
            Assert.Single(backend.Calls);
            Assert.Equal("""{"name":"quick","bound":5,"resourceLimit":42,"timeoutMs":7000,"explicit":["bound","resourceLimit","timeoutMs","escalation"]}""", ran.Mode);
        }
        else
        {
            Assert.Equal((6, 10_000, 8000), (backend.Calls[1].Options.Bound, backend.Calls[1].Options.ResourceLimit, backend.Calls[1].Options.TimeoutMs));
            Assert.Equal(
                """{"name":"thorough","bound":5,"resourceLimit":42,"timeoutMs":7000,"escalation":{"bound":6,"resourceLimit":10000,"timeoutMs":8000},"explicit":["bound","resourceLimit","timeoutMs","escalation"]}""",
                ran.Mode);
        }
    }

    /// <summary>
    /// ADR 0049 decision 4: the budget pass never asks with less than the first pass. A config whose
    /// <c>resourceLimit</c> is above the escalation's is escalated in timeout only; and when the first pass's
    /// values already meet the escalation's there is no budget pass, so the first pass asks rung 5's local proposer and
    /// refines its timeouts itself, and the IL pass runs at the first pass's values.
    /// </summary>
    [Fact]
    public void Escalation_NeverBelowFirstPass()
    {
        PassBackend above = new(Script(("T::A()", First, TimedOut()), ("T::A()", Budget, TimedOut())));
        PassBackend met = new(Script(("T::A()", First, TimedOut()), ("T::G()", First, Opaque), ("T::G()", Il, Opaque), (FromIl, First, Proved)));

        Ran raised = Run(Match(Plain("T::A()")), above, config: """{ "resourceLimit": 40000000 }""");
        Ran none = Run(Match(Plain("T::A()"), WithIl("T::G()"), Plain(FromIl)), met, config: """{ "bound": 9, "resourceLimit": 30000000, "timeoutMs": 600000 }""");

        Assert.Equal([(3, 40_000_000, 60_000), (3, 40_000_000, 600_000)], above.Calls.Select(static c => (c.Options.Bound, c.Options.ResourceLimit, c.Options.TimeoutMs)));
        Assert.Equal("""{"name":"thorough","bound":3,"resourceLimit":40000000,"timeoutMs":60000,"escalation":{"bound":3,"resourceLimit":40000000,"timeoutMs":600000},"explicit":["resourceLimit"]}""", raised.Mode);
        Assert.Equal([("T::A()", First), ("T::G()", First), (FromIl, First), ("T::G()", Il)], met.Calls.Select(static c => (c.Identity, c.Pass)));
        Assert.All(met.Calls, static c => Assert.Equal((9, 30_000_000, 600_000, true, true), (c.Options.Bound, c.Options.ResourceLimit, c.Options.TimeoutMs, c.Options.LocalProposer, c.Options.RefineTimeouts)));
        Assert.Equal("""{"name":"thorough","bound":9,"resourceLimit":30000000,"timeoutMs":600000,"explicit":["bound","resourceLimit","timeoutMs"]}""", none.Mode);
        Assert.Equal(["verify", "il", "write"], none.Phases, StringComparer.Ordinal);
    }

    /// <summary>
    /// Criterion 7 (ADR 0049 decision 3): no mode runs the solutions' code or names a model. Without <c>--execute</c> and
    /// <c>--invariant-model</c> nothing is replayed or tested and no pass is given a model, in either mode; thorough says
    /// once that its Unknowns were not tested, and says nothing when it has none.
    /// </summary>
    [Theory]
    [InlineData("quick", true, "")]
    [InlineData("thorough", true, "note: the Unknown results were not tested on generated inputs; --execute tests them, and runs the solutions' code to do it")]
    [InlineData("thorough", false, "")]
    public void Mode_NeverTurnsOnExecuteOrInvariantModel(string mode, bool anyUnknown, string note)
    {
        ArgumentNullException.ThrowIfNull(note);
        FakeReplay replay = new("[\"Returned\",1]", "[\"Returned\",2]");
        MatchResult match = Match(Plain("T::A()"), Plain("T::B()"), Plain("T::D()"));
        PassBackend backend = new(Script(
            ("T::A()", First, anyUnknown ? TimedOut() : Proved), ("T::A()", Budget, TimedOut()),
            ("T::B()", First, anyUnknown ? Opaque : Proved),
            ("T::D()", First, new Divergent(CompareCommandTests.Counterexample()))));

        Ran ran = Run(new FakeFrontend("csharp", _ => true, match, replay: replay), backend, mode, execution: new ExecutionEnvironment(IsWindows: true, replay));

        Assert.Equal(ExitCodes.Divergent, ran.ExitCode);
        Assert.Empty(replay.Creates);
        Assert.Empty(replay.Plans);
        Assert.Empty(replay.Starts);
        Assert.All(backend.Calls, static c => Assert.Null(c.Options.InvariantModel));
        Assert.DoesNotContain("execute", ran.Phases, StringComparer.Ordinal);
        Assert.Equal((string[])(note.Length == 0 ? [] : [note]), ran.StdErr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Criterion 6 (ADR 0049 decision 6): a run whose <c>--baseline</c> names the other mode warns once on stderr. One
    /// written in the same mode, one that names no mode, one whose mode has no name and one without a run do not.
    /// </summary>
    [Fact]
    public void BaselineFromOtherMode_Warns()
    {
        MatchResult match = Match(Plain("T::A()"));
        static PassBackend Backend() => new(Script(("T::A()", First, Proved)));
        string quick = CompareCommandTests.Serialize(Run(match, Backend(), mode: "quick").Log!);
        JObject unnamed = JObject.Parse(quick);
        ((JObject)unnamed["runs"]![0]!["properties"]!["mode"]!).Remove("name");
        JObject modeless = JObject.Parse(quick);
        ((JObject)modeless["runs"]![0]!["properties"]!).Remove("mode");
        JObject empty = JObject.Parse(quick);
        ((JArray)empty["runs"]!).Clear();

        Ran other = Run(match, Backend(), mode: "thorough", baseline: quick);
        Ran reverse = Run(match, Backend(), mode: "quick", baseline: CompareCommandTests.Serialize(other.Log!));
        Ran same = Run(match, Backend(), mode: "quick", baseline: quick);

        Assert.Equal($"warning: the baseline was written in quick mode and this run is in thorough mode; results the modes decide differently are reported as new{Environment.NewLine}", other.StdErr);
        Assert.Equal($"warning: the baseline was written in thorough mode and this run is in quick mode; results the modes decide differently are reported as new{Environment.NewLine}", reverse.StdErr);
        Assert.Equal(BaselineState.Unchanged, other.Results["T::A()"].BaselineState);
        Assert.Empty(same.StdErr);
        Assert.Empty(Run(match, Backend(), baseline: unnamed.ToString(Formatting.None)).StdErr);
        Assert.Empty(Run(match, Backend(), baseline: modeless.ToString(Formatting.None)).StdErr);
        Assert.Empty(Run(match, Backend(), baseline: empty.ToString(Formatting.None)).StdErr);
    }

    /// <summary>
    /// Criterion 6: a result both modes decide alike has the same rule id and fingerprints in both, the runs exit alike,
    /// and <c>--lower-only</c>, which verifies nothing, records no mode and has the frontend keep no second lowering.
    /// </summary>
    [Fact]
    public void ModesAgreeOnWhatBothDecide()
    {
        (string, string, Verdict)[] script = [("T::A()", First, Proved), ("T::D()", First, new Divergent(CompareCommandTests.Counterexample())), ("T::B()", First, Opaque)];
        MatchResult match = Match(Plain("T::A()"), Plain("T::D()"), Plain("T::B()"));
        FakeFrontend frontend = new("csharp", _ => true, match);

        Ran thorough = Run(match, new PassBackend(Script(script)));
        Ran quick = Run(match, new PassBackend(Script(script)), mode: "quick");
        Ran lowerOnly = Run(frontend, new PassBackend(Script()), mode: "thorough", with: static options => options with { LowerOnly = true });

        Assert.Equal((ExitCodes.Divergent, ExitCodes.Divergent), (thorough.ExitCode, quick.ExitCode));
        Assert.Equal(
            quick.Log!.Runs[0].Results.Select(static r => (r.RuleId, r.PartialFingerprints["resultFingerprint/v1"], r.PartialFingerprints["procedureIdentity/v1"])),
            thorough.Log!.Runs[0].Results.Select(static r => (r.RuleId, r.PartialFingerprints["resultFingerprint/v1"], r.PartialFingerprints["procedureIdentity/v1"])));
        Assert.Equal(ExitCodes.Success, lowerOnly.ExitCode);
        Assert.False(lowerOnly.Log!.Runs[0].TryGetProperty("mode", out JObject? _));
        Assert.Equal(CompareMode.Quick, frontend.LastConfig!.Mode);
    }

    /// <summary>Criterion 6: one thorough run, with a result each later pass produced, as the SARIF it writes.</summary>
    [Fact]
    public Task RunSnapshot_Thorough() => VerifyJson(CompareCommandTests.Serialize(SnapshotRun(mode: "thorough").Log!));

    /// <summary>Criterion 6: the same pairs in quick mode, where every result is the first pass's.</summary>
    [Fact]
    public Task RunSnapshot_Quick() => VerifyJson(CompareCommandTests.Serialize(SnapshotRun(mode: "quick").Log!));

    private static Ran SnapshotRun(string mode) => Run(
        Match(Plain("T::A()"), WithIl("T::G()"), Plain("T::D()"), Plain(FromIl)),
        new PassBackend(Script(
            ("T::A()", First, TimedOut()), ("T::A()", Budget, Proved with { Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, "no loop or self-call; every input checked")] }),
            ("T::G()", First, Opaque), ("T::G()", Il, Proved),
            ("T::D()", First, Proved),
            (FromIl, First, Proved))),
        mode);

    private static Unknown TimedOut(string limit = "2000000")
    {
        string detail = $"solver returned unknown (canceled): resource limit {limit} hit";
        return new Unknown(UnknownReason.Timeout, detail) { Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, detail)] };
    }

    private static Unknown Unaligned() =>
        new(UnknownReason.UnalignedLoop, "the loops do not align") { Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, "some input goes past the bound")] };

    private static MatchResult Match(params ProcedurePair[] pairs) => new([.. pairs], [], [], []);

    private static ProcedurePair Plain(string identity) => CompareCommandTests.Pair(new ProcedureIdentity(identity));

    private static ProcedurePair Looping(string identity)
    {
        IrProcedure body = IrText.Parse($"proc \"{identity}\" () entry B0 B0: goto B1 B1: goto B0");
        return new ProcedurePair(new ProcedureIdentity(identity), new ProcedureIdentity(identity), body, body);
    }

    /// <summary>A pair the frontend also lowered from IL: its IL bodies call <see cref="FromIl"/> and resolved one forwarder.</summary>
    private static ProcedurePair WithIl(string identity)
    {
        IrProcedure il = CompareCommandTests.Caller(identity, FromIl).OldBody!;
        return Plain(identity) with { Il = new IlBodies(il, il, [new ResolvedForwarder("T::Forwarder()", FromIl)]) };
    }

    private static Dictionary<string, Verdict> Verdicts(params (string Identity, Verdict Verdict)[] verdicts) =>
        verdicts.ToDictionary(static v => v.Identity, static v => v.Verdict, StringComparer.Ordinal);

    private static Dictionary<(string Identity, string Pass), Verdict> Script(params (string Identity, string Pass, Verdict Verdict)[] answers) =>
        answers.ToDictionary(static a => (a.Identity, a.Pass), static a => a.Verdict);

    private static Ran Run(MatchResult match, IVerificationBackend backend, string? mode = ThoroughMode, string? config = null, string? baseline = null, string? failOn = null) =>
        Run(new FakeFrontend("csharp", _ => true, match), backend, mode, config, baseline, failOn);

    /// <summary>
    /// One run through <see cref="CompareCommand.Run"/> with its own streams. <paramref name="replay"/> turns
    /// <c>--execute</c> on; <paramref name="execution"/> alone only offers an environment the run must not use.
    /// </summary>
    private static Ran Run(
        FakeFrontend frontend,
        IVerificationBackend backend,
        string? mode = ThoroughMode,
        string? config = null,
        string? baseline = null,
        string? failOn = null,
        FakeReplay? replay = null,
        ExecutionEnvironment? execution = null,
        Func<CompareOptions, CompareOptions>? with = null)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        using TempFile configFile = new();
        using TempFile baselineFile = new();
        File.WriteAllText(configFile.Path, config ?? "{}");
        File.WriteAllText(baselineFile.Path, baseline ?? string.Empty);
        using StringWriter stdout = new(CultureInfo.InvariantCulture);
        using StringWriter stderr = new(CultureInfo.InvariantCulture);
        InMemoryReportSink sink = new();
        EventLog log = new();
        CompareOptions options = new(legacy.Path, modern.Path, "equiv.sarif", baseline is null ? null : baselineFile.Path, config is null ? null : configFile.Path, failOn, DryRun: false, Execute: replay is not null)
        {
            Mode = mode,
            Streams = new Streams(stdout, stderr),
        };

        int exitCode = CompareCommand.Run(
            with?.Invoke(options) ?? options, [frontend], backend, sink, log, replay is null ? execution : new ExecutionEnvironment(IsWindows: true, replay));

        return new Ran(exitCode, stderr.ToString().Replace(ExecutionEnvironment.Note + Environment.NewLine, string.Empty, StringComparison.Ordinal), sink.Log, log.Events);
    }

    /// <summary>Runs <paramref name="action"/> with the console's two streams captured, and returns what it wrote to stderr.</summary>
    private static string Capture(Action action)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter stdout = new(CultureInfo.InvariantCulture);
        using StringWriter stderr = new(CultureInfo.InvariantCulture);
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        return stderr.ToString();
    }

    /// <summary>What one run left: its exit code, its stderr without <c>--execute</c>'s own note, its SARIF log and the run log's events.</summary>
    private sealed record Ran(int ExitCode, string StdErr, SarifLog? Log, List<string> Events)
    {
        public Dictionary<string, Result> Results => Log!.Runs[0].Results.ToDictionary(static r => r.PartialFingerprints["procedureIdentity/v1"], StringComparer.Ordinal);

        /// <summary><c>run.properties.mode</c>, as compact JSON.</summary>
        public string Mode => Log!.Runs[0].GetProperty<JObject>("mode").ToString(Formatting.None);

        /// <summary>The names of the run log's phases, in order.</summary>
        public IEnumerable<string> Phases => Events.Where(static e => e.StartsWith("phase ", StringComparison.Ordinal)).Select(static e => e.Split(' ')[1]);
    }

    /// <summary>
    /// A backend that answers by pair and by pass: <c>il</c> when the legacy body calls <see cref="FromIl"/>, else
    /// <c>first</c> the first time it sees the pair and <c>budget</c> after. A pair with no answer for the pass throws,
    /// which is how a test crashes a pass. It never proves a contract.
    /// </summary>
    private sealed class PassBackend(IReadOnlyDictionary<(string Identity, string Pass), Verdict> script) : IVerificationBackend
    {
        public List<(string Identity, string Pass, VerificationOptions Options)> Calls { get; } = [];

        public Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options)
        {
            string identity = newBody.Identity.Value;
            bool fromIl = !string.Equals(identity, FromIl, StringComparison.Ordinal) && IrText.Dump(oldBody).Contains(FromIl, StringComparison.Ordinal);
            string pass = (fromIl, Calls.Exists(c => string.Equals(c.Identity, identity, StringComparison.Ordinal))) switch
            {
                (true, _) => Il,
                (false, true) => Budget,
                _ => First,
            };
            Calls.Add((identity, pass, options));
            return script.TryGetValue((identity, pass), out Verdict? verdict) ? verdict : throw new InvalidOperationException($"{pass} pass crashed on {identity}");
        }

        public Equivalent? VerifyUnderContracts(IrProcedure oldBody, IrProcedure newBody, System.Collections.Immutable.ImmutableArray<CalleePair> callees, VerificationOptions options) => null;
    }

    /// <summary>Every call of the run log, as one short string, in order.</summary>
    private sealed class EventLog : IRunLog
    {
        public List<string> Events { get; } = [];

        public bool IsDebug => false;

        public void Phase(string name, int total, long totalWeight, PhaseBound? bound = null) =>
            Events.Add(string.Create(CultureInfo.InvariantCulture, $"phase {name} {total} {totalWeight}{(bound is null ? string.Empty : $" ({bound.SolverItems}, {bound.TimeoutMs}, {bound.Rungs})")}"));

        public void Item(string identity, long weight) => Events.Add(string.Create(CultureInfo.InvariantCulture, $"item {identity} {weight}"));

        public void ItemDone(string outcome) => Events.Add($"done {outcome}");

        public void Detail(string text) => Events.Add($"detail {text}");

        public void PhaseDone() => Events.Add("phase-done");
    }
}
