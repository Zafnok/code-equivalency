using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Microsoft.Z3;

using Xunit;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;
using TraceComparison = Equiv.Verify.Z3.ProductEncoder.TraceComparison;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Ticket P1-038: rung 1 compares the call traces by position (<see cref="PositionalTrace"/>), which is the relation the
/// equality of the two trace sequences is. The hand-written pairs are P1-034's: their only observable is the trace,
/// since each returns its first parameter and uses no call's result.
/// </summary>
public sealed partial class PositionalTraceTests
{
    private const string Header = "proc \"T::M(int,int)\" (%a \"a\": bv32, %b \"b\": bv32) -> bv32 entry B0\nB0:\n";

    private const string FThenG = Header + """
          call "S::F(int)"(%a)
          call "S::G(int)"(%b)
          ret %a
        """;

    private const string FOnce = Header + """
          call "S::F(int)"(%a)
          ret %a
        """;

    private const string FOfSum = Header + """
          %s: bv32 = add %a, %b
          call "S::F(int)"(%s)
          ret %a
        """;

    private const string FOfSumCommuted = Header + """
          %s: bv32 = add %b, %a
          call "S::F(int)"(%s)
          ret %a
        """;

    private const string FThenH = Header + """
          call "S::F(int)"(%a)
          call "S::H(int)"(%b)
          ret %a
        """;

    private const string FOfBThenG = Header + """
          call "S::F(int)"(%b)
          call "S::G(int)"(%b)
          ret %a
        """;

    private const string FTwice = Header + """
          call "S::F(int)"(%a)
          call "S::F(int)"(%a)
          ret %a
        """;

    private const string NoCall = Header + """
          ret %a
        """;

    private const string FWhenLess = Header + """
          %c: bool = slt %a, %b
          br %c, B1, B2
        B1:
          call "S::F(int)"(%a)
          goto B2
        B2:
          ret %a
        """;

    private const string GThenF = Header + """
          call "S::G(int)"(%b)
          call "S::F(int)"(%a)
          ret %a
        """;

    private const string FOnEitherArmThenG = Header + """
          %c: bool = slt %a, %b
          br %c, B1, B2
        B1:
          call "S::F(int)"(%a)
          goto B3
        B2:
          call "S::F(int)"(%a)
          goto B3
        B3:
          call "S::G(int)"(%b)
          ret %a
        """;

    private const string FWhenLessThenG = Header + """
          %c: bool = slt %a, %b
          br %c, B1, B2
        B1:
          call "S::F(int)"(%a)
          goto B2
        B2:
          call "S::G(int)"(%b)
          ret %a
        """;

    private const string FWhenGreaterSwappedThenG = Header + """
          %c: bool = sgt %b, %a
          br %c, B1, B2
        B1:
          call "S::F(int)"(%a)
          goto B2
        B2:
          call "S::G(int)"(%b)
          ret %a
        """;

    /// <summary>The callee of <see cref="FOnce"/> given a Bool: one identity, another type of argument.</summary>
    private const string FOfABool = Header + """
          %c: bool = slt %a, %b
          call "S::F(int)"(%c)
          ret %a
        """;

    private const string FOfBThenFOfA = Header + """
          call "S::F(int)"(%b)
          call "S::F(int)"(%a)
          ret %a
        """;

    private const string GOfAThenF = Header + """
          call "S::G(int)"(%a)
          call "S::F(int)"(%a)
          ret %a
        """;

    private const string FThenThrowsWhenLess = Header + """
          call "S::F(int)"(%a)
          %c: bool = slt %a, %b
          br %c, B1, B2
        B1:
          throw "System.ArgumentException"
        B2:
          ret %a
        """;

    private const string FThenThrowsAnotherWhenLess = Header + """
          call "S::F(int)"(%a)
          %c: bool = slt %a, %b
          br %c, B1, B2
        B1:
          throw "System.InvalidOperationException"
        B2:
          ret %a
        """;

    /// <summary>A path that ends in a throw after one call, one that ends in a return after two, and one cut off after none.</summary>
    private const string ThreeEnds = Header + """
          %c: bool = slt %a, %b
          br %c, B1, B2
        B1:
          call "S::F(int)"(%a)
          throw "System.Exception"
        B2:
          %d: bool = sgt %a, %b
          br %d, B3, B4
        B3:
          call "S::F(int)"(%a)
          call "S::G(int)"(%b)
          ret %a
        B4:
          unreachable
        """;

