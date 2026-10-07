using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;
using Equiv.Verify.Z3.Refinement;

using Microsoft.Z3;

using Xunit;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Ticket P1-031 (ADR 0025 and ADR 0026, clarifications of 2026-10-07): a rung 1 query that hits its budget is asked
/// again with the pair's multiplications, divisions and remainders of two unknowns as functions both sides share, and a
/// model that depends on one is replayed with the real arithmetic or refines the abstraction.
/// </summary>
public sealed class ArithmeticRefinementTests
{
    /// <summary>
    /// The pair of <c>samples/hard-arithmetic</c>: a product of three 64-bit unknowns whose first factor one side picks
    /// before it multiplies and the other after. The two are equal for any function in place of the multiplication, and
    /// Z3 does not prove it of the multiplier within 30,000,000 units.
    /// </summary>
    private const string MovedBranch = """
        proc "T::Volume(bool, long, long, long, long)" (%f: bool, %a: bv64, %d: bv64, %b: bv64, %c: bv64) -> bv64 entry B0
        B0:
          br %f, B1, B2
        B1:
          goto B3
        B2:
          goto B3
        B3:
          %x: bv64 = phi [B1: %a, B2: %d]
          %t: bv64 = mul %x, %b
          %p: bv64 = mul %t, %c
          ret %p
        ---
        proc "T::Volume(bool, long, long, long, long)" (%f: bool, %a: bv64, %d: bv64, %b: bv64, %c: bv64) -> bv64 entry B0
        B0:
          br %f, B1, B2
        B1:
          %t1: bv64 = mul %a, %b
          %p1: bv64 = mul %t1, %c
          ret %p1
        B2:
          %t2: bv64 = mul %d, %b
          %p2: bv64 = mul %t2, %c
          ret %p2
        """;

    /// <summary><see cref="MovedBranch"/> with one factor changed on the new side's second path: it multiplies by <c>b</c> twice.</summary>
    private const string FactorChanged = """
        proc "T::Volume(bool, long, long, long, long)" (%f: bool, %a: bv64, %d: bv64, %b: bv64, %c: bv64) -> bv64 entry B0
        B0:
          br %f, B1, B2
        B1:
          goto B3
        B2:
          goto B3
        B3:
          %x: bv64 = phi [B1: %a, B2: %d]
          %t: bv64 = mul %x, %b
          %p: bv64 = mul %t, %c
          ret %p
        ---
        proc "T::Volume(bool, long, long, long, long)" (%f: bool, %a: bv64, %d: bv64, %b: bv64, %c: bv64) -> bv64 entry B0
        B0:
          br %f, B1, B2
        B1:
          %t1: bv64 = mul %a, %b
          %p1: bv64 = mul %t1, %c
          ret %p1
        B2:
          %t2: bv64 = mul %d, %b
          %p2: bv64 = mul %t2, %b
          ret %p2
        """;

    /// <summary><c>x * y</c> against <c>y * x</c>: equal by an algebra the shared function does not have.</summary>
    private const string Commuted = """
        proc "T::M(long, long)" (%a: bv64, %b: bv64) -> bv64 entry B0
        B0:
          %p: bv64 = mul %a, %b
          ret %p
        ---
        proc "T::M(long, long)" (%a: bv64, %b: bv64) -> bv64 entry B0
        B0:
          %p: bv64 = mul %b, %a
          ret %p
        """;

    /// <summary>Always differs, whatever the product is: the new side returns one more.</summary>
    private const string OneMore = """
        proc "T::M(long, long)" (%a: bv64, %b: bv64) -> bv64 entry B0
        B0:
          %p: bv64 = mul %a, %b
          ret %p
        ---
        proc "T::M(long, long)" (%a: bv64, %b: bv64) -> bv64 entry B0
        B0:
          %p: bv64 = mul %a, %b
          %one: bv64 = const bv64 1
          %q: bv64 = add %p, %one
          ret %q
        """;

    /// <summary>At 3 and 5 the old side returns the product and the new side 15: the abstraction must learn that one point.</summary>
    private const string FixedProduct = """
        proc "T::M(long, long)" (%a: bv64, %b: bv64) -> bv64 entry B0
        B0:
          %three: bv64 = const bv64 3
          %five: bv64 = const bv64 5
          %x: bool = eq %a, %three
          %y: bool = eq %b, %five
          %g: bool = and %x, %y
          br %g, B1, B2
        B1:
          %p: bv64 = mul %a, %b
          ret %p
        B2:
          %zero: bv64 = const bv64 0
          ret %zero
        ---
        proc "T::M(long, long)" (%a: bv64, %b: bv64) -> bv64 entry B0
        B0:
          %three: bv64 = const bv64 3
          %five: bv64 = const bv64 5
          %x: bool = eq %a, %three
          %y: bool = eq %b, %five
          %g: bool = and %x, %y
          br %g, B1, B2
        B1:
          %fifteen: bv64 = const bv64 15
          ret %fifteen
        B2:
          %zero: bv64 = const bv64 0
          ret %zero
        """;

