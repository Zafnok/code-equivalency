using System.Collections.Immutable;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.TestSupport;

using Microsoft.Z3;

using Xunit;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Ticket P1-033: what <see cref="SecondSolver"/> sends (ADR 0050 decision 2). The script is the query as Z3 prints
/// it, after two rewrites that must leave its meaning as it is; the properties check that by asking Z3 both.
/// </summary>
public sealed class SecondSolverPrintTests
{
    [Theory]
    [InlineData("(seq.++ x)", "x")]
    [InlineData("(seq.++ (seq.++ x))", "x")]
    [InlineData("(seq.++\n   (seq.unit a))", "(seq.unit a)")]
    [InlineData("(f (seq.++ |a b|) c)", "(f |a b| c)")]
    [InlineData("(seq.++ x y)", "(seq.++ x y)")]
    [InlineData("(seq.++ (seq.++ x) (seq.++ y z))", "(seq.++ x (seq.++ y z))")]
    [InlineData("(seq.++ (f x y))", "(f x y)")]
    [InlineData("(seq.++x y)", "(seq.++x y)")]
    [InlineData("(f |(seq.++ x)| (seq.++ |(|))", "(f |(seq.++ x)| |(|)")]
    [InlineData("(f \"(seq.++ x)\" (seq.++ \")\"))", "(f \"(seq.++ x)\" \")\")")]
    [InlineData("(assert (= t (seq.++ (as seq.empty (Seq E)))))\n(check-sat)\n", "(assert (= t (as seq.empty (Seq E))))\n(check-sat)\n")]
    [InlineData("x (seq.++ y)", "x y")]
    [InlineData("", "")]
    public void AConcatenationOfOneSequenceIsThatSequence(string printed, string expected)
    {
        Assert.Equal(expected, SecondSolver.UnwrapUnaryConcat(printed));
    }

    [Fact]
    public void Print_IsTheQueryAsZ3PrintsItWithAGetValueOverItsScalars()
    {
        using Context context = new();
        BoolExpr flag = context.MkBoolConst("flag");
        BitVecExpr wide = context.MkBVConst("in.a b", 8);
        IntExpr count = context.MkIntConst("count");

        SecondSolver.Printed printed = Assert.IsType<SecondSolver.Printed>(
            SecondSolver.Print(context, [flag, context.MkEq(wide, context.MkBV(5, 8))], [context.MkGt(count, context.MkInt(0))]));

        Assert.Equal(
            """
            (set-option :produce-models true)
            (set-logic QF_BVLIA)
            (declare-fun flag () Bool)
            (declare-fun |in.a b| () (_ BitVec 8))
            (declare-fun count () Int)
            (assert flag)
            (assert (= |in.a b| #x05))
            (assert (> count 0))

            (check-sat)
            (get-value (flag |in.a b|))

            """,
            printed.Script,
            ignoreLineEndingDifferences: true);
        Assert.Equal([("flag", "Bool"), ("|in.a b|", "(_ BitVec 8)")], printed.Scalars);
    }

    [Fact]
    public void Print_WithNoScalarAsksForNoValue()
    {
        using Context context = new();
        IntExpr count = context.MkIntConst("count");

        SecondSolver.Printed printed = Assert.IsType<SecondSolver.Printed>(SecondSolver.Print(context, [], [context.MkGt(count, context.MkInt(0))]));

        Assert.EndsWith("(check-sat)\n", printed.Script, StringComparison.Ordinal);
        Assert.Empty(printed.Scalars);
        Assert.Equal(string.Empty, printed.Pins([]));
    }

    public static TheoryData<string, string> Logics => new()
    {
        { "bool", "QF_UF" },
        { "bitvector", "QF_BV" },
        { "integer", "QF_LIA" },
        { "array", "QF_ABV" },
        { "function", "QF_UFBV" },
        { "sort", "QF_UF" },
        { "datatype", "QF_DT" },
        { "sequence", "QF_BVS" },
    };

