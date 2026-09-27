using System.Collections.Immutable;
using System.Globalization;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Verdicts;
using Equiv.Verify.Z3.Ladder;

using Microsoft.Z3;

using Xunit;

using Rung = Equiv.Verify.Z3.LoopLadder.Rung;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Rung 5 on its own (ticket P1-002): <see cref="LlmInvariantRung"/> called on a pair directly with a scripted proposer,
/// so no earlier rung runs. <c>loops/fusion</c> is <c>samples/loop-fusion</c>'s shape: the old side counts in two loops,
/// the new side in one.
/// </summary>
public sealed class LlmInvariantRungTests
{
    /// <summary>
    /// A coupling invariant for <c>loops/fusion</c> that holds with wrap-around arithmetic: in lockstep through the first
    /// loop, every counter between 0 and <c>max(n, 0)</c> and <c>inside</c> at most <c>i</c>, so no subtraction wraps; then
    /// the new side has returned <c>max(n, 0) - inside</c> while the old side counts <c>total</c> up to <c>max(n, 0)</c>.
    /// </summary>
    internal const string FusionInvariant = """
        (define-fun inv.B1.B1 ((in.n Int) (in.low Int) (in.high Int) (old.i Int) (old.in Int) (new.i Int) (new.in Int) (new.t Int)) Bool
          (and (= old.i new.i) (= old.in new.in) (= new.t new.i) (<= 0 new.in) (<= new.in new.i) (<= 0 new.i) (or (= new.i 0) (<= new.i in.n))))
        (define-fun inv.B1.exit ((in.n Int) (in.low Int) (in.high Int) (old.i Int) (old.in Int) (new.returned Bool) (new.value Int) (new.exception Int)) Bool false)
        (define-fun inv.B6.B1 ((in.n Int) (in.low Int) (in.high Int) (old.j Int) (old.t Int) (old.in Int) (new.i Int) (new.in Int) (new.t Int)) Bool false)
        (define-fun inv.B6.exit ((in.n Int) (in.low Int) (in.high Int) (old.j Int) (old.t Int) (old.in Int) (new.returned Bool) (new.value Int) (new.exception Int)) Bool
          (and new.returned (= new.exception 0) (= old.t old.j) (<= 0 old.j) (or (= old.j 0) (<= old.j in.n)) (<= 0 old.in)
               (<= old.in (ite (< in.n 0) 0 in.n)) (= new.value (- (ite (< in.n 0) 0 in.n) old.in))))
        (define-fun inv.exit.B1 ((in.n Int) (in.low Int) (in.high Int) (old.returned Bool) (old.value Int) (old.exception Int) (new.i Int) (new.in Int) (new.t Int)) Bool false)
        (define-fun inv.exit.exit ((in.n Int) (in.low Int) (in.high Int) (old.returned Bool) (old.value Int) (old.exception Int) (new.returned Bool) (new.value Int) (new.exception Int)) Bool
          (and (= old.returned new.returned) (= old.value new.value) (= old.exception new.exception)))
        """;

    private static readonly string[] FusionSignatures =
    [
        "inv.B1.B1 ((in.n Int) (in.low Int) (in.high Int) (old.i Int) (old.in Int) (new.i Int) (new.in Int) (new.t Int))",
        "inv.B1.exit ((in.n Int) (in.low Int) (in.high Int) (old.i Int) (old.in Int) (new.returned Bool) (new.value Int) (new.exception Int))",
        "inv.B6.B1 ((in.n Int) (in.low Int) (in.high Int) (old.j Int) (old.t Int) (old.in Int) (new.i Int) (new.in Int) (new.t Int))",
        "inv.B6.exit ((in.n Int) (in.low Int) (in.high Int) (old.j Int) (old.t Int) (old.in Int) (new.returned Bool) (new.value Int) (new.exception Int))",
        "inv.exit.B1 ((in.n Int) (in.low Int) (in.high Int) (old.returned Bool) (old.value Int) (old.exception Int) (new.i Int) (new.in Int) (new.t Int))",
        "inv.exit.exit ((in.n Int) (in.low Int) (in.high Int) (old.returned Bool) (old.value Int) (old.exception Int) (new.returned Bool) (new.value Int) (new.exception Int))",
    ];