    /// <summary><see cref="FixedProduct"/> for the overflow test: at 3 and 5 a signed 32-bit product does not overflow.</summary>
    private const string FixedOverflow = """
        proc "T::M(int, int)" (%a: bv32, %b: bv32) -> bool entry B0
        B0:
          %three: bv32 = const bv32 3
          %five: bv32 = const bv32 5
          %x: bool = eq %a, %three
          %y: bool = eq %b, %five
          %g: bool = and %x, %y
          %no: bool = const bool false
          br %g, B1, B2
        B1:
          %o: bool = overflows smul %a, %b
          ret %o
        B2:
          ret %no
        ---
        proc "T::M(int, int)" (%a: bv32, %b: bv32) -> bool entry B0
        B0:
          %no: bool = const bool false
          ret %no
        """;

    /// <summary>An opaque node only an input with 3 times 5 other than 15 reaches: none does.</summary>
    private const string OpaqueBehindAFixedProduct = """
        proc "T::M(long, long)" (%a: bv64, %b: bv64) -> bv64 entry B0
        B0:
          %three: bv64 = const bv64 3
          %five: bv64 = const bv64 5
          %fifteen: bv64 = const bv64 15
          %x: bool = eq %a, %three
          %y: bool = eq %b, %five
          %g: bool = and %x, %y
          %p: bv64 = mul %a, %b
          %wrong: bool = ne %p, %fifteen
          %both: bool = and %g, %wrong
          br %both, B1, B2
        B1:
          opaque "Throw" at "T.cs" 4:13-4:40
          ret %a
        B2:
          ret %a
        ---
        proc "T::M(long, long)" (%a: bv64, %b: bv64) -> bv64 entry B0
        B0:
          ret %a
        """;

    /// <summary>A loop both sides have, taken only when 3 times 5 is not 15: no input goes past any bound.</summary>
    private const string LoopBehindAFixedProduct = """
        proc "T::Spin(long, long)" (%a: bv64, %b: bv64) entry B0
        B0:
          goto B1
        B1:
          %three: bv64 = const bv64 3
          %five: bv64 = const bv64 5
          %fifteen: bv64 = const bv64 15
          %x: bool = eq %a, %three
          %y: bool = eq %b, %five
          %g: bool = and %x, %y
          %p: bv64 = mul %a, %b
          %wrong: bool = ne %p, %fifteen
          %both: bool = and %g, %wrong
          br %both, B1, B2
        B2:
          ret
        """;

    /// <summary>A counted loop with no hard arithmetic: some input goes past any bound.</summary>
    private const string CountedLoop = """
        proc "T::Count(int)" (%n: bv32) -> bv32 entry B0
        B0:
          %z: bv32 = const bv32 0
          %one: bv32 = const bv32 1
          goto B1
        B1:
          %i: bv32 = phi [B0: %z, B2: %i1]
          %c: bool = slt %i, %n
          br %c, B2, B3
        B2:
          %i1: bv32 = add %i, %one
          goto B1
        B3:
          ret %i
        """;

    /// <summary>A checked signed division as the frontend lowers it: the zero test, the <c>MinValue / -1</c> test, then the quotient.</summary>
    private const string GuardedDivision = """
        proc "T::Div(int, int)" (%a: bv32, %b: bv32) -> bv32 entry B0
        B0:
          %zero: bv32 = const bv32 0
          %z: bool = eq %b, %zero
          br %z, B1, B2
        B1:
          throw "System.DivideByZeroException"
        B2:
          %o: bool = overflows sdiv %a, %b
          br %o, B3, B4
        B3:
          throw "System.OverflowException"
        B4:
          %q: bv32 = sdiv %a, %b
          ret %q
        """;

    /// <summary><see cref="GuardedDivision"/> without its zero test.</summary>
    private const string DivisionWithoutTheZeroTest = """
        proc "T::Div(int, int)" (%a: bv32, %b: bv32) -> bv32 entry B0
        B0:
          %o: bool = overflows sdiv %a, %b
          br %o, B3, B4
        B3:
          throw "System.OverflowException"
        B4:
          %q: bv32 = sdiv %a, %b
          ret %q
        """;

