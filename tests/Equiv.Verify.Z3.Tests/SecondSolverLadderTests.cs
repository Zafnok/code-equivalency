using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Ticket P1-033 criteria 2 and 3 (ADR 0050): rung 1 asks a second solver the queries Z3 gives up on. Z3 is starved
/// here by its resource limit, which ends a hard query at the same point on every machine and still lets it answer a
/// query whose constants are all fixed, which is what a read-back is.
/// </summary>
public sealed class SecondSolverLadderTests
{
    /// <summary>The new side returns one more when <c>a * b</c> is a constant and neither is 1: a divergence Z3 must factor to find.</summary>
    private const string Factoring = """
        proc "T::F(uint,uint)" (%a: bv32, %b: bv32) -> bv32 entry B0
        B0:
          %p: bv32 = mul %a, %b
          ret %p
        ---
        proc "T::F(uint,uint)" (%a: bv32, %b: bv32) -> bv32 entry B0
        B0:
          %p: bv32 = mul %a, %b
          %c: bv32 = const bv32 2147483629
          %one: bv32 = const bv32 1
          %hit: bool = eq %p, %c
          %x: bool = ne %a, %one
          %y: bool = ne %b, %one
          %xy: bool = and %x, %y
          %both: bool = and %hit, %xy
          br %both, B1, B2
        B1:
          %q: bv32 = add %p, %one
          ret %q
        B2:
          ret %p
        """;

    /// <summary>The 64-bit division identity of <c>hard-multiplication.ir</c> beside a branch that reaches an opaque node on the old side only.</summary>
    private const string HardBesideAnOpaque = """
        proc "T::Rebuild(ulong,ulong,int)" (%a: bv64, %b: bv64, %n: bv32) -> bv64 entry B0
        B0:
          %zero: bv32 = const bv32 0
          %negative: bool = slt %n, %zero
          br %negative, B1, B2
        B1:
          opaque "Throw" at "T.cs" 4:13-4:40
          ret %a
        B2:
          %q: bv64 = udiv %a, %b
          %m: bv64 = mul %q, %b
          %r: bv64 = urem %a, %b
          %s: bv64 = add %m, %r
          ret %s
        ---
        proc "T::Rebuild(ulong,ulong,int)" (%a: bv64, %b: bv64, %n: bv32) -> bv64 entry B0
        B0:
          ret %a
        """;

    /// <summary>An opaque node only an input that breaks the division identity reaches: none does, and Z3 cannot say so.</summary>
    private const string OpaqueBehindAHardCondition = """
        proc "T::Rebuild(ulong,ulong)" (%a: bv64, %b: bv64) -> bv64 entry B0
        B0:
          %q: bv64 = udiv %a, %b
          %m: bv64 = mul %q, %b
          %r: bv64 = urem %a, %b
          %s: bv64 = add %m, %r
          %broken: bool = ne %s, %a
          br %broken, B1, B2
        B1:
          opaque "Throw" at "T.cs" 4:13-4:40
          ret %a
        B2:
          ret %a
        ---
        proc "T::Rebuild(ulong,ulong)" (%a: bv64, %b: bv64) -> bv64 entry B0
        B0:
          ret %a
        """;

    /// <summary>A loop both sides have, taken only when <c>a * b</c> is the constant: whether any input goes past the bound is the hard query.</summary>
    private const string LoopBehindAFactoring = """
        proc "T::Spin(uint,uint)" (%a: bv32, %b: bv32) entry B0
        B0:
          goto B1
        B1:
          %p: bv32 = mul %a, %b
          %c: bv32 = const bv32 2147483629
          %one: bv32 = const bv32 1
          %hit: bool = eq %p, %c
          %x: bool = ne %a, %one
          %y: bool = ne %b, %one
          %xy: bool = and %x, %y
          %both: bool = and %hit, %xy
          br %both, B1, B2
        B2:
          ret
        """;

    /// <summary>The loop of <c>ABoundOnlyAHardConditionReachesTimesOutInRungOnesLastQuery</c>: no input takes the back edge.</summary>
    private const string LoopBehindAHardCondition = """
        proc "T::Rebuild(ulong, ulong)" (%a: bv64, %b: bv64) entry B0
        B0:
          goto B1
        B1:
          %q: bv64 = udiv %a, %b
          %m: bv64 = mul %q, %b
          %r: bv64 = urem %a, %b
          %s: bv64 = add %m, %r
          %broken: bool = ne %s, %a
          br %broken, B1, B2
        B2:
          ret
        """;

    private static readonly SolverUse Cvc5 = new(ScriptedSolver.SolverName, ScriptedSolver.SolverVersion);

    /// <summary>A limit the hard queries here run out of and a query with every constant fixed does not.</summary>
    private static readonly VerificationOptions Starved = new(3, 600_000, []) { ResourceLimit = 10_000 };

    private const string StarvedDetail = "solver returned unknown (canceled): resource limit 10000 hit";

