using System.Collections.Immutable;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Verify.Z3.Ladder;

using Microsoft.Z3;

using Xunit;

using Conjunct = Equiv.Verify.Z3.Ladder.InvariantTemplates.Conjunct;
using Rung = Equiv.Verify.Z3.LoopLadder.Rung;
using Sample = System.Collections.Generic.IReadOnlyDictionary<string, object>;
using Template = Equiv.Verify.Z3.Ladder.InvariantTemplates.Template;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Rung 5's local proposer (ticket P1-009): <see cref="TraceInvariantProposer"/> mines <see cref="InvariantTemplates"/>
/// from runs of both sides in <c>IrInterpreter</c>, and <see cref="LlmInvariantRung"/> checks its candidates as it checks
/// a model's.
/// </summary>
public sealed partial class TraceInvariantProposerTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    [Fact]
    public void Templates_KeepOnlyRelationsThatHoldOnEveryTrace()
    {
        ImmutableArray<InvariantRequest.Variable> parameters =
        [
            new("a", "Int"), new("b", "Int"), new("c", "Int"), new("d", "Int"), new("e", "Int"), new("f", "Int"),
            new("p", "Bool"), new("r", "Bool"), new("s", "S"), new("t", "S"), new("m", "Int"),
        ];
        IrSortValue x = new("S", 0);
        IrSortValue y = new("S", 1);
        IReadOnlyList<IReadOnlyList<Sample>> traces =
        [
            [
                Values(("a", 0L), ("b", 1L), ("c", 0L), ("d", 0L), ("e", 0L), ("f", 4L), ("p", true), ("r", false), ("s", x), ("t", x), ("m", 7L)),
                Values(("a", 1L), ("b", 2L), ("c", 2L), ("d", 5L), ("e", 3L), ("f", 4L), ("p", true), ("r", false), ("s", y), ("t", y)),
            ],
            [
                Values(("a", 2L), ("b", 3L), ("c", 4L), ("d", 1L), ("e", 0L), ("f", 4L), ("p", true), ("r", false), ("s", x), ("t", y), ("m", 7L)),
            ],
        ];

        ImmutableArray<Conjunct> mined = InvariantTemplates.Mine(parameters, traces);

        Assert.Equal(
            [
                "(= a (+ b (- 1)))", "(= c (* 2 a))", "(<= a c)", "(<= a f)", "(<= b f)", "(<= c f)", "(<= e d)", "(<= e f)",
                "(<= 0 e)", "(<= 4 f)", "(<= f 4)", "p", "(not r)",
            ],
            [.. mined.Select(static c => c.Smt)]);
        Assert.Equal(["false"], [.. InvariantTemplates.Mine(parameters, [[]]).Select(static c => c.Smt)]);
        Assert.Equal(["(= a b)"], [.. InvariantTemplates.Mine([new("a", "S"), new("b", "S")], [[Values(("a", x), ("b", x))], [Values(("a", y), ("b", y))]]).Select(static c => c.Smt)]);
    }

    /// <summary>Only an offset or factor of at most <see cref="InvariantTemplates.MaxConstant"/> is a template instance, and a factor is never 0 or 1.</summary>
    [Fact]
    public void Templates_KeepOnlySmallConstants()
    {
        ImmutableArray<InvariantRequest.Variable> parameters = [new("x", "Int"), new("y", "Int")];

        Assert.Equal(["(= x (+ y 64))"], Smt(parameters, (64, 0), (65, 1)));
        Assert.Equal(["(<= y x)"], Smt(parameters, (65, 0), (67, 1)));
        Assert.Equal(["(= x (* (- 64) y))", "(<= x y)"], Smt(parameters, (-64, 1), (-128, 2)));
        Assert.Equal(["(<= x y)"], Smt(parameters, (-65, 1), (-130, 2)));
        Assert.Equal(["(= x (+ y (- 1)))"], Smt(parameters, (-1, 0), (0, 1)));
        Assert.Equal(["(= y (* 3 x))", "(<= x y)"], Smt(parameters, (0, 0), (1, 3), (2, 6)));
        Assert.DoesNotContain(Smt(parameters, (3, 2), (2, 3)), static s => s.Contains('*', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Never", "x", "", 0, "", true)]
    [InlineData("Equal", "x", "y", 0, "x=1,y=1", false)]
    [InlineData("Equal", "x", "y", 0, "x=1,y=2", true)]
    [InlineData("Equal", "x", "y", 0, "x=true,y=false", true)]
    [InlineData("Equal", "x", "y", 0, "x=true,y=true", false)]
    [InlineData("Equal", "x", "y", 0, "x=|S!val!0|,y=|S!val!1|", false)]
    [InlineData("Offset", "x", "y", 2, "x=(- 1),y=-3", false)]
    [InlineData("Offset", "x", "y", 2, "x=1,y=3", true)]
    [InlineData("Scale", "x", "y", -2, "x=4,y=(- 2)", false)]
    [InlineData("Scale", "x", "y", -2, "x=4,y=2", true)]
    [InlineData("AtMost", "x", "y", 0, "x=4,y=4", false)]
    [InlineData("AtMost", "x", "y", 0, "x=5,y=4", true)]
    [InlineData("AtMost", "x", "y", 0, "x=5", false)]
    [InlineData("Lower", "x", "", 3, "x=3", false)]
    [InlineData("Lower", "x", "", 3, "x=2", true)]
    [InlineData("Upper", "x", "", 3, "x=4", true)]
    [InlineData("Upper", "x", "", 3, "y=4", false)]
    [InlineData("Truth", "x", "", 1, "x=false", true)]
    [InlineData("Truth", "x", "", 0, "x=false", false)]
    public void Conjunct_IsFalsifiedOnlyByValuesThatMakeItFalse(string template, string x, string y, long c, string values, bool falsified)
    {
        ArgumentNullException.ThrowIfNull(values);
        Dictionary<string, string> parsed = values.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(static v => v.Split('=')).ToDictionary(static v => v[0], static v => v[1], StringComparer.Ordinal);

        Assert.Equal(falsified, new Conjunct(Enum.Parse<Template>(template), x, y, c).Falsified(parsed));
    }

    [Theory]
    [InlineData("5", 5L)]
    [InlineData("-5", -5L)]
    [InlineData("(- 5)", -5L)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("((as const (Array Int Int)) 0)", null)]
    [InlineData("(- x)", null)]
    public void Read_ParsesIntegersAndBooleansAsZ3PrintsThem(string text, object? expected) =>
        Assert.Equal(expected, InvariantTemplates.Read(text));

    [Fact]
    public async Task Proposer_ReturnsNullWithNoCandidate()
    {
        InvariantRequest request = RequestFor(Fixture.Load("loops/state-unpaired"));
        string? first = await new TraceInvariantProposer().ProposeAsync(request, TestContext.Current.CancellationToken);

        InvariantRequest bare = request with { Relations = [new("inv.B1.B1", []), new("inv.B1.exit", [])] };
        InvariantRequest repeated = request with { Rejected = [new InvariantRequest.Rejection(first!, "Z3 gave up on the step obligation: timeout")] };

        Assert.StartsWith("(define-fun inv.B1.B1 ((in.n Int) (old.i Int) (new.i Int)) Bool (and (= old.i new.i) ", first, StringComparison.Ordinal);
        Assert.Null(await new TraceInvariantProposer().ProposeAsync(bare with { Relations = [bare.Relations[0]] }, TestContext.Current.CancellationToken));
        Assert.Equal(
            "(define-fun inv.B1.B1 () Bool true)\n(define-fun inv.B1.exit () Bool false)",
            await new TraceInvariantProposer().ProposeAsync(bare, TestContext.Current.CancellationToken));
        Assert.Null(await new TraceInvariantProposer().ProposeAsync(repeated, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Criterion 4 (<c>loops/phis-reordered</c>): the traces never reach the largest <c>n</c>, so the first candidate holds
    /// <c>old.i = old.a - 1</c>, which wraps around there; Z3's step counterexample falsifies it and every other conjunct
    /// over <c>a</c>, which the second candidate drops, keeping the rest, and Z3 admits that one.
    /// </summary>
    [Fact]
    public void Proposer_DropsConjunctsFalsifiedByCounterexample()
    {
        Fixture phis = Fixture.Load("loops/phis-reordered");

        ImmutableArray<Rung> rounds = new LlmInvariantRung(static () => new Context(), Options, new TraceInvariantProposer(), TraceInvariantProposer.Name, ProofMethod.TraceInvariant).Prove(phis.Old, phis.New);

        Assert.Equal(2, rounds.Length);
        Assert.Contains("(define-fun inv.B1.B1 ((in.n Int) (old.i Int) (old.a Int) (new.a Int) (new.i Int)) Bool (and (= old.i (+ old.a (- 1))) ", rounds[0].Step.Detail, StringComparison.Ordinal);
        Assert.Contains(": the step obligation fails: inv.B1.B1(in.n = 2147483647, ", rounds[0].Step.Detail, StringComparison.Ordinal);
        Equivalent proved = Assert.IsType<Equivalent>(rounds[1].Verdict);
        Assert.Equal((ProofMethod.TraceInvariant, "trace"), (proved.Method, proved.ProposedBy));
        Assert.StartsWith("(define-fun inv.B1.B1 ((in.n Int) (old.i Int) (old.a Int) (new.a Int) (new.i Int)) Bool (and (= old.i new.i) (= old.a new.a) (<= 0 old.i) (<= 0 new.i))) ", proved.Invariant, StringComparison.Ordinal);
        Assert.DoesNotContain("old.a (- 1)", proved.Invariant, StringComparison.Ordinal);
    }

    /// <summary>
    /// A counterexample's inputs run first: an integer or Boolean input takes its value, any other is random, and an
    /// input the counterexample does not name is random too.
    /// </summary>
    [Fact]
    public async Task Proposer_RunsTheCounterexamplesInputsFirst()
    {
        Fixture tangle = Fixture.Load("loops/irreducible");
        InvariantRequest request = RequestFor(tangle);
        InvariantRequest.Fact fact = new("inv.other", [new("in.c", "true"), new("in.n", "(- 7)"), new("in.unknown", "0")]);
        InvariantRequest.Rejection rejection = new("(define-fun inv.other () Bool true)", "the init obligation fails: entry -> inv.other(in.c = true)") { Premise = fact };

        string? candidate = await new TraceInvariantProposer().ProposeAsync(request with { Rejected = [rejection] }, TestContext.Current.CancellationToken);

        Assert.NotNull(candidate);
        Assert.Equal(request.Relations.Length, candidate.Split('\n').Length);
    }

    /// <summary>
    /// The proposer runs every kind of input (sorts and maps of <c>loops/array-count</c>, whose sides throw and pass the
    /// heap by reference) and stops a run that reaches an opaque node (<c>loops/loop-opaque</c>), so each gets a candidate.
    /// </summary>
    [Theory]
    [InlineData("loops/array-count", "(= old.array.int__ new.array.int__)")]
    [InlineData("loops/loop-opaque", "(define-fun inv.B1.B1 ((in.n Int) (old.i Int) (new.i Int)) Bool (and (= old.i new.i) ")]
    public async Task Proposer_RunsEveryKindOfInputAndStop(string name, string expected)
    {
        InvariantRequest request = RequestFor(Fixture.Load(name));
        InvariantRequest.Fact fact = new("inv.exit.exit", [new("in.values", "|int[]!val!0|"), new("in.n", "3")]);

        string? candidate = await new TraceInvariantProposer().ProposeAsync(request with { Rejected = [new InvariantRequest.Rejection("-", "-") { Conclusion = fact }] }, TestContext.Current.CancellationToken);

        Assert.Contains(expected, candidate, StringComparison.Ordinal);
    }

    /// <summary>
    /// Criteria 3 and 5, with the deviation in the ticket's Notes: with a 1 ms timeout rung 4 gives up on
    /// <c>loops/state-unpaired</c> (a constant hoisted out of the loop, so the headers carry different state and rungs 2 and
    /// 3 do not align them, whatever the timeout), and the trace proposer alone, on by default and asked first, proves it.
    /// The snapshot holds the verdict and the ladder from rung 4 on.
    /// </summary>
    [Fact]
    public Task StateUnpaired_ProvedByTraceProposer()
    {
        Fixture unpaired = Fixture.Load("loops/state-unpaired");
        LoopLadder ladder = new(static () => new Context(), Options with { TimeoutMs = 1 }) { InvariantTimeoutMs = 10_000 };

        Verdict verdict = ladder.Verify(unpaired.Old, unpaired.New);

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

    /// <summary><c>loops/counter-shape</c> (a counter pre-incremented from -1 against one from 0) needs the offset template: <c>old.i = new.k + 1</c>.</summary>
    [Fact]
    public void CounterShape_ProvedByTheOffsetTemplate()
    {
        Fixture shape = Fixture.Load("loops/counter-shape");

        ImmutableArray<Rung> rounds = new LlmInvariantRung(static () => new Context(), Options, new TraceInvariantProposer(), TraceInvariantProposer.Name, ProofMethod.TraceInvariant).Prove(shape.Old, shape.New);

        Equivalent proved = Assert.IsType<Equivalent>(Assert.Single(rounds).Verdict);
        Assert.StartsWith("(define-fun inv.B1.B1 ((in.n Int) (old.i Int) (new.k Int)) Bool (and (= old.i (+ new.k 1)) ", proved.Invariant, StringComparison.Ordinal);
    }

    /// <summary>
    /// Why criterion 5 names <c>loops/counter-shape</c> and not <c>loop-fusion</c> (ticket Notes): once the fused side has
    /// returned, the old side's second loop must end at the new side's final count, <c>max(n, 0)</c>, which no conjunction
    /// of linear relations states. The trace proposer's candidates are rejected, and the pair stays Unknown.
    /// </summary>
    [Fact]
    public void LoopFusion_NeedsMoreThanTheTemplates()
    {
        Fixture fusion = Fixture.Load("loops/fusion");

        Verdict verdict = new LoopLadder(static () => new Context(), Options with { TimeoutMs = 1 }) { InvariantTimeoutMs = 10_000 }.Verify(fusion.Old, fusion.New);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.NoInvariant, unknown.Reason);
        Assert.StartsWith("no invariant trace proposed was admitted in ", unknown.Detail, StringComparison.Ordinal);
        Assert.Equal(ProofMethod.TraceInvariant, verdict.Ladder[^1].Rung);
    }

    /// <summary>
    /// Soundness (ADR 0036), P1-002's property with this proposer: both pairs are Divergent over the bitvectors, so nothing
    /// the traces propose, from any seed, may be admitted.
    /// </summary>
    [Fact]
    public void RandomWrongInvariantIsNeverAccepted()
    {
        Fixture[] divergent = [Fixture.Load("loops/int-proof-wraps"), Fixture.Load("loops/trip-count-changed")];

        Gen.ULong.Sample(
            seed =>
            {
                foreach (Fixture fixture in divergent)
                {
                    ImmutableArray<Rung> rounds = new LlmInvariantRung(static () => new Context(), Options, new TraceInvariantProposer(seed), TraceInvariantProposer.Name, ProofMethod.TraceInvariant).Prove(fixture.Old, fixture.New);
                    Assert.IsNotType<Equivalent>(rounds[^1].Verdict);
                }
            },
            iter: 10,
            threads: 1);
    }

    /// <summary>The request rung 5 sends for <paramref name="fixture"/>, from a proposer that gives up at once.</summary>
    private static InvariantRequest RequestFor(Fixture fixture)
    {
        FakeInvariantProposer asking = new();
        new LlmInvariantRung(static () => new Context(), Options, asking, "fake-model", ProofMethod.LlmInvariant).Prove(fixture.Old, fixture.New);
        return Assert.Single(asking.Requests);
    }

    private static Dictionary<string, object> Values(params (string Name, object Value)[] values) =>
        values.ToDictionary(static v => v.Name, static v => v.Value, StringComparer.Ordinal);

    /// <summary>What <see cref="InvariantTemplates.Mine"/> keeps over <c>x</c> and <c>y</c>, one trace per sample.</summary>
    private static string[] Smt(ImmutableArray<InvariantRequest.Variable> parameters, params (long X, long Y)[] samples) =>
        [.. InvariantTemplates.Mine(parameters, [.. samples.Select(static s => (IReadOnlyList<Sample>)[Values(("x", s.X), ("y", s.Y))])]).Select(static c => c.Smt)];
}