    /// <summary><see cref="GuardedDivision"/> without its <c>MinValue / -1</c> test.</summary>
    private const string DivisionWithoutTheOverflowTest = """
        proc "T::Div(int, int)" (%a: bv32, %b: bv32) -> bv32 entry B0
        B0:
          %zero: bv32 = const bv32 0
          %z: bool = eq %b, %zero
          br %z, B1, B2
        B1:
          throw "System.DivideByZeroException"
        B2:
          %q: bv32 = sdiv %a, %b
          ret %q
        """;

    /// <summary>Every operation the abstraction replaces, each once, at two widths for the multiplication.</summary>
    private const string EveryOperator = """
        proc "T::M(long, long, int, int)" (%a: bv64, %b: bv64, %i: bv32, %j: bv32) -> bool entry B0
        B0:
          %m: bv64 = mul %a, %b
          %sd: bv64 = sdiv %a, %b
          %sr: bv64 = srem %a, %b
          %ud: bv64 = udiv %a, %b
          %ur: bv64 = urem %a, %b
          %n: bv32 = mul %i, %j
          %so: bool = overflows smul %i, %j
          %uo: bool = overflows umul %i, %j
          ret %so
        """;

    /// <summary>The same operators with a constant operand on either side, and operators of two unknowns the abstraction leaves alone.</summary>
    private const string ConstantOperands = """
        proc "T::M(long, long)" (%a: bv64, %b: bv64) -> bool entry B0
        B0:
          %two: bv64 = const bv64 2
          %m: bv64 = mul %a, %two
          %n: bv64 = mul %two, %b
          %d: bv64 = udiv %a, %two
          %r: bv64 = srem %two, %b
          %s: bv64 = add %a, %b
          %x: bv64 = xor %a, %b
          %l: bool = slt %a, %b
          %mo: bool = overflows smul %a, %two
          %no: bool = overflows umul %two, %b
          %ao: bool = overflows sadd %a, %b
          %do: bool = overflows sdiv %a, %b
          ret %mo
        """;

    /// <summary>
    /// The default resource limit ends every hard query here in about a second. The timeout behind it is a minute and not
    /// more, so that a mutant of the encoders that makes a query slow without spending the limit is ended by it.
    /// </summary>
    private static readonly VerificationOptions Options = new(3, 60_000, []) { RefineTimeouts = false };

    /// <summary>A limit every query here runs out of, the abstracted ones too.</summary>
    private static readonly VerificationOptions Starved = Options with { ResourceLimit = 1 };

    private const string StarvedDetail = "solver returned unknown (canceled): resource limit 1 hit";

    private const string Proved = "no loop or self-call; every input checked";

    [Fact]
    public void AbstractedOperators_AreSharedByBothSides()
    {
        using Context context = new();
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(MovedBranch);
        ArithmeticAbstraction arithmetic = new(context);

        ProductEncoding encoding = ProductEncoder.EncodeAbstracted(context, old, @new, [], arithmetic);

        Assert.Same(arithmetic, encoding.Arithmetic);
        Assert.Equal(6, arithmetic.Applications.Count);
        Assert.Equal("arith.Mul.64", Assert.Single(arithmetic.Applications.Select(static a => a.Function.Name.ToString()).Distinct(StringComparer.Ordinal)));
        Assert.Single(arithmetic.Applications.Select(static a => a.Function.Id).Distinct());
        string text = string.Join('\n', encoding.Assertions.Select(static a => a.ToString()));
        Assert.DoesNotContain("bvmul", text, StringComparison.Ordinal);
        Assert.Contains("(= old.t (arith.Mul.64 old.x in.b))", text, StringComparison.Ordinal);
        Assert.Contains("(= new.t1 (arith.Mul.64 in.a in.b))", text, StringComparison.Ordinal);
        Assert.Contains("(= new.p2 (arith.Mul.64 new.t2 in.c))", text, StringComparison.Ordinal);

        ProductEncoding exact = ProductEncoder.Encode(context, old, @new, []);
        Assert.Null(exact.Arithmetic);
        Assert.Contains("bvmul", string.Join('\n', exact.Assertions.Select(static a => a.ToString())), StringComparison.Ordinal);
    }