    private static readonly VerificationOptions Options = new(3, 10_000, []) { InvariantModel = "fake-model" };

    private static readonly Fixture Fusion = Fixture.Load("loops/fusion");

    [Fact]
    public void Rung_AcceptsOnlyWhenAllObligationsUnsat()
    {
        FakeInvariantProposer proposer = new(Everywhere("false"), Everywhere("true"), FusionInvariant);

        ImmutableArray<Rung> rounds = Prove(proposer);

        Assert.Equal(3, rounds.Length);
        Assert.StartsWith("round 1: Z3 rejected fake-model's candidate (define-fun inv.B1.B1 ", rounds[0].Step.Detail, StringComparison.Ordinal);
        Assert.Contains(": the init obligation fails: entry -> inv.B1.B1(in.n = ", rounds[0].Step.Detail, StringComparison.Ordinal);
        Assert.Contains(": the exit obligation fails: inv.exit.exit(", rounds[1].Step.Detail, StringComparison.Ordinal);
        Assert.EndsWith(") -> bad", rounds[1].Step.Detail, StringComparison.Ordinal);
        Assert.All(rounds.Take(2), static r => Assert.Equal((ProofMethod.LlmInvariant, RungOutcome.Inconclusive, (Verdict?)null), (r.Step.Rung, r.Step.Outcome, r.Verdict)));
        Assert.Equal((ProofMethod.LlmInvariant, RungOutcome.Proved), (rounds[2].Step.Rung, rounds[2].Step.Outcome));
        Equivalent proved = Assert.IsType<Equivalent>(rounds[2].Verdict);
        Assert.Equal(ProofMethod.LlmInvariant, proved.Method);
        Assert.Equal("fake-model", proved.ProposedBy);
        Assert.StartsWith("(define-fun inv.B1.B1 ((in.n Int) ", proved.Invariant, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', proved.Invariant!);
        Assert.EndsWith(proved.Invariant!, rounds[2].Step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Rung_FeedsCounterexampleBack()
    {
        string counting = FusionInvariant.Replace("(<= 0 new.i) (or (= new.i 0) (<= new.i in.n))", "(= new.i 0)", StringComparison.Ordinal);
        FakeInvariantProposer proposer = new(counting, FusionInvariant);

        ImmutableArray<Rung> rounds = Prove(proposer);

        Assert.IsType<Equivalent>(rounds[^1].Verdict);
        Assert.Equal(2, proposer.Requests.Count);
        Assert.Empty(proposer.Requests[0].Rejected);
        InvariantRequest.Rejection rejection = Assert.Single(proposer.Requests[1].Rejected);
        Assert.Equal(counting, rejection.Candidate);
        Assert.StartsWith("the step obligation fails: inv.B1.B1(in.n = ", rejection.Reason, StringComparison.Ordinal);
        Assert.Contains(", new.i = 0, ", rejection.Reason, StringComparison.Ordinal);
        Assert.Contains(") -> inv.B1.B1(in.n = ", rejection.Reason, StringComparison.Ordinal);
        Assert.Contains(", new.i = 1, ", rejection.Reason, StringComparison.Ordinal);
        Assert.Equal((proposer.Requests[0].OldIr, proposer.Requests[0].NewIr, proposer.Requests[0].Relations), (proposer.Requests[1].OldIr, proposer.Requests[1].NewIr, proposer.Requests[1].Relations));
    }

    /// <summary>The request names both procedures' IR and every relation with its arguments' names and sorts.</summary>
    [Fact]
    public void Rung_SendsTheIrAndEveryRelation()
    {
        FakeInvariantProposer proposer = new();

        Prove(proposer);

        InvariantRequest request = Assert.Single(proposer.Requests);
        Assert.Equal(Core.Ir.IrText.Dump(Fusion.Old), request.OldIr);
        Assert.Equal(Core.Ir.IrText.Dump(Fusion.New), request.NewIr);
        Assert.Equal(["inv.B1.B1", "inv.B1.exit", "inv.B6.B1", "inv.B6.exit", "inv.exit.B1", "inv.exit.exit"], [.. request.Relations.Select(static r => r.Name)]);
        Assert.Equal(
            [new("in.n", "Int"), new("in.low", "Int"), new("in.high", "Int"), new("old.i", "Int"), new("old.in", "Int"), new("new.returned", "Bool"), new("new.value", "Int"), new("new.exception", "Int")],
            request.Relations[1].Parameters);
    }

    [Fact]
    public void Rung_StopsAtMaxRounds()
    {
        FakeInvariantProposer proposer = new(Everywhere("true"), Everywhere("true"), Everywhere("true"), FusionInvariant);

        ImmutableArray<Rung> rounds = Prove(proposer);

        Assert.Equal(LlmInvariantRung.MaxRounds, proposer.Requests.Count);
        Assert.Equal(LlmInvariantRung.MaxRounds, rounds.Length);
        Unknown unknown = Assert.IsType<Unknown>(rounds[^1].Verdict);
        Assert.Equal(UnknownReason.NoInvariant, unknown.Reason);
        Assert.Equal("no invariant fake-model proposed was admitted in 3 round(s)", unknown.Detail);
        Assert.StartsWith("round 3: Z3 rejected fake-model's candidate ", rounds[^1].Step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Rung_StopsWhenTheProposerGivesUp()
    {
        ImmutableArray<Rung> rounds = Prove(new FakeInvariantProposer(Everywhere("true"), null, FusionInvariant));

        Assert.Equal(2, rounds.Length);
        Assert.Equal("round 2: fake-model proposed no invariant", rounds[1].Step.Detail);
        Assert.Equal("no invariant fake-model proposed was admitted in 2 round(s)", Assert.IsType<Unknown>(rounds[1].Verdict).Detail);
    }

    [Theory]
    [InlineData("(define-fun inv.B1.B1", "it does not parse as a definition of inv.B1.B1: ")]
    [InlineData("(define-fun inv.B1.B1 () Bool true)", "it does not parse as a definition of inv.B1.B1: ")]
    [InlineData("(exit)", "it asserts no definition of inv.B1.B1")]
    [InlineData("(declare-const z Int) (define-fun inv.B1.B1 ((a Int) (b Int) (c Int) (d Int) (e Int) (f Int) (g Int) (h Int)) Bool (= z d))", "its definition of inv.B1.B1 uses z, which is not one of that relation's arguments")]
    [InlineData("(declare-fun inv.B1.B1 (Int Int Int Int Int Int Int Int) Bool)", "its definition of inv.B1.B1 uses inv.B1.B1, which is not one of that relation's arguments")]
    [InlineData("(declare-fun f (Int) Int) (define-fun inv.B1.B1 ((a Int) (b Int) (c Int) (d Int) (e Int) (f0 Int) (g Int) (h Int)) Bool (exists ((k Int)) (= (f k) d)))", "its definition of inv.B1.B1 uses f, which is not one of that relation's arguments")]
    public void Rung_ParseFailureIsRejection(string candidate, string reason)
    {
        FakeInvariantProposer proposer = new(candidate);

        ImmutableArray<Rung> rounds = Prove(proposer);

        Assert.StartsWith(reason, Assert.Single(proposer.Requests[1].Rejected).Reason, StringComparison.Ordinal);
        Assert.Equal(RungOutcome.Inconclusive, rounds[0].Step.Outcome);
    }

    /// <summary>A quantified definition parses, with its bound variable; only the arguments and bound variables are allowed.</summary>
    [Fact]
    public void Rung_AdmitsAQuantifiedDefinition()
    {
        string quantified = FusionInvariant.Replace("(<= 0 new.in) (<= new.in new.i)", "(exists ((k Int)) (and (<= 0 k) (= new.in (- new.i k)) (<= 0 new.in)))", StringComparison.Ordinal);

        Assert.IsType<Equivalent>(Prove(new FakeInvariantProposer(quantified))[^1].Verdict);
    }

    /// <summary>
    /// A relation over an uninterpreted sort and maps of it (<c>loops/array-count</c>): the request gives each sort as
    /// SMT-LIB prints it, and a candidate written with those sorts parses, so Z3 judges it on its obligations.
    /// </summary>
    [Fact]
    public void Rung_ParsesSortsAndMapsAsTheRequestSpellsThem()
    {
        Fixture arrays = Fixture.Load("loops/array-count");
        FakeInvariantProposer asking = new();
        new LlmInvariantRung(static () => new Context(), Options, asking, "fake-model").Prove(arrays.Old, arrays.New);
        InvariantRequest request = Assert.Single(asking.Requests);
        Assert.Contains(request.Relations.SelectMany(static r => r.Parameters), static p => p.Sort.StartsWith("(Array |int[]| (Array Int Int))", StringComparison.Ordinal));
        string candidate = string.Join('\n', request.Relations.Select(static r => $"(define-fun {r.Name} ({string.Join(' ', r.Parameters.Select(static p => $"({p.Name} {p.Sort})"))}) Bool true)"));
        FakeInvariantProposer proposer = new(candidate);

        new LlmInvariantRung(static () => new Context(), Options, proposer, "fake-model").Prove(arrays.Old, arrays.New);

        Assert.StartsWith("the exit obligation fails: ", proposer.Requests[1].Rejected[0].Reason, StringComparison.Ordinal);
    }

    /// <summary>An obligation Z3 cannot decide within the timeout rejects the candidate, naming the obligation.</summary>
    [Fact]
    public void Rung_TimeoutIsRejection()
    {
        FakeInvariantProposer proposer = new(FusionInvariant);

        new LlmInvariantRung(static () => new Context(), Options with { TimeoutMs = 1 }, proposer, "fake-model").Prove(Fusion.Old, Fusion.New);

        Assert.StartsWith("Z3 gave up on the ", Assert.Single(proposer.Requests[1].Rejected).Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Soundness (ADR 0036): <c>loops/int-proof-wraps</c> is Divergent over the bitvectors, though equal over the plain
    /// integers, so no definition of its relations can be admitted. Random linear predicates over each relation's
    /// arguments never are.
    /// </summary>
    [Fact]
    public void RandomWrongInvariantIsNeverAccepted()
    {
        Fixture wraps = Fixture.Load("loops/int-proof-wraps");
        Gen<string> atom = Gen.Select(Gen.Int[-2, 2], Gen.Int[-2, 2], Gen.Int[-2, 2], Gen.Int[-6, 6], Gen.OneOfConst("<=", "=", ">="))
            .Select(static t => string.Create(CultureInfo.InvariantCulture, $"({t.Item5} (+ (* {t.Item1} x) (* {t.Item2} i) (* {t.Item3} k)) {t.Item4})"));
        Gen<string> predicate = Gen.Frequency(
            (1, Gen.OneOfConst("true", "false")),
            (4, atom.Array[1, 3].Select(Gen.OneOfConst("and", "or"), static (atoms, op) => $"({op} {string.Join(' ', atoms)})")));
        Gen<string> candidates = Gen.Select(predicate, predicate, Gen.OneOfConst("true", "false", "(= old.value new.value)", "(and old.returned (= old.value new.value))"))
            .Select(static t => $"""
                (define-fun inv.B1.exit ((x Int) (i Int) (k Int) (r Bool) (v Bool) (e Int)) Bool (and (=> r {t.Item1}) (=> (not r) {t.Item2})))
                (define-fun inv.exit.exit ((in.x Int) (old.returned Bool) (old.value Bool) (old.exception Int) (new.returned Bool) (new.value Bool) (new.exception Int)) Bool {t.Item3})
                """);

        candidates.Sample(
            candidate =>
            {
                ImmutableArray<Rung> rounds = new LlmInvariantRung(static () => new Context(), Options, new FakeInvariantProposer(candidate), "fake-model").Prove(wraps.Old, wraps.New);
                Assert.IsNotType<Equivalent>(rounds[^1].Verdict);
            },
            iter: 50,
            threads: 1);
    }

    /// <summary>
    /// Criteria 2 and 5 through the ladder: with a 1 ms timeout rung 4 gives up on <c>loops/fusion</c>, and rung 5, given
    /// its own timeout, proves the pair with the fake proposer's second candidate. The snapshot holds the verdict and the
    /// ladder from rung 4 on (rungs 1 to 3 at 1 ms may time out or not).
    /// </summary>
    [Fact]
    public Task LadderSnapshot_FakeProposerSolvesLoopFusionAfterRungFourTimesOut()
    {
        FakeInvariantProposer proposer = new(Everywhere("true"), FusionInvariant);
        LoopLadder ladder = new(static () => new Context(), Options with { TimeoutMs = 1 }, proposer) { InvariantTimeoutMs = 10_000 };

        Verdict verdict = ladder.Verify(Fusion.Old, Fusion.New);

        Equivalent proved = Assert.IsType<Equivalent>(verdict);
        LadderStep[] fromRungFour = [.. verdict.Ladder.SkipWhile(static s => s.Rung != ProofMethod.Chc)];
        Assert.Equal(RungOutcome.Timeout, fromRungFour[0].Outcome);
        return VerifyXunit.Verifier.Verify(string.Join(
            '\n',
            [
                $"{proved.Method} proposedBy={proved.ProposedBy}",
                $"invariant: {proved.Invariant}",
                "Chc Timeout",
                .. fromRungFour.Skip(1).Select(static s => $"{s.Rung} {s.Outcome}: {s.Detail}"),
            ]));
    }

    /// <summary>Criterion 4: without a proposer (no <c>--invariant-model</c>) the ladder stops at rung 4's Unknown.</summary>
    [Fact]
    public void WithoutAProposerTheLadderReportsRungFoursUnknown()
    {
        Verdict verdict = new LoopLadder(static () => new Context(), Options with { TimeoutMs = 1, InvariantModel = null }).Verify(Fusion.Old, Fusion.New);

        Assert.Equal(UnknownReason.ChcTimeout, Assert.IsType<Unknown>(verdict).Reason);
        Assert.Equal(ProofMethod.Chc, verdict.Ladder[^1].Rung);
    }

    /// <summary>
    /// Criterion 4 through the backend: it builds a proposer for the named model only, and rung 5's obligations share the
    /// run's timeout, so at 1 ms they give up too and the pair is Unknown(NoInvariant).
    /// </summary>
    [Fact]
    public void TheBackendAsksTheNamedModelOnlyWhenOneIsNamed()
    {
        List<string> asked = [];
        FakeInvariantProposer proposer = new(FusionInvariant);
        Z3Backend backend = new(static () => new Context(), model =>
        {
            asked.Add(model);
            return proposer;
        });

        backend.Verify(Fusion.Old, Fusion.New, Options with { TimeoutMs = 1, InvariantModel = null });
        Verdict verdict = backend.Verify(Fusion.Old, Fusion.New, Options with { TimeoutMs = 1 });

        Assert.Equal(["fake-model"], asked);
        Assert.Equal(UnknownReason.NoInvariant, Assert.IsType<Unknown>(verdict).Reason);
        Assert.StartsWith("Z3 gave up on the ", proposer.Requests[1].Rejected[0].Reason, StringComparison.Ordinal);
    }

    private static ImmutableArray<Rung> Prove(FakeInvariantProposer proposer) =>
        new LlmInvariantRung(static () => new Context(), Options, proposer, "fake-model").Prove(Fusion.Old, Fusion.New);

    /// <summary><c>loops/fusion</c>'s relations, each defined as <paramref name="body"/>.</summary>
    private static string Everywhere(string body) => string.Join('\n', FusionSignatures.Select(s => $"(define-fun {s} Bool {body})"));
}