    [Fact]
    public void WithoutASolver_AHardQueryIsATimeout()
    {
        Unknown unknown = Assert.IsType<Unknown>(Verify(Factoring, solver: null));

        Assert.Equal((UnknownReason.Timeout, StarvedDetail), (unknown.Reason, unknown.Detail));
        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, StarvedDetail), Assert.Single(unknown.Ladder));
    }

    [Fact]
    public void ASatWhoseValuesReplayToADivergence_IsDivergent()
    {
        ScriptedSolver solver = new(ScriptedSolver.Solve);

        Divergent divergent = Assert.IsType<Divergent>(Verify(Factoring, solver));

        LadderStep step = Assert.Single(divergent.Ladder);
        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Refuted, "a divergence within 3 iterations") { Solver = Cvc5 }, step);
        ulong[] inputs = [.. divergent.Counterexample.Inputs.Arguments.Cast<IrBitVecValue>().Select(static v => v.Bits)];
        Assert.Equal(2147483629u, (uint)(inputs[0] * inputs[1]));
        Assert.DoesNotContain(1ul, inputs);
        Assert.NotEqual(divergent.Counterexample.Old.Outcome, divergent.Counterexample.New.Outcome);
        Assert.Single(solver.Scripts);
        Assert.Equal(TimeSpan.FromMilliseconds(600_000), Assert.Single(solver.Limits));
    }

    [Fact]
    public void ASatWhoseValuesReplayToNoDifference_IsATimeout()
    {
        // Every constant zero: both sides return 0, so nothing differs, and Z3 does not satisfy the query with those values.
        ScriptedSolver solver = new(ScriptedSolver.Zeros);

        AssertTimeout(Verify(Factoring, solver));
        Assert.Single(solver.Scripts);
    }

    [Fact]
    public void AnUnknown_IsATimeout()
    {
        ScriptedSolver solver = new(static _ => new SmtUnknown("resourceout"));

        AssertTimeout(Verify(Factoring, solver));
        Assert.Single(solver.Scripts);
    }

    [Fact]
    public void ASolverThatThrows_IsATimeout()
    {
        ScriptedSolver solver = new(static _ => throw new InvalidOperationException("the executable is gone"));

        AssertTimeout(Verify(Factoring, solver));
        Assert.Single(solver.Scripts);
    }

    public static TheoryData<string> Malformations => ["missing", "empty", "term", "narrow", "wide", "not-binary", "not-hex", "short-hex", "bool-as-bits", "bits-as-bool", "bare"];

    /// <summary>A right answer with one value spoiled: the read-back takes values as literals of the constant's own sort, or not at all.</summary>
    [Theory]
    [MemberData(nameof(Malformations))]
    public void AMalformedSat_IsATimeout(string malformation)
    {
        ScriptedSolver solver = new(script =>
        {
            ImmutableDictionary<string, string> values = ((SmtSat)ScriptedSolver.Solve(script)).Values;
            return new SmtSat(malformation switch
            {
                "missing" => values.Remove("in.a"),
                "empty" => [],
                "term" => values.SetItem("in.a", "(_ bv5 32)"),
                "narrow" => values.SetItem("in.a", values["in.a"][..^1]),
                "wide" => values.SetItem("in.a", values["in.a"] + "0"),
                "not-binary" => values.SetItem("in.a", "#b" + new string('2', 32)),
                "not-hex" => values.SetItem("in.a", "#xg0000000"),
                "short-hex" => values.SetItem("in.a", "#x0000000"),
                "bool-as-bits" => values.SetItem("new.hit", "#b1"),
                "bits-as-bool" => values.SetItem("in.a", "true"),
                _ => values.SetItem("in.a", "#"),
            });
        });

        AssertTimeout(Verify(Factoring, solver));
        Assert.Single(solver.Scripts);
    }

    /// <summary>The literals the read-back takes: <c>#x</c> in either case as well as <c>#b</c>.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASatInHexadecimal_IsReadBack(bool upper)
    {
        ScriptedSolver solver = new(script => new SmtSat(((SmtSat)ScriptedSolver.Solve(script)).Values.ToImmutableDictionary(
            static v => v.Key,
            v => v.Value.StartsWith("#b", StringComparison.Ordinal) ? "#x" + Convert.ToUInt32(v.Value[2..], 2).ToString(upper ? "X8" : "x8", System.Globalization.CultureInfo.InvariantCulture) : v.Value,
            StringComparer.Ordinal)));

        Assert.Equal(Cvc5, Assert.IsType<Divergent>(Verify(Factoring, solver)).Ladder[0].Solver);
    }

    [Fact]
    public void AnUnsatOnDivergence_ContinuesToOpaque_WhichZ3Answers()
    {
        ScriptedSolver solver = new(static _ => new SmtUnsat());

        Unknown unknown = Assert.IsType<Unknown>(Verify(HardBesideAnOpaque, solver));

        Assert.Equal((UnknownReason.Opaque, "old: Throw", UnknownScope.Line), (unknown.Reason, unknown.Detail, unknown.Scope));
        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, "an input reaches an opaque node: old: Throw") { Solver = Cvc5 }, Assert.Single(unknown.Ladder));
        Assert.Contains("old.s", Assert.Single(solver.Scripts), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnsatOnDivergence_WithNoOpaqueAndNoLoop_ProvesThePair()
    {
        Fixture fixture = Fixture.Load("hard-multiplication");
        ScriptedSolver solver = new(static _ => new SmtUnsat());

        Assert.IsType<Unknown>(new Z3Backend().Verify(fixture.Old, fixture.New, Starved));
        Equivalent equivalent = Assert.IsType<Equivalent>(new Z3Backend().Verify(fixture.Old, fixture.New, Starved with { Solver = solver }));

        Assert.Equal((ProofMethod.Bounded, null), (equivalent.Method, equivalent.BoundedBy));
        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, "no loop or self-call; every input checked") { Solver = Cvc5 }, Assert.Single(equivalent.Ladder));
        Assert.Single(solver.Scripts);
    }

    [Fact]
    public void AnOpaqueQueryZ3GivesUpOn_IsAskedOfTheSolver()
    {
        AssertTimeout(Verify(OpaqueBehindAHardCondition, solver: null));
        ScriptedSolver unsat = new(static _ => new SmtUnsat());
        ScriptedSolver unknown = new(static _ => new SmtUnknown("resourceout"));

        Equivalent equivalent = Assert.IsType<Equivalent>(Verify(OpaqueBehindAHardCondition, unsat));
        AssertTimeout(Verify(OpaqueBehindAHardCondition, unknown));

        Assert.Equal(Cvc5, Assert.Single(equivalent.Ladder).Solver);
        Assert.Equal(unsat.Scripts, unknown.Scripts);
        Assert.Contains("old.reach.B1", unsat.Scripts[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void ABoundQueryZ3GivesUpOn_IsAskedOfTheSolver_AndAnUnsatProvesThePairWithinTheBound()
    {
        IrProcedure procedure = IrText.Parse(LoopBehindAHardCondition);
        ScriptedSolver solver = new(static _ => new SmtUnsat());

        Equivalent equivalent = Assert.IsType<Equivalent>(new Z3Backend().Verify(procedure, procedure, Starved with { Solver = solver }));

        Assert.Equal((ProofMethod.Bounded, 3), (equivalent.Method, equivalent.BoundedBy));
        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, "no input goes past the bound 3") { Solver = Cvc5 }, Assert.Single(equivalent.Ladder));
        Assert.Single(solver.Scripts);
    }

    [Fact]
    public void ASatOnTheBoundQuery_ReadBack_SaysSomeInputGoesPastTheBound()
    {
        IrProcedure procedure = IrText.Parse(LoopBehindAFactoring);
        ScriptedSolver solver = new(ScriptedSolver.Solve);

        Verdict alone = new Z3Backend().Verify(procedure, procedure, Starved);
        Verdict verdict = new Z3Backend().Verify(procedure, procedure, Starved with { Solver = solver });

        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, StarvedDetail), alone.Ladder[0]);
        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, "no divergence within the bound 3, and some input goes past it") { Solver = Cvc5 }, verdict.Ladder[0]);
        Assert.All(verdict.Ladder.Skip(1), static step => Assert.Null(step.Solver));
        Assert.Single(solver.Scripts);
    }

    /// <summary>At <c>debug</c> the second solver's query and the read-back are a stage each, named after the query.</summary>
    [Fact]
    public void TheSolversQueriesAreStages()
    {
        RecordingRunLog log = new(isDebug: true);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Factoring);

        new Z3Backend().Verify(old, @new, Starved with { Solver = new ScriptedSolver(ScriptedSolver.Solve), Log = log });
        new Z3Backend().Verify(old, @new, Starved with { Solver = new ScriptedSolver(static _ => new SmtUnsat()), Log = log });
        new Z3Backend().Verify(old, @new, Starved with { Solver = new ScriptedSolver(static _ => new SmtUnknown("gave up")), Log = log });

        string[] checks = [.. log.Events.Where(static e => e.Contains("stage=check:", StringComparison.Ordinal)).Select(static e => e[..e.IndexOf(" took=", StringComparison.Ordinal)] + e[e.IndexOf(" result=", StringComparison.Ordinal)..])];
        Assert.Equal(
            [
                "detail stage=check:divergence result=unknown",
                "detail stage=check:divergence:cvc5 result=sat",
                "detail stage=check:divergence-read-back result=sat",
                "detail stage=check:divergence result=unknown",
                "detail stage=check:divergence:cvc5 result=unsat",
                "detail stage=check:opaque result=unsat",
                "detail stage=check:divergence result=unknown",
                "detail stage=check:divergence:cvc5 result=unknown",
            ],
            checks);
    }

    private static Verdict Verify(string pair, ScriptedSolver? solver)
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(pair);
        return new Z3Backend().Verify(old, @new, Starved with { Solver = solver });
    }

    private static void AssertTimeout(Verdict verdict)
    {
        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal((UnknownReason.Timeout, StarvedDetail), (unknown.Reason, unknown.Detail));
        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, StarvedDetail), Assert.Single(unknown.Ladder));
    }
}