    /// <summary>One function per operator and width, each of both operands' sort, and the overflow tests' Bool.</summary>
    [Fact]
    public void AbstractedOperators_AreOnePerOperatorAndWidth()
    {
        using Context context = new();
        IrProcedure procedure = IrText.Parse(EveryOperator);
        ArithmeticAbstraction arithmetic = new(context);

        ProductEncoder.EncodeAbstracted(context, procedure, procedure, [], arithmetic);

        string[] names = ["arith.Mul.64", "arith.SDiv.64", "arith.SRem.64", "arith.UDiv.64", "arith.URem.64", "arith.Mul.32", "arith.overflows.SMul.32", "arith.overflows.UMul.32"];
        Assert.Equal([.. names, .. names], arithmetic.Applications.Select(static a => a.Function.Name.ToString()), StringComparer.Ordinal);
        Assert.Equal(names.Length, arithmetic.Applications.Select(static a => a.Function.Id).Distinct().Count());
        Assert.All(arithmetic.Applications, a =>
        {
            bool overflows = a.Function.Name.ToString().Contains("overflows", StringComparison.Ordinal);
            Assert.Equal(overflows ? context.BoolSort : a.A.Sort, a.Function.Range);
            Assert.Equal([a.A.Sort, a.B.Sort], a.Function.Domain);
        });
        Assert.True(ArithmeticAbstraction.AppliesTo(procedure));
    }

    [Fact]
    public void ConstantOperand_IsNeverAbstracted()
    {
        using Context context = new();
        IrProcedure procedure = IrText.Parse(ConstantOperands);
        ArithmeticAbstraction arithmetic = new(context);

        ProductEncoding encoding = ProductEncoder.EncodeAbstracted(context, procedure, procedure, [], arithmetic);

        Assert.Empty(arithmetic.Applications);
        Assert.False(ArithmeticAbstraction.AppliesTo(procedure));
        Assert.Equal(
            ProductEncoder.Encode(context, procedure, procedure, [], traces: ProductEncoder.TraceComparison.Positional).Assertions.Select(static a => a.ToString()),
            encoding.Assertions.Select(static a => a.ToString()),
            StringComparer.Ordinal);
    }

    public static TheoryData<string, bool> Operations => new()
    {
        { "%r: bv64 = mul %a, %b", true },
        { "%r: bv64 = sdiv %a, %b", true },
        { "%r: bv64 = srem %a, %b", true },
        { "%r: bv64 = udiv %a, %b", true },
        { "%r: bv64 = urem %a, %b", true },
        { "%r: bool = overflows smul %a, %b", true },
        { "%r: bool = overflows umul %a, %b", true },
        { "%r: bv64 = mul %a, %k", false },
        { "%r: bv64 = mul %k, %b", false },
        { "%r: bool = overflows smul %a, %k", false },
        { "%r: bool = overflows umul %k, %b", false },
        { "%r: bv64 = add %a, %b", false },
        { "%r: bv64 = shl %a, %b", false },
        { "%r: bool = overflows sadd %a, %b", false },
        { "%r: bool = overflows usub %a, %b", false },
        { "%r: bool = overflows sdiv %a, %b", false },
    };

    /// <summary>Which operations make a pair worth asking again: the five operators and the two overflow tests, of two unknowns.</summary>
    [Theory]
    [MemberData(nameof(Operations))]
    public void AppliesTo_IsTheOperatorsOfTheDesignOnTwoUnknowns(string operation, bool applies)
    {
        IrProcedure procedure = IrText.Parse($"""
            proc "T::M(long, long)" (%a: bv64, %b: bv64) entry B0
            B0:
              %k: bv64 = const bv64 7
              {operation}
              ret
            """);

        Assert.Equal(applies, ArithmeticAbstraction.AppliesTo(procedure));
    }

