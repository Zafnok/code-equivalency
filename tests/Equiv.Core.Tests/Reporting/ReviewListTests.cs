using System.Collections.Immutable;
using System.Globalization;

using CsCheck;

using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Core.Tests.Reporting;

/// <summary>
/// Ticket P2-064: every flagged result (EQ002, EQ003, EQ006) gets a <c>reviewGroup</c> and a <c>rank</c>, the run lists the
/// groups in <c>reviewList</c>, and <see cref="ReviewList.Lines"/> reads the stdout lines back from the run.
/// </summary>
public sealed class ReviewListTests
{
    private const string HashCodeCall = "System.String::GetHashCode()";
    private const string IndexOfCall = "System.String::IndexOf(char)";
    private const string HashCodeGroup = "runtime-change:System.String::GetHashCode(";
    private const string IndexOfGroup = "runtime-change:System.String::IndexOf(";
    private const string SwapGroup = "calls:N::New()|N::Old()";

    private static readonly Gen<string[]> Callees = Gen.OneOfConst("N::A()", "N::B()", "N::C()").Array[0, 3];

    private static readonly Gen<Verdict> Verdicts = Gen.OneOf(
        Gen.Select(Callees, Callees).Select(static t => (Verdict)Diverges(t.Item1, t.Item2)),
        Gen.OneOfConst("Lambda", "Await", "Lock").Array[0, 3].Select(static reasons => (Verdict)Opaque(reasons)),
        Gen.OneOfConst<string?>("DelegateCreation", "Lambda", null).Array[1, 3].Select(static reasons => (Verdict)Abstracted(reasons)),
        Gen.OneOfConst<Verdict>(new Unknown(UnknownReason.Timeout, "slow"), new Equivalent(ProofMethod.Bounded), new Added()));

    [Fact]
    public void Eq006_groups_by_runtime_changed_member()
    {
        Run run = Write(
            Result("A", Diverges([Flagged(HashCodeCall)], [])),
            Result("B", Diverges([], [Flagged(IndexOfCall)])),
            Result("C", Diverges([Flagged(HashCodeCall)], [new CallIdentity("N::Other()")])));

        Assert.All(run.Results, static r => Assert.Equal("EQ006", r.RuleId));
        Assert.Equal([HashCodeGroup, IndexOfGroup, HashCodeGroup], run.Results.Select(Group), StringComparer.Ordinal);
        Assert.Equal(
            [new Entry(HashCodeGroup, "EQ006", 40.004, 2, ["A", "C"]), new Entry(IndexOfGroup, "EQ006", 40.002, 1, ["B"])],
            List(run));
    }

