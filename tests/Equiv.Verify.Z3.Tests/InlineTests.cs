using System.Globalization;
using System.Text;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.TestSupport;

using Microsoft.Z3;

using Xunit;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Ticket P2-076 criterion 2: <see cref="Z3Backend.Inline"/> and the disposal of a query's context are cheaper, and what
/// <c>Inline</c> gives is, term for term, what substituting every definition at every step gave. <see cref="Original"/> is
/// that substitution as it was before the ticket.
/// </summary>
public sealed class InlineTests
{
    /// <summary>How many calls each side of <see cref="StraightLine"/> makes.</summary>
    private const int Calls = 8000;

    /// <summary>
    /// The stages of one run of rung 1 on <see cref="StraightLine"/>, in seconds. Before the ticket <c>inline</c> took
    /// about a hundred times what <c>encode</c> does at this size, and <c>dispose</c> about a thousand times.
    /// </summary>
    private static readonly Lazy<Dictionary<string, double>> Stages = new(static () =>
    {
        RecordingRunLog log = new(isDebug: true);
        new Z3Backend().Verify(StraightLine(1), StraightLine(2), new VerificationOptions(3, 600_000, []) { Log = log });
        return log.Events
            .Select(static e => e.Split(' '))
            .Where(static parts => parts[1].StartsWith("stage=", StringComparison.Ordinal))
            .GroupBy(static parts => parts[1]["stage=".Length..], StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.Sum(static parts => double.Parse(parts[2]["took=".Length..^1], CultureInfo.InvariantCulture)), StringComparer.Ordinal);
    });

    public static TheoryData<string> Fixtures =>
    [
        .. FixtureTests.Names.Select(static row => row.Data),
        .. EncoderSnapshotTests.Kinds.Select(static row => "kinds/" + row.Data),
        .. LadderFixtureTests.Names.Select(static row => "loops/" + row.Data).Where(static name => !string.Equals(name, "loops/irreducible", StringComparison.Ordinal)),
    ];

    /// <summary>
    /// On every fixture rung 1 encodes, each term a query is built from (the product's own, every opaque's reach, every
    /// variable and every block's reach on both sides) inlines to the same Z3 term as before.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void InlineGivesTheTermsSubstitutingEveryDefinitionGave(string name)
    {
        Fixture fixture = Fixture.Load(name);
        (IrProcedure old, IrProcedure @new, _) = ProductEncoder.ShareFragments(fixture.Old, fixture.New);
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, IrUnroller.Unroll(old, 3), IrUnroller.Unroll(@new, 3), []);
        BoolExpr[] query =
        [
            encoding.Differs, encoding.OpaqueOld, encoding.OpaqueNew, encoding.Old.Unreachable, encoding.New.Unreachable,
            .. encoding.Opaques.Select(static o => o.Reach),
            .. encoding.Old.Reach.Values, .. encoding.New.Reach.Values,
            .. encoding.Old.Vars.Values.Concat(encoding.New.Vars.Values).Select(v => context.MkEq(v, v)),
        ];