    [Fact]
    public void DivisionGuards_StayExact()
    {
        using Context context = new();
        IrProcedure guarded = IrText.Parse(GuardedDivision);
        ArithmeticAbstraction arithmetic = new(context);

        ProductEncoding encoding = ProductEncoder.EncodeAbstracted(context, guarded, guarded, [], arithmetic);

        Assert.Equal(["arith.SDiv.32", "arith.SDiv.32"], arithmetic.Applications.Select(static a => a.Function.Name.ToString()), StringComparer.Ordinal);
        string text = string.Join('\n', encoding.Assertions.Select(static a => a.ToString()));
        Assert.DoesNotContain("arith.overflows", text, StringComparison.Ordinal);
        Assert.Contains("(= old.z (= in.b old.zero))", text, StringComparison.Ordinal);
        Assert.Contains("(= old.q (arith.SDiv.32 in.a in.b))", text, StringComparison.Ordinal);

        // So a dropped guard is found with the quotient abstracted: the exception does not depend on the function.
        Divergent zero = Assert.IsType<Divergent>(Forced.Verify(guarded, IrText.Parse(DivisionWithoutTheZeroTest), Options));
        Assert.Equal(0ul, ((IrBitVecValue)zero.Counterexample.Inputs.Arguments[1]).Bits);
        Assert.Equal(new IrThrew("System.DivideByZeroException"), zero.Counterexample.Old.Outcome);
        Divergent overflow = Assert.IsType<Divergent>(Forced.Verify(guarded, IrText.Parse(DivisionWithoutTheOverflowTest), Options));
        Assert.Equal([0x8000_0000ul, 0xFFFF_FFFFul], overflow.Counterexample.Inputs.Arguments.Cast<IrBitVecValue>().Select(static v => v.Bits));
        Assert.Equal(new IrThrew("System.OverflowException"), overflow.Counterexample.Old.Outcome);
        Assert.All([zero, overflow], static d => Assert.Equal((RungOutcome.Refuted, 0), (d.Ladder[^1].Outcome, d.Ladder[^1].FactsAdded)));
    }

    /// <summary>
    /// Criterion 3 on the sample's pair: Equivalent at the default budget, the timeout followed by one round, and the
    /// Unknown(timeout) it was with the path off.
    /// </summary>
    [Fact]
    public void Abstracted_UnsatisfiableIsEquivalent()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(MovedBranch);
        string timedOut = "solver returned unknown (canceled): resource limit 2000000 hit";

        Verdict verdict = new Z3Backend().Verify(old, @new, Options);
        Verdict without = new Z3Backend { Arithmetic = ArithmeticMode.Off }.Verify(old, @new, Options);