    [Fact]
    public void Eq002_groups_by_differing_callees()
    {
        Run run = Write(
            Result("A", Diverges(["N::Old()", "N::Shared()"], ["N::Shared()", "N::New()"])),
            Result("B", Diverges(["N::New()"], ["N::Old()", "N::Old()"])),
            Result("C", Diverges(["N::Shared()"], ["N::Shared()", "N::Shared()"])),
            Result("D", Observed()),
            Result("E", new Divergent(Fixtures.Counterexample())));

        Assert.All(run.Results, static r => Assert.Equal("EQ002", r.RuleId));
        Assert.Equal(
            [SwapGroup, SwapGroup, "proofMethod:none", "proofMethod:observed", "proofMethod:none"],
            run.Results.Select(Group),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Eq003_groups_by_reason_and_opaque_reasons()
    {
        Unknown withCauses = new(UnknownReason.UnalignedLoop, "no rung decided it") { Causes = [Cause(Codebase.Modern, "Lambda")] };
        ImmutableArray<Abstraction> fragments =
        [
            new Abstraction(Codebase.Modern, new CallIdentity("opaque:1f"), Span(3)) { Reason = "Lambda" },
            new Abstraction(Codebase.Legacy, new CallIdentity("opaque:2e"), Span: null) { Reason = "DelegateCreation" },
            new Abstraction(Codebase.Legacy, new CallIdentity("f32.mul"), Span: null),
            new Abstraction(Codebase.Modern, new CallIdentity("opaque:2e"), Span: null) { Reason = "DelegateCreation" },
        ];

        Run run = Write(
            Result("A", new Unknown(UnknownReason.Timeout, "slow")),
            Result("B", Opaque("Lambda", "Await", "Lambda")),
            Result("C", new Unknown(UnknownReason.Opaque, "async-mismatch")),
            Result("D", Unknown.DependingOn(Fixtures.Counterexample(), fragments)),
            Result("E", Abstracted([null])),
            Result("F", withCauses));

        Assert.All(run.Results, static r => Assert.Equal("EQ003", r.RuleId));
        Assert.Equal(
            ["timeout", "opaque:Await+Lambda", "opaque", "abstraction:DelegateCreation+Lambda", "abstraction", "unaligned-loop"],
            run.Results.Select(Group),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Observed_divergent_ranks_above_eq002_above_eq006_above_unknown()
    {
        ExecutionOutcome outcome = new(new ExecutionInput([]), "", OutcomeKind.Returned, "1");
        Run run = Write(
            Result("Method", new Unknown(UnknownReason.Timeout, "slow")),
            Result("Line", Opaque("Lambda") with { Scope = UnknownScope.Line }),
            Result("Runtime", Diverges([Flagged(HashCodeCall)], [])),
            Result("Solver", new Divergent(Fixtures.Counterexample())),
            Result("Observed", Observed()),
            Result("Reproduced", Diverges(["N::Old()"], ["N::New()"])) with { Replay = ReplayResult.Reproduced },
            Result("RuntimeReproduced", Diverges([Flagged(IndexOfCall)], [])) with { Replay = ReplayResult.Reproduced },
            Result("NotReproduced", Diverges(["N::A()"], ["N::B()"])) with { Replay = ReplayResult.NotReproduced(outcome, outcome) });

        Assert.Equal([0.002, 20.002, 40.002, 60.002, 80.002, 80.002, 80.002, 60.002], run.Results.Select(static r => r.Rank));
        Assert.Equal(
            [
                new Entry(SwapGroup, "EQ002", 80.002, 1, ["Reproduced"]),
                new Entry("proofMethod:observed", "EQ002", 80.002, 1, ["Observed"]),
                new Entry(IndexOfGroup, "EQ006", 80.002, 1, ["RuntimeReproduced"]),
                new Entry("calls:N::A()|N::B()", "EQ002", 60.002, 1, ["NotReproduced"]),
                new Entry("proofMethod:none", "EQ002", 60.002, 1, ["Solver"]),
                new Entry(HashCodeGroup, "EQ006", 40.002, 1, ["Runtime"]),
                new Entry("opaque:Lambda", "EQ003", 20.002, 1, ["Line"]),
                new Entry("timeout", "EQ003", 0.002, 1, ["Method"]),
            ],
            List(run));
    }

    [Fact]
    public void Larger_group_ranks_higher_within_a_tier()
    {
        Run run = Write(
            Result("U1", new Unknown(UnknownReason.Unbound, "legacy: unbound")),
            Result("T1", new Unknown(UnknownReason.Timeout, "slow")),
            Result("N1", new Unknown(UnknownReason.NoInvariant, "none found")),
            Result("T2", new Unknown(UnknownReason.Timeout, "slower")),
            Result("N2", new Unknown(UnknownReason.NoInvariant, "none found")),
            Result("T3", new Unknown(UnknownReason.Timeout, "slowest")));

        Assert.Equal([0.002, 0.006, 0.004, 0.006, 0.004, 0.006], run.Results.Select(static r => r.Rank));
        Assert.Equal(
            [
                new Entry("timeout", "EQ003", 0.006, 3, ["T1", "T2", "T3"]),
                new Entry("no-invariant", "EQ003", 0.004, 2, ["N1", "N2"]),
                new Entry("unbound", "EQ003", 0.002, 1, ["U1"]),
            ],
            List(run));
    }

    /// <summary>One rank per group, while <c>replay</c> and <c>scope</c> are per result: the group takes its best result's tier.</summary>
    [Fact]
    public void A_group_takes_the_best_tier_any_of_its_results_is_in()
    {
        Run run = Write(
            Result("Method", Opaque("Lambda")),
            Result("Line", Opaque("Lambda") with { Scope = UnknownScope.Line }),
            Result("Plain", Diverges(["N::Old()"], ["N::New()"])),
            Result("Reproduced", Diverges(["N::Old()"], ["N::New()"])) with { Replay = ReplayResult.Reproduced });

        Assert.Equal([20.004, 20.004, 80.004, 80.004], run.Results.Select(static r => r.Rank));
    }

    [Fact]
    public void Equivalent_and_added_removed_get_no_group()
    {
        Run run = Write(
            Result("A", new Equivalent(ProofMethod.Bounded)),
            Result("B", new Added()),
            Result("C", new Removed()));

        Assert.All(run.Results, static r =>
        {
            Assert.False(r.TryGetProperty("reviewGroup", out string? _));
            Assert.Equal(-1.0, r.Rank);
        });
        Assert.Empty(List(run));
        Assert.Equal(["review list: 0 groups for 0 flagged results"], ReviewList.Lines(run), StringComparer.Ordinal);
    }

    /// <summary>
    /// The key is a function of one result, read as sets: neither the order of the results nor the order of a trace's
    /// calls, an Unknown's causes or its abstractions changes any result's group or rank, or the list.
    /// </summary>
    [Fact]
    public void Key_is_stable_across_result_order()
    {
        Gen.Select(Verdicts, Gen.Int).Array[0, 12].Sample(static generated =>
        {
            VerificationResult[] results = [.. generated.Select(static (g, index) => Result(Numbered("T::M", index), g.Item1))];
            VerificationResult[] reordered =
            [
                .. generated
                    .Select(static (g, index) => (Key: g.Item2, Result: Result(Numbered("T::M", index), Reversed(g.Item1))))
                    .OrderBy(static r => r.Key)
                    .Select(static r => r.Result),
            ];

            Run first = Write(results);
            Run second = Write(reordered);

            Assert.Equal(ByIdentity(first), ByIdentity(second));
            Assert.Equal(List(first).Select(static e => e with { Identities = [] }), List(second).Select(static e => e with { Identities = [] }));
            Assert.Equal(results.Count(static r => r.Verdict is Divergent or Unknown), List(first).Sum(static e => e.Count));
        }, iter: 300);
    }

    [Fact]
    public void An_entry_lists_the_first_five_identities_in_result_order()
    {
        Run run = Write([.. Enumerable.Range(1, 7).Reverse().Select(static i => Result(Numbered("T", i), new Unknown(UnknownReason.Timeout, "slow")))]);

        Assert.Equal([new Entry("timeout", "EQ003", 0.014, 7, ["T7", "T6", "T5", "T4", "T3"])], List(run));
    }

    /// <summary>A tier is 20 wide, so a group's size counts up to 9,999 results and no further.</summary>
    [Fact]
    public void A_huge_group_stays_inside_its_tier()
    {
        Run run = Write([.. Enumerable.Range(1, 10_000).Select(static i => Result(Numbered("T", i), new Unknown(UnknownReason.Timeout, "slow")))]);

        Assert.All(run.Results, static r => Assert.Equal(19.998, r.Rank));
        Assert.Equal(10_000, Assert.Single(List(run)).Count);
    }

    /// <summary>A baseline's carry-over is a result of the log like any other: it keeps its group and is ranked and counted.</summary>
    [Fact]
    public void A_carry_over_keeps_its_group_and_is_counted()
    {
        VerificationResult a = Result("A", new Unknown(UnknownReason.Timeout, "slow"));
        SarifLog baseline = SarifReportWriter.Write([a, Result("B", new Unknown(UnknownReason.Timeout, "slow")), Result("C", new Equivalent(ProofMethod.Bounded))]);

        Run run = SarifReportWriter.Write([a], baseline, reviewList: true).Runs[0];

        Assert.Equal([BaselineState.Unchanged, BaselineState.Absent, BaselineState.Absent], run.Results.Select(static r => r.BaselineState));
        Assert.Equal([0.004, 0.004, -1.0], run.Results.Select(static r => r.Rank));
        Assert.False(run.Results[2].TryGetProperty("reviewGroup", out string? _));
        Assert.Equal([new Entry("timeout", "EQ003", 0.004, 2, ["A", "B"])], List(run));
    }

    /// <summary>A flagged result from a log older than this ticket has no key, and none can be derived from the SARIF alone.</summary>
    [Fact]
    public void A_carry_over_from_an_older_baseline_is_ungrouped()
    {
        SarifLog baseline = SarifReportWriter.Write(
        [
            Result("B", new Unknown(UnknownReason.Timeout, "slow")),
            Result("D", Diverges([Flagged(HashCodeCall)], [])) with { Replay = ReplayResult.Reproduced },
            Result("C", new Divergent(Fixtures.Counterexample())) with { Replay = ReplayResult.Reproduced },
        ]);
        foreach (Result old in baseline.Runs[0].Results)
        {
            old.RemoveProperty("reviewGroup");
            old.Rank = -1.0;
        }

        Run run = SarifReportWriter.Write([Result("A", new Unknown(UnknownReason.Timeout, "slow"))], baseline, reviewList: true).Runs[0];

        Assert.Equal(["timeout", "ungrouped", "ungrouped", "ungrouped"], run.Results.Select(Group), StringComparer.Ordinal);
        Assert.Equal(
            [
                new Entry("ungrouped", "EQ002", 80.002, 1, ["C"]),
                new Entry("ungrouped", "EQ006", 80.002, 1, ["D"]),
                new Entry("timeout", "EQ003", 0.002, 1, ["A"]),
                new Entry("ungrouped", "EQ003", 0.002, 1, ["B"]),
            ],
            List(run));
    }

    /// <summary><c>--lower-only</c>: the results still carry their group and rank, and the run has no list and prints no lines.</summary>
    [Fact]
    public void Without_the_list_results_keep_their_group_and_rank()
    {
        Run run = SarifReportWriter.Write([Result("A", new Unknown(UnknownReason.UnmatchedOverload, "two overloads"))]).Runs[0];

        Assert.Equal("unmatched-overload", Group(run.Results[0]));
        Assert.Equal(0.002, run.Results[0].Rank);
        Assert.DoesNotContain("reviewList", run.PropertyNames, StringComparer.Ordinal);
        Assert.Empty(ReviewList.Lines(run));
    }

    [Fact]
    public void Lines_are_the_count_line_then_one_line_per_group_by_rank()
    {
        Run run = Write(
            Result("T1", new Unknown(UnknownReason.Timeout, "slow")),
            Result("S1", Diverges(["N::Old()"], ["N::New()"])),
            Result("E1", new Equivalent(ProofMethod.Bounded)),
            Result("T2", new Unknown(UnknownReason.Timeout, "slow")),
            Result("S2", Diverges(["N::New()"], ["N::Old()"])),
            Result("S3", Diverges(["N::Old()"], ["N::New()"])));

        Assert.Equal(
            [
                "review list: 2 groups for 5 flagged results",
                "  EQ002 count=3 rank=60.006 calls:N::New()|N::Old()",
                "  EQ003 count=2 rank=0.004 timeout",
            ],
            ReviewList.Lines(run),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Lines_stop_after_ten_groups()
    {
        Run run = Write([.. Enumerable.Range(10, 11).Select(static i => Result(Numbered("T", i), Opaque(Numbered("R", i))))]);

        IReadOnlyList<string> lines = ReviewList.Lines(run);

        Assert.Equal(11, lines.Count);
        Assert.Equal("review list: 11 groups for 11 flagged results", lines[0]);
        Assert.Equal("  EQ003 count=1 rank=0.002 opaque:R10", lines[1]);
        Assert.Equal("  EQ003 count=1 rank=0.002 opaque:R19", lines[10]);
    }

    /// <summary>Against a baseline the lines count only <c>new</c> and <c>updated</c> results, while the run's list counts every one.</summary>
    [Fact]
    public void Lines_count_only_new_and_updated_results()
    {
        VerificationResult unchanged = Result("A", new Unknown(UnknownReason.Timeout, "slow"));
        VerificationResult divergent = Result("C", new Divergent(Fixtures.Counterexample()));
        SarifLog baseline = SarifReportWriter.Write([unchanged, Result("B", new Unknown(UnknownReason.Timeout, "slow")), divergent, Result("Gone", new Unknown(UnknownReason.Timeout, "slow"))]);

        Run run = SarifReportWriter.Write(
            [unchanged, Result("B", new Unknown(UnknownReason.Timeout, "slower")), divergent, Result("D", new Unknown(UnknownReason.Unbound, "legacy: unbound"))],
            baseline,
            reviewList: true).Runs[0];

        Assert.Equal(
            [BaselineState.Unchanged, BaselineState.Updated, BaselineState.Unchanged, BaselineState.New, BaselineState.Absent],
            run.Results.Select(static r => r.BaselineState));
        Assert.Equal(
            [
                "review list: 2 groups for 2 flagged results",
                "  EQ003 count=1 rank=0.006 timeout",
                "  EQ003 count=1 rank=0.002 unbound",
            ],
            ReviewList.Lines(run),
            StringComparer.Ordinal);
        Assert.Equal([1, 3, 1], List(run).Select(static e => e.Count));
    }

    [Fact]
    public void Lines_rejects_a_null_run()
    {
        Assert.Throws<ArgumentNullException>(static () => ReviewList.Lines(null!));
    }

    private static Run Write(params VerificationResult[] results) => SarifReportWriter.Write(results, reviewList: true).Runs[0];

    private static VerificationResult Result(string identity, Verdict verdict) => Fixtures.Result(verdict, identity);

    private static string Group(Result result) => result.GetProperty<string>("reviewGroup");

    private static List<Entry> List(Run run) => run.GetProperty<List<Entry>>("reviewList");

    private static Dictionary<string, (string? Group, double Rank)> ByIdentity(Run run) =>
        run.Results.ToDictionary(
            static r => r.PartialFingerprints["procedureIdentity/v1"],
            static r => (r.TryGetProperty("reviewGroup", out string? group) ? group : null, r.Rank),
            StringComparer.Ordinal);

    private static string Numbered(string prefix, int number) => prefix + number.ToString(CultureInfo.InvariantCulture);

    private static CallIdentity Flagged(string identity) => new(identity, RuntimeChanged: true);

    private static Divergent Diverges(string[] old, string[] @new) =>
        Diverges([.. old.Select(static c => new CallIdentity(c))], [.. @new.Select(static c => new CallIdentity(c))]);

    private static Divergent Diverges(CallIdentity[] old, CallIdentity[] @new) => new(Fixtures.Counterexample() with
    {
        Old = Fixtures.Run() with { Trace = [.. old.Select(static c => new IrCallRecord(c, []))] },
        New = Fixtures.Run(2) with { Trace = [.. @new.Select(static c => new IrCallRecord(c, []))] },
    });

    private static Divergent Observed()
    {
        ExecutionInput input = new(["null"]);
        return Divergent.Observation(new ObservedDivergence(
            new ExecutionOutcome(input, "tr-TR", OutcomeKind.Threw, "\"System.ArgumentNullException\""),
            new ExecutionOutcome(input, "tr-TR", OutcomeKind.Returned, "1")));
    }

    private static Unknown Opaque(params string[] reasons) =>
        new(UnknownReason.Opaque, "an input reaches an opaque node") { Causes = [.. reasons.Select(static r => Cause(Codebase.Modern, r))] };

    /// <summary>An <c>abstraction</c> Unknown over one opaque fragment per non-null reason and one <c>IrPure</c> operator per null.</summary>
    private static Unknown Abstracted(string?[] reasons) => Unknown.DependingOn(
        Fixtures.Counterexample(),
        [.. reasons.Select(static r => new Abstraction(Codebase.Modern, new CallIdentity(r is null ? "f32.mul" : $"opaque:{r}"), Span: null) { Reason = r })]);

    /// <summary><paramref name="verdict"/> with every trace, cause list and abstraction list in the opposite order.</summary>
    private static Verdict Reversed(Verdict verdict) => verdict switch
    {
        Divergent divergent => new Divergent(divergent.Counterexample with
        {
            Old = divergent.Counterexample.Old with { Trace = [.. divergent.Counterexample.Old.Trace.Reverse()] },
            New = divergent.Counterexample.New with { Trace = [.. divergent.Counterexample.New.Trace.Reverse()] },
        }),
        Unknown unknown => unknown with { Causes = [.. unknown.Causes.Reverse()], Abstractions = [.. unknown.Abstractions.Reverse()] },
        _ => verdict,
    };

    private static UnknownCause Cause(Codebase side, string reason) => new(side, reason, Span(1));

    private static SourceSpan Span(int line) => new("New.cs", line, 1, line, 2);

    /// <summary>One <c>run.properties.reviewList</c> entry, as it deserialises.</summary>
    private sealed record Entry(string Group, string RuleId, double Rank, int Count, IReadOnlyList<string> Identities)
    {
        public bool Equals(Entry? other) =>
            other is not null
            && string.Equals(Group, other.Group, StringComparison.Ordinal)
            && string.Equals(RuleId, other.RuleId, StringComparison.Ordinal)
            && Rank.Equals(other.Rank)
            && Count == other.Count
            && Identities.SequenceEqual(other.Identities, StringComparer.Ordinal);

        public override int GetHashCode() => HashCode.Combine(Group, RuleId, Rank, Count);
    }
}