        Assert.Equal(Original(context, encoding.Assertions, query), Z3Backend.Inline(context, encoding.Assertions, query));
    }

    /// <summary>
    /// A constant defined twice, a definition that uses a constant defined only later, a numeral, and a constant that is
    /// the whole query: each as before.
    /// </summary>
    [Fact]
    public void ANameDefinedAgainOrUsedBeforeItsDefinitionIsSubstitutedAsBefore()
    {
        using Context context = new();
        IntExpr a = context.MkIntConst("a");
        IntExpr b = context.MkIntConst("b");
        IntExpr c = context.MkIntConst("c");
        BoolExpr flag = context.MkBoolConst("flag");
        BoolExpr[] assertions =
        [
            context.MkEq(a, context.MkAdd(c, context.MkInt(1))),
            context.MkEq(b, context.MkMul(a, a)),
            flag,
            context.MkEq(a, context.MkInt(7)),
            context.MkEq(c, context.MkAdd(a, b)),
            context.MkLt(a, b),
            context.MkEq(context.MkAdd(a, b), c),
        ];
        BoolExpr[] query = [context.MkLt(c, context.MkAdd(a, b)), flag, context.MkAnd(flag, context.MkEq(b, context.MkInt(3))), context.MkBoolConst("free")];

        BoolExpr[] inlined = Z3Backend.Inline(context, assertions, query);

        Assert.Equal(Original(context, assertions, query), inlined);
        Assert.Equal(context.MkTrue(), inlined[1]);
        Assert.Equal(query[3], inlined[3]);
    }

    /// <summary>
    /// A definition under a quantifier is substituted by Z3's own rules, whichever side of it the traversal reaches first,
    /// and the definitions after it go on as before.
    /// </summary>
    [Fact]
    public void ADefinitionUnderAQuantifierIsSubstitutedAsBefore()
    {
        using Context context = new();
        IntExpr a = context.MkIntConst("a");
        IntExpr b = context.MkIntConst("b");
        BoolExpr p = context.MkBoolConst("p");
        BoolExpr q = context.MkBoolConst("q");
        IntExpr x = context.MkIntConst("x");
        BoolExpr forall = context.MkForall([x], context.MkLt(x, context.MkAdd(a, x)));
        BoolExpr[] assertions =
        [
            context.MkEq(a, context.MkInt(5)),
            context.MkEq(p, context.MkAnd(forall, context.MkLt(a, b))),
            context.MkEq(q, context.MkAnd(context.MkLt(a, b), forall)),
            context.MkEq(b, context.MkAdd(a, a)),
        ];
        BoolExpr[] query = [context.MkOr(p, q), forall, context.MkLt(b, a)];

        BoolExpr[] inlined = Z3Backend.Inline(context, assertions, query);

        Assert.Equal(Original(context, assertions, query), inlined);
        Assert.NotEqual(query[1], inlined[1]);
    }

    /// <summary>A term with forty levels of a shared subterm has two to the forty paths and forty-one terms; each is visited once.</summary>
    [Fact]
    public void ASharedSubtermIsVisitedOnce()
    {
        using Context context = new();
        IntExpr a = context.MkIntConst("a");
        ArithExpr shared = Enumerable.Range(0, 40).Aggregate((ArithExpr)a, (term, _) => context.MkAdd(term, term));
        BoolExpr[] assertions = [context.MkEq(a, context.MkInt(1))];
        BoolExpr[] query = [context.MkGt(shared, context.MkInt(0))];

        BoolExpr[] inlined = Z3Backend.Inline(context, assertions, query);

        Assert.Equal(Original(context, assertions, query), inlined);
    }

    /// <summary>
    /// The stage made cheaper: inlining a pair of <see cref="Calls"/> straight-line calls costs about what encoding it
    /// does, where substituting every definition at every step cost their number squared.
    /// </summary>
    [Fact]
    public void InlineCostGrowsWithTheDefinitionsAndNotTheirSquare() =>
        Assert.InRange(Stages.Value["inline"], 0, (10 * Stages.Value["encode"]) + 1);

    /// <summary>
    /// The stage made cheaper: disposing the context of that query costs about what encoding the pair does, where every
    /// inlined definition left for the finalizer cost a pass over all of the context's terms.
    /// </summary>
    [Fact]
    public void DisposeCostGrowsWithTheDefinitionsAndNotTheirSquare() =>
        Assert.InRange(Stages.Value["dispose"], 0, (10 * Stages.Value["encode"]) + 1);

    /// <summary><see cref="Z3Backend.Inline"/> as it was before ticket P2-076: every definition so far handed to Z3 at every step.</summary>
    private static BoolExpr[] Original(Context context, IEnumerable<BoolExpr> assertions, BoolExpr[] query)
    {
        List<Expr> names = [];
        List<Expr> values = [];
        foreach (BoolExpr assertion in assertions)
        {
            if (assertion.IsConst)
            {
                names.Add(assertion);
                values.Add(context.MkTrue());
            }
            else if (assertion.IsEq && assertion.Args[0].IsConst)
            {
                Expr value = assertion.Args[1].Substitute([.. names], [.. values]);
                names.Add(assertion.Args[0]);
                values.Add(value);
            }
        }

        Expr[] from = [.. names];
        Expr[] to = [.. values];
        return [.. query.Select(q => (BoolExpr)q.Substitute(from, to))];
    }

    /// <summary>A procedure of <see cref="Calls"/> calls in a row, each taking the last one's result plus <paramref name="step"/>.</summary>
    private static IrProcedure StraightLine(int step)
    {
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"proc \"T::Init(int)\" (%a: bv32) -> bv32 entry B0\nB0:\n  %k: bv32 = const bv32 {step}\n");
        string previous = "%a";
        for (int i = 0; i < Calls; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"  %c{i}: bv32 = call \"T::Set(int)\"({previous})\n  %s{i}: bv32 = add %c{i}, %k\n");
            previous = string.Create(CultureInfo.InvariantCulture, $"%s{i}");
        }

        return IrText.Parse(text.Append(CultureInfo.InvariantCulture, $"  ret {previous}\n").ToString());
    }
}