        Assert.Equal(
            new Equivalent(ProofMethod.Bounded)
            {
                Ladder =
                [
                    new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, timedOut),
                    new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, $"arithmetic abstracted, round 1: {Proved}") { FactsAdded = 0 },
                ],
            },
            verdict);
        Assert.True(verdict.Ladder[^1].DecidedAbstracted);
        Assert.Equal(new Unknown(UnknownReason.Timeout, timedOut) { Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, timedOut)] }, without);
    }

    /// <summary>The abstraction applies when either side holds the arithmetic, and not to a timeout of a pair that holds none.</summary>
    [Fact]
    public void ATimeout_IsAskedAgainOnlyWhenASideHoldsHardArithmetic()
    {
        Fixture hard = Fixture.Load("hard-multiplication");
        Fixture plain = Fixture.Load("equivalent-refactor");

        Assert.True(new Z3Backend().Verify(hard.Old, hard.New, Starved).Ladder.Length > 1);
        Assert.True(new Z3Backend().Verify(hard.New, hard.Old, Starved).Ladder.Length > 1);
        Assert.Equal(
            new Unknown(UnknownReason.Timeout, StarvedDetail) { Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, StarvedDetail)] },
            new Z3Backend().Verify(plain.Old, plain.New, Starved));
    }

    /// <summary>A query that is decided on the exact product is never asked again, whatever the pair holds.</summary>
    [Fact]
    public void ADecidedQuery_IsNotAskedAgain()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(FactorChanged);

        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(old, @new, Options));

        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Refuted, "a divergence within 3 iterations"), Assert.Single(divergent.Ladder));
    }

    /// <summary>
    /// A round the solver gives up in ends the refinement, and is no cause of its own: the Unknown keeps the detail of the
    /// exact product's timeout. With the path forced there is no such step, and the round's is the detail.
    /// </summary>
    [Fact]
    public void ARoundThatTimesOut_EndsTheRefinementAndLeavesTheTimeoutItWas()
    {
        Fixture fixture = Fixture.Load("hard-multiplication");
        LadderStep round = new(ProofMethod.Bounded, RungOutcome.Timeout, $"arithmetic abstracted, round 1: {StarvedDetail}") { FactsAdded = 0 };

        Verdict verdict = new Z3Backend().Verify(fixture.Old, fixture.New, Starved);
        Verdict forced = Forced.Verify(fixture.Old, fixture.New, Starved);

        Assert.Equal(new Unknown(UnknownReason.Timeout, StarvedDetail) { Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, StarvedDetail), round] }, verdict);
        Assert.Equal(new Unknown(UnknownReason.Timeout, round.Detail) { Ladder = [round] }, forced);
        Assert.False(round.DecidedAbstracted);
    }

    [Theory]
    [InlineData(FixedProduct)]
    [InlineData(FixedOverflow)]
    [InlineData(OpaqueBehindAFixedProduct)]
    public void SpuriousModel_AddsPointFacts(string pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(pair);

        Verdict verdict = Forced.Verify(old, @new, Options);

        Assert.Equal(
            new Equivalent(ProofMethod.Bounded)
            {
                Ladder =
                [
                    new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, "arithmetic abstracted, round 1: the model is spurious; operator results it gets wrong: 1") { FactsAdded = 1 },
                    new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, $"arithmetic abstracted, round 2: {Proved}") { FactsAdded = 0 },
                ],
            },
            verdict);
    }

    /// <summary>The bound query's model is refined as the others are: the loop no input takes is proved within the bound.</summary>
    [Fact]
    public void SpuriousModel_OfTheBoundQuery_AddsPointFacts()
    {
        IrProcedure procedure = IrText.Parse(LoopBehindAFixedProduct);

        Verdict verdict = Forced.Verify(procedure, procedure, Options);

        Assert.Equal(
            new Equivalent(ProofMethod.Bounded, 3)
            {
                Ladder =
                [
                    new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, "arithmetic abstracted, round 1: the model is spurious; operator results it gets wrong: 1") { FactsAdded = 1 },
                    new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, "arithmetic abstracted, round 2: no input goes past the bound 3") { FactsAdded = 0 },
                ],
            },
            verdict);
    }

    /// <summary>A model that is one of the exact product is read as rung 1 reads any: a loop some input stays in, an opaque node some input reaches.</summary>
    [Fact]
    public void AModelOfTheExactProduct_IsReadAsRungOneReadsIt()
    {
        IrProcedure loop = IrText.Parse(CountedLoop);
        Fixture opaque = Fixture.Load("opaque-void-effect");

        Verdict looping = Forced.Verify(loop, loop, Options);
        Unknown reached = Assert.IsType<Unknown>(Forced.Verify(opaque.Old, opaque.New, Options));
        Unknown exact = Assert.IsType<Unknown>(new Z3Backend().Verify(opaque.Old, opaque.New, Options));

        Assert.Equal(
            new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, "arithmetic abstracted, round 1: no divergence within the bound 3, and some input goes past it") { FactsAdded = 0 },
            looping.Ladder[0]);
        Assert.Equal(ProofMethod.LockstepInduction, Assert.IsType<Equivalent>(looping).Method);
        Assert.Equal((UnknownReason.Opaque, exact.Detail, exact.Scope), (reached.Reason, reached.Detail, reached.Scope));
        Assert.Equal(exact.Causes, reached.Causes);
        Assert.Equal(exact.Ladder[0] with { Detail = $"arithmetic abstracted, round 1: {exact.Ladder[0].Detail}", FactsAdded = 0 }, Assert.Single(reached.Ladder));
    }

    [Fact]
    public void RealModel_IsDivergentAfterReplay()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(FactorChanged);
        Fixture plain = Fixture.Load("return-value");

        Divergent divergent = Assert.IsType<Divergent>(Forced.Verify(old, @new, Options));
        Divergent exact = Assert.IsType<Divergent>(Forced.Verify(plain.Old, plain.New, Options));

        LadderStep step = Assert.Single(divergent.Ladder);
        Assert.Equal((RungOutcome.Refuted, 0, true), (step.Outcome, step.FactsAdded, step.DecidedAbstracted));
        AssertReplays(divergent, old, @new);
        Assert.Equal(
            new LadderStep(ProofMethod.Bounded, RungOutcome.Refuted, "arithmetic abstracted, round 1: a divergence within 3 iterations") { FactsAdded = 0 },
            Assert.Single(exact.Ladder));
    }

    /// <summary>
    /// A model that gets the product wrong, of a pair whose inputs diverge whatever the product is: the replay computes the
    /// real product, diverges, and refutes the pair; nothing is learned.
    /// </summary>
    [Fact]
    public void Spurious_AModelWhoseInputsReallyDiverge_RefutesThePair()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(OneMore);
        using Refining refining = new(old, @new, wrong: true);

        LoopLadder.Rung? rung = refining.Refinement.Spurious(refining.Model, (old, @new));

        Assert.NotNull(rung);
        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Refuted, "the model's inputs diverge under the real operators"), rung.Step);
        AssertReplays(Assert.IsType<Divergent>(rung.Verdict), old, @new);
        Assert.Empty(refining.Refinement.Facts);
    }

    /// <summary>The same model with no pair to replay it through, as the opaque and the bound queries give it, is only spurious.</summary>
    [Fact]
    public void Spurious_WithoutAReplay_AddsTheFactsOfABrokenModel()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(OneMore);
        using Refining refining = new(old, @new, wrong: true);

        LoopLadder.Rung? rung = refining.Refinement.Spurious(refining.Model, replay: null);

        Assert.NotNull(rung);
        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, "the model is spurious; operator results it gets wrong: 1"), rung.Step);
        Assert.Null(rung.Verdict);

        // Both sides apply the function to the same operands: one fact, the real product at the model's operands.
        BoolExpr fact = Assert.Single(refining.Refinement.Facts);
        AssertFact(refining, fact, refining.Arithmetic.Applications[0]);
        Assert.True(refining.Model.Eval(refining.Context.MkNot(fact), completion: true).IsTrue);
    }

    /// <summary>A model that gives every application the real operator's value is not spurious, and adds nothing.</summary>
    [Fact]
    public void Spurious_AModelOfTheExactProduct_IsNotSpurious()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(OneMore);
        using Refining refining = new(old, @new, wrong: false);

        Assert.Null(refining.Refinement.Spurious(refining.Model, (old, @new)));
        Assert.Empty(refining.Refinement.Facts);
        Assert.Empty(refining.Arithmetic.Broken(refining.Model));
    }

    /// <summary>Each application the model gets wrong is a fact, in application order; the two sides' applications differ here.</summary>
    [Fact]
    public void Broken_ListsEveryApplicationTheModelGetsWrongOnce()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Commuted);
        using Refining refining = new(old, @new, wrong: true, distinctOperands: true);

        BoolExpr[] facts = [.. refining.Arithmetic.Broken(refining.Model)];

        Assert.Equal(2, facts.Length);
        AssertFact(refining, facts[0], refining.Arithmetic.Applications[0]);
        AssertFact(refining, facts[1], refining.Arithmetic.Applications[1]);
        Assert.NotEqual(facts[0].ToString(), facts[1].ToString(), StringComparer.Ordinal);
    }

    [Fact]
    public void Refinement_StopsAfterEightRounds()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Commuted);

        Unknown unknown = Assert.IsType<Unknown>(Forced.Verify(old, @new, Options));

        Assert.Equal(8, ArithmeticRefinement.MaxRounds);
        Assert.Equal(ArithmeticRefinement.MaxRounds, unknown.Ladder.Length);
        Assert.Equal((UnknownReason.Timeout, unknown.Ladder[^1].Detail), (unknown.Reason, unknown.Detail));
        Assert.All(unknown.Ladder.Select(static (step, index) => (step, index)), static round =>
        {
            Assert.Equal((ProofMethod.Bounded, RungOutcome.Inconclusive), (round.step.Rung, round.step.Outcome));
            Assert.True(round.step.FactsAdded > 0);
            Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"arithmetic abstracted, round {round.index + 1}: the model is spurious; operator results it gets wrong: {round.step.FactsAdded}"), round.step.Detail);
        });
    }

    /// <summary>
    /// Criterion 4: the abstraction is too coarse for <c>x * y</c> against <c>y * x</c>. Z3 decides the original, so the
    /// pair is Equivalent by the exact product; asked of the abstracted one it is never Divergent, at any number of rounds.
    /// </summary>
    [Fact]
    public void CommutedProduct_IsNeverDivergent()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Commuted);

        Verdict verdict = new Z3Backend().Verify(old, @new, Options);

        Assert.Equal(new Equivalent(ProofMethod.Bounded) { Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, Proved)] }, verdict);
        Assert.IsType<Unknown>(Forced.Verify(old, @new, Options));
        Assert.IsType<Unknown>(Forced.Verify(@new, old, Options));
    }

    /// <summary>At <c>debug</c> an abstracted round's queries are stages of their own names.</summary>
    [Fact]
    public void TheRoundsQueriesAreStages()
    {
        RecordingRunLog log = new(isDebug: true);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(FixedProduct);
        IrProcedure loop = IrText.Parse(LoopBehindAFixedProduct);

        Forced.Verify(old, @new, Options with { Log = log });
        Forced.Verify(loop, loop, Options with { Log = log });

        string[] checks = [.. log.Events.Where(static e => e.Contains("stage=check:", StringComparison.Ordinal)).Select(static e => e[..e.IndexOf(" took=", StringComparison.Ordinal)] + e[e.IndexOf(" result=", StringComparison.Ordinal)..])];
        Assert.Equal(
            [
                "detail stage=check:abstracted-divergence result=sat",
                "detail stage=check:abstracted-divergence result=unsat",
                "detail stage=check:abstracted-opaque result=unsat",
                "detail stage=check:abstracted-divergence result=unsat",
                "detail stage=check:abstracted-opaque result=unsat",
                "detail stage=check:abstracted-bound result=sat",
                "detail stage=check:abstracted-divergence result=unsat",
                "detail stage=check:abstracted-opaque result=unsat",
                "detail stage=check:abstracted-bound result=unsat",
            ],
            checks);
        Assert.Equal(2, log.Events.Count(static e => e.StartsWith("detail rung=bounded ", StringComparison.Ordinal)));
    }

    /// <summary>Rung 1 run on its own, as the soundness harness runs it, is asked as the ladder is set to; its result is its last step.</summary>
    [Fact]
    public void RungOneOnItsOwn_IsAskedInTheLaddersMode()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(MovedBranch);

        IReadOnlyList<LoopLadder.Rung> rungs = [.. new LoopLadder(static () => new Context(), Options) { Arithmetic = ArithmeticMode.Forced }.Independently(old, @new).Take(1)];

        Assert.Equal(new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, $"arithmetic abstracted, round 1: {Proved}") { FactsAdded = 0 }, Assert.Single(rungs).Step);

        LoopLadder.Rung last = new LoopLadder(static () => new Context(), Options).Independently(old, @new).First();
        Assert.Equal((RungOutcome.Proved, 0), (last.Step.Outcome, last.Step.FactsAdded));
        Assert.IsType<Equivalent>(last.Verdict);
    }

    /// <summary><paramref name="fact"/> says the function of <paramref name="application"/>, at the operands the model gives it, is their real product.</summary>
    private static void AssertFact(Refining refining, BoolExpr fact, ArithmeticAbstraction.Application application)
    {
        ulong a = ((BitVecNum)refining.Model.Eval(application.A, completion: true)).UInt64;
        ulong b = ((BitVecNum)refining.Model.Eval(application.B, completion: true)).UInt64;
        Assert.True(fact.IsEq);
        Assert.Equal("arith.Mul.64", fact.Arg(0).FuncDecl.Name.ToString());
        Assert.Equal([a, b], fact.Arg(0).Args.Cast<BitVecNum>().Select(static n => n.UInt64));
        Assert.Equal(unchecked(a * b), ((BitVecNum)refining.Model.Eval(fact.Arg(1), completion: true)).UInt64);
    }

    private static Z3Backend Forced => new() { Arithmetic = ArithmeticMode.Forced };

    private static void AssertReplays(Divergent divergent, IrProcedure old, IrProcedure @new)
    {
        Counterexample counterexample = divergent.Counterexample;
        Assert.NotEqual(counterexample.Old.Outcome, counterexample.New.Outcome);
        Assert.Equal(counterexample.Old.Outcome, IrGen.Run(old, counterexample.Inputs).Outcome);
        Assert.Equal(counterexample.New.Outcome, IrGen.Run(@new, counterexample.Inputs).Outcome);
    }

    /// <summary>
    /// A pair's abstracted product and one model of it, of a query that says only whether every application has the real
    /// product's value or none has, and that the first one's operands differ when asked to.
    /// </summary>
    private sealed class Refining : IDisposable
    {
        private readonly Solver solver;

        public Refining(IrProcedure old, IrProcedure @new, bool wrong, bool distinctOperands = false)
        {
            Arithmetic = new ArithmeticAbstraction(Context);
            ProductEncoding encoding = ProductEncoder.EncodeAbstracted(Context, old, @new, [], Arithmetic);
            Refinement = new ArithmeticRefinement(Context, encoding, Arithmetic, Options);
            ArithmeticAbstraction.Application first = Arithmetic.Applications[0];
            solver = Context.MkSolver();
            solver.Add(encoding.Assertions);
            foreach (ArithmeticAbstraction.Application application in Arithmetic.Applications)
            {
                BoolExpr real = Context.MkEq(Context.MkApp(application.Function, application.A, application.B), Context.MkBVMul(application.A, application.B));
                solver.Add(wrong ? Context.MkNot(real) : real);
            }

            solver.Add(distinctOperands ? Context.MkBVUGT(first.A, first.B) : Context.MkTrue());
            Assert.Equal(Status.SATISFIABLE, solver.Check());
            Model = new SolverModel(solver.Model, Context, Context);
        }

        public Context Context { get; } = new();

        public ArithmeticAbstraction Arithmetic { get; }

        public ArithmeticRefinement Refinement { get; }

        public SolverModel Model { get; }

        public void Dispose()
        {
            solver.Dispose();
            Context.Dispose();
        }
    }
}