    /// <summary>The logic is named from what the printed text holds, in the order SMT-LIB names its theories.</summary>
    [Theory]
    [MemberData(nameof(Logics))]
    public void Print_NamesTheLogicFromTheTheoriesInTheText(string theory, string logic)
    {
        using Context context = new();
        BitVecSort bits = context.MkBitVecSort(8);
        BoolExpr term = theory switch
        {
            "bool" => context.MkBoolConst("p"),
            "bitvector" => context.MkEq(context.MkBVConst("x", 8), context.MkBV(1, 8)),
            "integer" => context.MkGt(context.MkIntConst("n"), context.MkInt(0)),
            "array" => context.MkEq(context.MkSelect(context.MkArrayConst("h", bits, bits), context.MkBV(0, 8)), context.MkBV(1, 8)),
            "function" => context.MkEq(context.MkApp(context.MkFuncDecl("f", [bits], bits), context.MkBV(0, 8)), context.MkBV(1, 8)),
            "sort" => context.MkEq(context.MkConst("u", context.MkUninterpretedSort("U")), context.MkConst("v", context.MkUninterpretedSort("U"))),
            "datatype" => Boxed(context),
            _ => context.MkEq(context.MkConst("s", context.MkSeqSort(bits)), context.MkEmptySeq(context.MkSeqSort(bits))),
        };

        SecondSolver.Printed printed = Assert.IsType<SecondSolver.Printed>(SecondSolver.Print(context, [term], []));

        Assert.Contains($"(set-logic {logic})\n", printed.Script, StringComparison.Ordinal);

        static BoolExpr Boxed(Context context)
        {
            using Constructor box = context.MkConstructor("box", "is-box", ["unbox"], [context.BoolSort]);
            DatatypeSort boxed = context.MkDatatypeSort("Box", [box]);
            return context.MkEq(context.MkConst("b", boxed), context.MkConst("c", boxed));
        }
    }

    [Fact]
    public void Pins_DeclareEachConstantAndFixItToItsValue()
    {
        SecondSolver.Printed printed = new("(check-sat)", [("flag", "Bool"), ("|in.a b|", "(_ BitVec 8)"), ("n", "(_ BitVec 3)")]);
        ImmutableDictionary<string, string> values = ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            [KeyValuePair.Create("flag", "true"), KeyValuePair.Create("|in.a b|", "#xAf"), KeyValuePair.Create("n", "#b101"), KeyValuePair.Create("unasked", "junk")]);

