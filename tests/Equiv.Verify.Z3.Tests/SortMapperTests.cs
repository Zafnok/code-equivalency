using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>Naming and the <c>distinct</c> constraint for uninterpreted-sort literals (VERIFICATION-MODEL.md section 2).</summary>
public sealed class SortMapperTests
{
    [Fact]
    public void NameNamesEachIrTypeDistinctly()
    {
        Assert.Equal("bool", SortMapper.Name(new IrBool()));
        Assert.Equal("bv32", SortMapper.Name(new IrBitVec(32)));
        Assert.Equal("sort<S>", SortMapper.Name(new IrSort("S")));
        Assert.Equal("map<bv32,bool>", SortMapper.Name(new IrMap(new IrBitVec(32), new IrBool())));
    }

    [Fact]
    public void DistinctnessSkipsASortWithOnlyOneLiteral()
    {
        using Context context = new();
        SortMapper sorts = new(context);
        sorts.Literal(new IrSortValue("S", 1));

        Assert.Empty(sorts.Distinctness());
    }

    [Fact]
    public void DistinctnessAssertsEachSortWithTwoOrMoreLiteralsApart()
    {
        using Context context = new();
        SortMapper sorts = new(context);
        sorts.Literal(new IrSortValue("S", 2));
        sorts.Literal(new IrSortValue("S", 1));

        ImmutableArray<BoolExpr> distinctness = sorts.Distinctness();

        Assert.Equal("(distinct lit.S.1 lit.S.2)", Assert.Single(distinctness).ToString());
    }

    /// <summary>
    /// After <c>fpa2bv</c>, as a refined query is solved, an uninterpreted function can answer a NaN input and the NaN
    /// literal differently, since it is applied to their bits; given <see cref="SortMapper.OneNaN"/> of each, it cannot
    /// (ticket P2-221). A term of any other sort is left as it is.
    /// </summary>
    [Theory]
    [InlineData(false, Status.SATISFIABLE)]
    [InlineData(true, Status.UNSATISFIABLE)]
    public void OneNaNGivesAnUninterpretedFunctionOneNaN(bool canonical, Status expected)
    {
        using Context context = new();
        SortMapper sorts = new(context, floats: true);
        FPSort binary64 = context.MkFPSort64();
        FPExpr input = (FPExpr)context.MkConst("x", binary64);
        FuncDecl function = context.MkFuncDecl("f", binary64, context.MkBitVecSort(32));
        Expr Argument(Expr term) => canonical ? sorts.OneNaN(term) : term;
        using Tactic toBitVectors = context.MkTactic("fpa2bv");
        using Tactic bitBlast = context.MkTactic("bit-blast");
        using Tactic smt = context.MkTactic("smt");
        using Tactic pipeline = context.AndThen(toBitVectors, bitBlast, smt);
        using Solver solver = context.MkSolver(pipeline);

        solver.Add(context.MkFPIsNaN(input), context.MkNot(context.MkEq(context.MkApp(function, Argument(input)), context.MkApp(function, Argument(context.MkFPNaN(binary64))))));

        Assert.Equal(expected, solver.Check());
        Expr integer = context.MkBV(7, 32);
        Assert.Same(integer, sorts.OneNaN(integer));
    }
}
