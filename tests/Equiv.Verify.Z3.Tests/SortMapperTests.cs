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
}