        Assert.Equal(
            "(declare-fun flag () Bool)\n(assert (= flag true))\n(declare-fun |in.a b| () (_ BitVec 8))\n(assert (= |in.a b| #xAf))\n(declare-fun n () (_ BitVec 3))\n(assert (= n #b101))\n",
            printed.Pins(values));
        Assert.NotNull(printed.Pins(values.SetItem("flag", "false")));
        Assert.Null(printed.Pins(values.SetItem("flag", "#b1")));
        Assert.Null(printed.Pins(values.SetItem("n", "#x5")));
        Assert.Null(printed.Pins(values.SetItem("n", "#b1010")));
        Assert.Null(printed.Pins(values.SetItem("n", "#b10")));
        Assert.Null(printed.Pins(values.SetItem("n", "#b1x1")));
        Assert.Null(printed.Pins(values.SetItem("|in.a b|", "#xfg")));
        Assert.Null(printed.Pins(values.SetItem("|in.a b|", "#b0000111")));
        Assert.Null(printed.Pins(values.SetItem("|in.a b|", "xAf")));
        Assert.Null(printed.Pins(values.SetItem("|in.a b|", "(_ bv5 8)")));
        Assert.Null(printed.Pins(values.SetItem("|in.a b|", string.Empty)));
        Assert.Null(printed.Pins(values.Remove("n")));
    }

    [Fact]
    public void TermsWithNoSymbolicConstantArray_AreSentAsTheyAre()
    {
        using Context context = new();
        BitVecSort bits = context.MkBitVecSort(8);
        ArrayExpr zeros = context.MkConstArray(bits, context.MkBV(0, 8));
        ArrayExpr falses = context.MkConstArray(bits, context.MkFalse());
        ArrayExpr trues = context.MkConstArray(bits, context.MkTrue());
        ArrayExpr nested = context.MkConstArray(bits, zeros);
        BoolExpr[] terms =
        [
            context.MkEq(context.MkSelect(zeros, context.MkBVConst("i", 8)), context.MkBVConst("v", 8)),
            (BoolExpr)context.MkSelect(falses, context.MkBVConst("i", 8)),
            (BoolExpr)context.MkSelect(trues, context.MkBVConst("i", 8)),
            context.MkEq(context.MkSelect((ArrayExpr)context.MkSelect(nested, context.MkBVConst("i", 8)), context.MkBVConst("j", 8)), context.MkBVConst("v", 8)),
        ];

        Assert.Same(terms, SecondSolver.WithoutSymbolicConstantArrays(context, terms));
        Assert.Empty(Assert.IsType<BoolExpr[]>(SecondSolver.WithoutSymbolicConstantArrays(context, [])));
    }

    /// <summary>
    /// An array built on a constant array by <c>store</c>, <c>ite</c> and a definition, and read: the constant array
    /// becomes a fresh constant, constrained at each index read from anything built on it.
    /// </summary>
    [Fact]
    public void ASymbolicConstantArray_BecomesAFreshArrayConstrainedWhereItIsRead()
    {
        using Context context = new();
        BitVecSort bits = context.MkBitVecSort(8);
        BitVecExpr d = context.MkBVConst("d", 8);
        BitVecExpr i = context.MkBVConst("i", 8);
        BitVecExpr j = context.MkBVConst("j", 8);
        ArrayExpr constant = context.MkConstArray(bits, d);
        ArrayExpr named = context.MkArrayConst("h", bits, bits);
        BoolExpr[] terms =
        [
            context.MkEq(named, context.MkStore(constant, i, context.MkBV(7, 8))),
            context.MkNot(context.MkEq(i, j)),
            context.MkNot(context.MkEq(context.MkSelect(named, j), d)),
        ];

        BoolExpr rewritten = Assert.Single(Assert.IsType<BoolExpr[]>(SecondSolver.WithoutSymbolicConstantArrays(context, terms)));

        // Unsatisfiable: away from the stored index the array holds its default. An unconstrained array would satisfy it.
        Assert.Equal(Status.UNSATISFIABLE, Check(context, terms));
        Assert.Equal(Status.UNSATISFIABLE, Check(context, rewritten));
        string text = rewritten.ToString();
        Assert.DoesNotContain("as const", text, StringComparison.Ordinal);
        Assert.Contains("(= (select const-array!", text, StringComparison.Ordinal);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(text, @"\(= \(select const-array!\d+ j\) d\)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(60)));
    }

    public static TheoryData<string> OpaqueUses => ["function", "equality", "nested-equality", "two-definitions", "defines-a-term", "element", "key", "index", "distinct", "quantifier"];

    /// <summary>A use that can see the whole of an array built on a symbolic constant array: the read set cannot be computed, and the query is not sent.</summary>
    [Theory]
    [MemberData(nameof(OpaqueUses))]
    public void AnArrayBuiltOnASymbolicConstantArrayUsedAsAWhole_IsNotSent(string use)
    {
        using Context context = new();
        BitVecSort bits = context.MkBitVecSort(8);
        ArraySort arrays = context.MkArraySort(bits, bits);
        BitVecExpr d = context.MkBVConst("d", 8);
        BitVecExpr i = context.MkBVConst("i", 8);
        ArrayExpr constant = context.MkConstArray(bits, d);
        ArrayExpr stored = context.MkStore(constant, i, context.MkBV(7, 8));
        ArrayExpr h = context.MkArrayConst("h", bits, bits);
        ArrayExpr g = context.MkArrayConst("g", bits, bits);
        BoolExpr[] terms = use switch
        {
            "function" => [context.MkEq(context.MkApp(context.MkFuncDecl("f", [arrays], bits), stored), d)],
            "equality" => [context.MkEq(stored, h)],
            "nested-equality" => [context.MkOr(context.MkEq(h, stored), context.MkBoolConst("p"))],
            "two-definitions" => [context.MkEq(h, stored), context.MkEq(h, g)],
            "defines-a-term" => [context.MkEq(context.MkStore(g, i, d), constant)],
            "element" => [context.MkEq(context.MkSelect((ArrayExpr)context.MkSelect(context.MkStore(context.MkArrayConst("hh", bits, arrays), i, stored), i), i), d)],
            "key" => [context.MkEq(context.MkSelect(context.MkArrayConst("by", arrays, bits), constant), d)],
            "index" => [context.MkEq(context.MkSelect(context.MkStore(context.MkArrayConst("to", arrays, bits), constant, d), g), d)],
            "distinct" => [context.MkDistinct(stored, h)],
            _ => [context.MkForall([i], context.MkEq(context.MkSelect(constant, i), d))],
        };

        Assert.Null(SecondSolver.WithoutSymbolicConstantArrays(context, terms));
        Assert.Null(SecondSolver.Print(context, terms, []));
        Assert.Null(SecondSolver.Print(context, [], terms));
    }

    [Fact]
    public void AQueryThatIsNotPrinted_IsNotSentAndIsUnknown()
    {
        ScriptedSolver solver = new(static _ => new SmtUnsat());

        SmtUnknown unknown = Assert.IsType<SmtUnknown>(SecondSolver.Ask(solver, printed: null, timeoutMs: 1_000));

        Assert.Equal("not sent: a constant array cannot be rewritten exactly", unknown.Reason);
        Assert.Empty(solver.Scripts);
        Assert.IsType<SmtUnsat>(SecondSolver.Ask(solver, new SecondSolver.Printed("(check-sat)", []), timeoutMs: 1_000));
        Assert.Equal(["(check-sat)"], solver.Scripts);
        Assert.Equal([TimeSpan.FromSeconds(1)], solver.Limits);
        ScriptedSolver throwing = new(static _ => throw new InvalidOperationException("gone"));
        Assert.Equal("gone", Assert.IsType<SmtUnknown>(SecondSolver.Ask(throwing, new SecondSolver.Printed("(check-sat)", []), timeoutMs: 1_000)).Reason);
    }

    [Fact]
    public void AQuantifiedTermWithNoConstantArray_IsNotSentEither()
    {
        using Context context = new();
        BitVecExpr i = context.MkBVConst("i", 8);

        Assert.Null(SecondSolver.WithoutSymbolicConstantArrays(context, [context.MkForall([i], context.MkEq(i, i))]));
    }

    /// <summary>
    /// The second rewrite is exact (the ticket's first pitfall): over generated terms that build arrays on symbolic
    /// constant arrays by <c>store</c>, <c>ite</c> and definitions, and read them, Z3 answers the rewritten terms, and
    /// the script printed from them, as it answers the originals.
    /// </summary>
    [Fact]
    public void TheConstantArrayRewriteKeepsTheAnswer()
    {
        int satisfiable = 0;
        int unsatisfiable = 0;
        Gen.Int[0, 999].Array[48].Sample(
            choices =>
            {
                using Context context = new();
                BoolExpr[] terms = ArrayTerms(context, choices);
                Status original = Check(context, terms);

                BoolExpr[] rewritten = Assert.IsType<BoolExpr[]>(SecondSolver.WithoutSymbolicConstantArrays(context, terms));
                SecondSolver.Printed printed = Assert.IsType<SecondSolver.Printed>(SecondSolver.Print(context, terms, []));

                Assert.NotEqual(Status.UNKNOWN, original);
                Assert.Equal(original, Check(context, rewritten));
                Assert.Equal(original == Status.SATISFIABLE, ScriptedSolver.Solve(printed.Script) is SmtSat);
                Assert.DoesNotContain("as const", printed.Script, StringComparison.Ordinal);
                _ = original == Status.SATISFIABLE ? Interlocked.Increment(ref satisfiable) : Interlocked.Increment(ref unsatisfiable);
            },
            iter: 400);

        // The generator must exercise both answers, or the property says little.
        Assert.InRange(satisfiable, 40, 400);
        Assert.InRange(unsatisfiable, 40, 400);
    }

    /// <summary>
    /// The ticket's property: on generated pairs, Z3 answers the text sent for rung 1's divergence query as it answers
    /// the query itself, and the values of a satisfiable answer read back to a model of the query.
    /// </summary>
    [Fact]
    public void Z3AnswersTheRewrittenTextAsItAnswersTheOriginal()
    {
        int sent = 0;
        Gen.OneOf(
            IrGen.AcyclicProcedure.Select(static p => (Old: p, New: p)),
            IrGen.AcyclicProcedure.SelectMany(IrGen.Mutation).Where(static m => m is not null).Select(static m => (Old: m!.Original, New: m.Mutant))).Sample(
            pair =>
            {
                using Context context = new();
                ProductEncoding encoding = ProductEncoder.Encode(context, pair.Old, pair.New, []);
                BoolExpr[] query = [encoding.Differs, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew), context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];
                Status original = Check(context, [.. encoding.Assertions, .. query]);
                if (original == Status.UNKNOWN || SecondSolver.Print(context, encoding.Assertions, query) is not { } printed)
                {
                    return;
                }

                Interlocked.Increment(ref sent);
                SmtAnswer answer = ScriptedSolver.Solve(printed.Script);
                Assert.Equal(original == Status.SATISFIABLE, answer is SmtSat);
                Assert.Equal(original == Status.UNSATISFIABLE, answer is SmtUnsat);
                if (answer is SmtSat sat)
                {
                    string pins = Assert.IsType<string>(printed.Pins(sat.Values));
                    Assert.Equal(Status.SATISFIABLE, Check(context, [.. encoding.Assertions, .. query, .. context.ParseSMTLIB2String(pins)]));
                }
            },
            iter: 200,
            print: static pair => IrText.Dump(pair.Old) + "\n" + IrText.Dump(pair.New));

        Assert.InRange(sent, 100, 200);
    }

    private static Status Check(Context context, params BoolExpr[] terms)
    {
        using Solver solver = ScriptedSolver.Pipeline(context);
        solver.Add(terms);
        return solver.Check();
    }

    /// <summary>
    /// A small conjunction over arrays from 2-bit keys to 2-bit values, steered by <paramref name="choices"/>: two
    /// constant arrays with symbolic defaults, an input array, stores, <c>ite</c>s and constants defined once by an
    /// asserted equality, with reads compared to values. Every use is one the rewrite follows.
    /// </summary>
    private static BoolExpr[] ArrayTerms(Context context, int[] choices)
    {
        int next = 0;
        BitVecSort bits = context.MkBitVecSort(2);
        BitVecExpr[] scalars = [context.MkBVConst("d", 2), context.MkBVConst("e", 2), context.MkBVConst("i", 2), context.MkBVConst("j", 2), context.MkBVConst("v", 2)];
        List<ArrayExpr> arrays = [context.MkConstArray(bits, scalars[0]), context.MkConstArray(bits, scalars[1]), context.MkArrayConst("in", bits, bits)];
        List<BoolExpr> terms = [];
        for (int step = 0; step < 8; step++)
        {
            int kind = Choose() % 3;
            ArrayExpr built = kind switch
            {
                0 => context.MkStore(Array(), Scalar(), Scalar()),
                1 => (ArrayExpr)context.MkITE(context.MkEq(Scalar(), Scalar()), Array(), Array()),
                _ => Defined(Array(), step),
            };
            arrays.Add(built);
            BoolExpr read = context.MkEq(context.MkSelect(Array(), Scalar()), Scalar());
            terms.Add(Choose() % 2 == 0 ? read : context.MkNot(read));
        }

        return [.. terms];

        int Choose() => choices[next++ % choices.Length];

        ArrayExpr Array() => arrays[Choose() % arrays.Count];

        BitVecExpr Scalar() => Choose() % 7 is var pick && pick < scalars.Length ? scalars[pick] : context.MkBV(pick - scalars.Length, 2);

        ArrayExpr Defined(ArrayExpr definition, int step)
        {
            ArrayExpr constant = context.MkArrayConst("c" + step.ToString(System.Globalization.CultureInfo.InvariantCulture), bits, bits);
            terms.Add(context.MkEq(constant, definition));
            return constant;
        }
    }
}