    /// <summary><see cref="SecondSolverLadderTests"/>' factoring pair, which a starved Z3 gives up on, with a call on each side.</summary>
    private const string FactoringWithACall = """
        proc "T::F(uint,uint)" (%a: bv32, %b: bv32) -> bv32 entry B0
        B0:
          %r: bv32 = call "S::F(int)"(%a)
          %p: bv32 = mul %r, %b
          ret %p
        ---
        proc "T::F(uint,uint)" (%a: bv32, %b: bv32) -> bv32 entry B0
        B0:
          %r: bv32 = call "S::F(int)"(%a)
          %p: bv32 = mul %r, %b
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

    private static readonly VerificationOptions Options = new(3, 10_000, []);

    /// <summary>The spike's nine pairs, and one per way two events differ or agree that they leave out.</summary>
    [Theory]
    [InlineData(FOfSum, FOfSumCommuted, Status.UNSATISFIABLE)]
    [InlineData(FThenG, FThenH, Status.SATISFIABLE)]
    [InlineData(FThenG, FOfBThenG, Status.SATISFIABLE)]
    [InlineData(FTwice, FOnce, Status.SATISFIABLE)]
    [InlineData(FWhenLess, NoCall, Status.SATISFIABLE)]
    [InlineData(FThenG, GThenF, Status.SATISFIABLE)]
    [InlineData(FOnEitherArmThenG, FThenG, Status.UNSATISFIABLE)]
    [InlineData(FWhenLessThenG, FWhenGreaterSwappedThenG, Status.UNSATISFIABLE)]
    [InlineData(FWhenLessThenG, FThenG, Status.SATISFIABLE)]
    [InlineData(FOnce, FOfABool, Status.SATISFIABLE)]
    [InlineData(FOfABool, FOfABool, Status.UNSATISFIABLE)]
    [InlineData(FThenThrowsWhenLess, FThenThrowsWhenLess, Status.UNSATISFIABLE)]
    [InlineData(FThenThrowsWhenLess, FThenThrowsAnotherWhenLess, Status.SATISFIABLE)]
    [InlineData(ThreeEnds, ThreeEnds, Status.UNSATISFIABLE)]
    public void BothComparisonsAnswerTheDivergenceQueryAsExpected(string old, string @new, Status expected)
    {
        (IrProcedure Old, IrProcedure New) pair = (IrText.Parse(old), IrText.Parse(@new));

        Assert.Equal(expected, Divergence(pair, TraceComparison.Sequence));
        Assert.Equal(expected, Divergence(pair, TraceComparison.Positional));
    }

    /// <summary>
    /// Criterion 2: over generated acyclic pairs that call, a procedure with itself or with a mutant, the
    /// <c>divergence</c> query has one answer under both comparisons. A query Z3 gives up on has none to compare.
    /// </summary>
    [Fact]
    public void BothComparisonsAnswerTheDivergenceQueryTheSameOnGeneratedPairs()
    {
        Gen<(IrProcedure Old, IrProcedure New)> pairs = Gen.Frequency(
            (1, IrGen.AcyclicProcedure.Select(static p => (p, p))),
            (3, IrGen.AcyclicProcedure.SelectMany(IrGen.Mutation).Where(static m => m is not null).Select(static m => (m!.Original, m.Mutant))));
        pairs.Where(static pair => Calls(pair.Old) || Calls(pair.New)).Sample(
            static pair =>
            {
                (IrProcedure old, IrProcedure @new, _) = ProductEncoder.ShareFragments(pair.Old, pair.New);
                Status sequence = Divergence((old, @new), TraceComparison.Sequence);
                Status positional = Divergence((old, @new), TraceComparison.Positional);
                Assert.True(sequence == positional || sequence == Status.UNKNOWN || positional == Status.UNKNOWN, $"sequence {sequence}, positional {positional}");
            },
            iter: 200,
            print: static pair => $"{IrText.Dump(pair.Old)}\n{IrText.Dump(pair.New)}");

        static bool Calls(IrProcedure procedure) => procedure.Blocks.Any(static b => b.Instructions.Any(static i => i is IrCall or IrOpaque { Fingerprint: not null }));
    }

    /// <summary>Criterion 1, on the product: the positional <c>divergence</c> query holds bit-vectors and functions only.</summary>
    [Fact]
    public void ThePositionalDivergenceQueryHoldsNoSequenceDatatypeOrInteger()
    {
        (IrProcedure Old, IrProcedure New) pair = (IrText.Parse(FThenThrowsWhenLess), IrText.Parse(FThenThrowsAnotherWhenLess));

        Assert.Equal("QF_BVDTSLIA", Logic(DivergenceScript(pair, TraceComparison.Sequence)));
        Assert.Equal("QF_BV", Logic(DivergenceScript(pair, TraceComparison.Positional)));
    }

    /// <summary>Criterion 1, on the ladder: the query rung 1 hands a second solver when Z3 gives up is the positional one.</summary>
    [Fact]
    public void RungOneAsksThePositionalQuery()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(FactoringWithACall);
        ScriptedSolver solver = new(static _ => new SmtUnknown("resourceout"));

        Verdict verdict = new Z3Backend().Verify(old, @new, new VerificationOptions(3, 600_000, []) { ResourceLimit = 10_000, Solver = solver });

        Assert.Equal(UnknownReason.Timeout, Assert.IsType<Unknown>(verdict).Reason);
        string script = Assert.Single(solver.Scripts);
        Assert.Contains("f:S::F(int)", script, StringComparison.Ordinal);
        Assert.Equal("QF_UFBV", Logic(script));
    }

    /// <summary>Each site's least and greatest position over the paths of the control-flow graph, and what its event holds.</summary>
    [Fact]
    public void ACallSiteKnowsTheRangeOfItsPosition()
    {
        using Context context = new();
        (_, FragmentEncoder straight, FragmentEncoder branching) = Sides(context, IrText.Parse(FThenG), IrText.Parse(ThreeEnds));

        Assert.Equal([(0, 0), (1, 1)], straight.CallSites.Select(static s => (s.MinPosition, s.MaxPosition)));
        Assert.Equal([(0, 0), (1, 1), (0, 0)], branching.CallSites.Select(static s => (s.MinPosition, s.MaxPosition)));
        Assert.All(straight.CallSites, static s => Assert.Equal(s.Args, s.Event));
    }

    /// <summary>A call after a branch that calls on one arm stands at either of two positions.</summary>
    [Fact]
    public void ACallAfterAOneArmedCallHasARangeOfPositions()
    {
        using Context context = new();
        (_, FragmentEncoder oneArm, FragmentEncoder eitherArm) = Sides(context, IrText.Parse(FWhenLessThenG), IrText.Parse(FOnEitherArmThenG));

        Assert.Equal([(0, 0), (0, 1)], oneArm.CallSites.Select(static s => (s.MinPosition, s.MaxPosition)));
        Assert.Equal([(0, 0), (0, 0), (1, 1)], eitherArm.CallSites.Select(static s => (s.MinPosition, s.MaxPosition)));
    }

    /// <summary>The length is the count out of whichever end the path reaches: a throw, a return or an <c>unreachable</c>.</summary>
    [Fact]
    public void TheLengthIsTheCountAtTheEndReached()
    {
        using Context context = new();
        (_, FragmentEncoder straight, FragmentEncoder branching) = Sides(context, IrText.Parse(FThenG), IrText.Parse(ThreeEnds));

        Assert.Equal("(bvadd old.cnt.B0 #x00000002)", straight.Length!.ToString());
        Assert.Equal(
            "(ite new.reach.B4 (bvadd new.cnt.B4 #x00000000) (ite new.reach.B3 (bvadd new.cnt.B3 #x00000002) (bvadd new.cnt.B1 #x00000001)))",
            Flat(branching.Length!));
    }

    /// <summary>The exception type as bits is the integer one with the same ids, and 0 when no throw is reached.</summary>
    [Fact]
    public void TheExceptionTypeAsBitsHasTheIntegerIds()
    {
        using Context context = new();
        (_, FragmentEncoder one, FragmentEncoder other) = Sides(context, IrText.Parse(FThenThrowsWhenLess), IrText.Parse(FThenThrowsAnotherWhenLess));

        Assert.Equal("(ite old.reach.B1 1 0)", one.ExceptionType.ToString());
        Assert.Equal("(ite old.reach.B1 #x00000001 #x00000000)", one.ExceptionBits.ToString());
        Assert.Equal("(ite new.reach.B1 2 0)", other.ExceptionType.ToString());
        Assert.Equal("(ite new.reach.B1 #x00000002 #x00000000)", other.ExceptionBits.ToString());
    }

    /// <summary>
    /// Sites at fixed positions: only the two pairs that share a position are compared, without their positions, and a
    /// pair whose values are one term adds nothing.
    /// </summary>
    [Fact]
    public void SitesAtFixedPositionsAreComparedPositionByPosition()
    {
        Assert.Equal(
            "(and (= (bvadd old.cnt.B0 #x00000002) (bvadd new.cnt.B0 #x00000002)) (=> (and old.reach.B0 new.reach.B0) (and (= in.a in.b))))",
            Equal(FThenG, FOfBThenG));
        Assert.Equal(
            "(and (= (bvadd old.cnt.B0 #x00000002) (bvadd new.cnt.B0 #x00000002)) (=> (and old.reach.B0 new.reach.B0) (and (= in.a in.b))) (=> (and old.reach.B0 new.reach.B0) (and (= in.b in.a))))",
            Equal(FThenG.Replace("S::G", "S::F", StringComparison.Ordinal), FOfBThenFOfA));
    }

    /// <summary>Two sites of different callees, or of one callee with other types, are never both made at one position.</summary>
    [Fact]
    public void SitesOfDifferentShapesNeverMeet()
    {
        Assert.Equal(
            "(and (= (bvadd old.cnt.B0 #x00000002) (bvadd new.cnt.B0 #x00000002)) (not (and old.reach.B0 new.reach.B0)) (not (and old.reach.B0 new.reach.B0)))",
            Equal(FThenG, GThenF));
        Assert.Equal(
            "(and (= (bvadd old.cnt.B0 #x00000001) (bvadd new.cnt.B0 #x00000001)) (not (and old.reach.B0 new.reach.B0)))",
            Equal(FOnce, FOfABool));
    }

    /// <summary>A site whose position depends on the path is compared with each site it can meet, where their positions are equal.</summary>
    [Fact]
    public void SitesAtARangeOfPositionsAreComparedWhereTheirPositionsAreEqual()
    {
        Assert.Equal(
            [
                "(= (bvadd old.cnt.B2 #x00000001) (bvadd new.cnt.B0 #x00000002))",
                "(not (and old.reach.B1 new.reach.B0))",
                "(=> (and old.reach.B2 new.reach.B0 (= (bvadd old.cnt.B2 #x00000000) (bvadd new.cnt.B0 #x00000000))) (and (= in.b in.a)))",
                "(not (and old.reach.B2 new.reach.B0 (= (bvadd old.cnt.B2 #x00000000) (bvadd new.cnt.B0 #x00000001))))",
            ],
            Conjuncts(FWhenLessThenG, GOfAThenF));
    }

    /// <summary>The cap is on the pairs compared: at it the comparison is built, one past it it is not.</summary>
    [Fact]
    public void MorePairsThanTheCapAreNotCompared()
    {
        using Context context = new();
        (TraceEncoder calls, FragmentEncoder old, FragmentEncoder @new) = Sides(context, IrText.Parse(FThenG), IrText.Parse(GThenF));

        Assert.NotNull(PositionalTrace.Equal(context, calls, old, @new, maxPairs: 2));
        Assert.Null(PositionalTrace.Equal(context, calls, old, @new, maxPairs: 1));
    }

    /// <summary>
    /// A product keeps the sequence comparison, the very term, when it has more pairs of sites than
    /// <see cref="PositionalTrace.MaxPairs"/>: <paramref name="branches"/> calls a side, each under a branch of its own, can
    /// each stand at position 0, so every old one can meet every new one.
    /// </summary>
    [Theory]
    [InlineData(100, false)]
    [InlineData(101, true)]
    public void AProductAboveTheCapKeepsTheSequenceComparison(int branches, bool sequence)
    {
        Assert.Equal(PositionalTrace.MaxPairs, 100 * 100);
        IrProcedure procedure = IrText.Parse(Branches(branches));
        using Context context = new();

        ProductEncoding asSequence = ProductEncoder.Encode(context, procedure, procedure, []);
        ProductEncoding positional = ProductEncoder.Encode(context, procedure, procedure, [], traces: TraceComparison.Positional);

        Assert.Equal(sequence, asSequence.Differs.Equals(positional.Differs));
    }

    /// <summary><paramref name="count"/> calls of <c>S::F</c>, each made when its own bit of <c>a</c> is set.</summary>
    private static string Branches(int count)
    {
        StringBuilder text = new(Header);
        for (int i = 0; i < count; i++)
        {
            string n = i.ToString(CultureInfo.InvariantCulture);
            string made = (2 * i + 1).ToString(CultureInfo.InvariantCulture);
            string next = (2 * i + 2).ToString(CultureInfo.InvariantCulture);
            text.Append(CultureInfo.InvariantCulture, $"  %k{n}: bv32 = const bv32 {n}\n  %c{n}: bool = sgt %a, %k{n}\n  br %c{n}, B{made}, B{next}\nB{made}:\n  call \"S::F(int)\"(%b)\n  goto B{next}\nB{next}:\n");
        }

        return text.Append("  ret %a\n").ToString();
    }

    /// <summary>Z3's answer to rung 1's <c>divergence</c> query on the pair's product under <paramref name="traces"/>.</summary>
    private static Status Divergence((IrProcedure Old, IrProcedure New) pair, TraceComparison traces)
    {
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, pair.Old, pair.New, [], traces: traces);
        using SolverQuery query = Z3Backend.Query(context, encoding, Options, DivergenceQuery(context, encoding));
        return query.Check(Options, "divergence");
    }

    /// <summary>The <c>divergence</c> query as a second solver is sent it.</summary>
    private static string DivergenceScript((IrProcedure Old, IrProcedure New) pair, TraceComparison traces)
    {
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, pair.Old, pair.New, [], traces: traces);
        return SecondSolver.Print(context, encoding.Assertions, DivergenceQuery(context, encoding))!.Script;
    }

    private static BoolExpr[] DivergenceQuery(Context context, ProductEncoding encoding) =>
        [encoding.Differs, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew), context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];

    private static string Logic(string script) => SetLogic.Match(script).Groups["logic"].Value;

    /// <summary>The positional comparison of a pair, on one line.</summary>
    private static string Equal(string old, string @new)
    {
        using Context context = new();
        (TraceEncoder calls, FragmentEncoder oldSide, FragmentEncoder newSide) = Sides(context, IrText.Parse(old), IrText.Parse(@new));
        return Flat(PositionalTrace.Equal(context, calls, oldSide, newSide)!);
    }

    /// <summary>The conjuncts of the positional comparison of a pair, each on one line.</summary>
    private static string[] Conjuncts(string old, string @new)
    {
        using Context context = new();
        (TraceEncoder calls, FragmentEncoder oldSide, FragmentEncoder newSide) = Sides(context, IrText.Parse(old), IrText.Parse(@new));
        return [.. PositionalTrace.Equal(context, calls, oldSide, newSide)!.Args.Select(Flat)];
    }

    private static string Flat(Expr term) => Whitespace.Replace(term.ToString(), " ");

    /// <summary>
    /// The two sides of a pair's product, built over the encoders of <see cref="ProductEncoder.Encode"/>'s own, which name
    /// every term as its two did.
    /// </summary>
    private static (TraceEncoder Calls, FragmentEncoder Old, FragmentEncoder New) Sides(Context context, IrProcedure old, IrProcedure @new)
    {
        ProductEncoding encoding = ProductEncoder.Encode(context, old, @new, []);
        Dictionary<string, int> exceptionTypes = new(StringComparer.Ordinal);
        return (encoding.Calls, Encoder(Side.Old, old, static s => s.Old), Encoder(Side.New, @new, static s => s.New));

        FragmentEncoder Encoder(Side side, IrProcedure procedure, Func<SharedParameter, IrParameter?> parameter) => new(
            side,
            procedure,
            encoding.Sorts,
            (encoding.Calls, encoding.Pures),
            encoding.Inputs.Where(i => parameter(i.Shared) is not null).ToDictionary(i => parameter(i.Shared)!.Var.Name, static i => i.Term, StringComparer.Ordinal),
            [],
            exceptionTypes);
    }

    [GeneratedRegex(@"\(set-logic (?<logic>\S+)\)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 60_000)]
    private static partial Regex SetLogic { get; }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 60_000)]
    private static partial Regex Whitespace { get; }
}
