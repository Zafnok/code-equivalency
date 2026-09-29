using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Core.Tests.Ir;

/// <summary>The tuple spelling of VERIFICATION-MODEL.md section 2 (ticket P2-027).</summary>
public sealed class IrTupleTests
{
    [Fact]
    public void ATupleSortNamesItsElementTypesAndGivesThemBack()
    {
        IrSort sort = IrTuple.Sort([new IrBitVec(32), new IrBool(), new IrBitVec(8)]);

        Assert.Equal("tuple(bv32,bool,bv8)", sort.Name);
        Assert.Equal([new IrBitVec(32), new IrBool(), new IrBitVec(8)], IrTuple.Elements(sort));
    }

    [Theory]
    [InlineData("System.ValueTuple`2")]
    [InlineData("tuple(bv32")]
    public void AnyOtherSortHasNoElements(string name) => Assert.Empty(IrTuple.Elements(new IrSort(name)));

    [Fact]
    public void ANonSortHasNoElements() => Assert.Empty(IrTuple.Elements(new IrBitVec(32)));

    [Theory]
    [InlineData("tuple.item1", 1)]
    [InlineData("tuple.item3", 3)]
    [InlineData("tuple.new", null)]
    [InlineData("tuple.itemX", null)]
    [InlineData("f64.add", null)]
    public void AnElementReadNamesItsPosition(string function, int? position) => Assert.Equal(position, IrTuple.Position(function));

    [Fact]
    public void ItemIsTheInverseOfPosition() => Assert.Equal(2, IrTuple.Position(IrTuple.Item(2)));

    [Fact]
    public void NullIsRejected() => Assert.Throws<ArgumentNullException>(static () => IrTuple.Position(null!));
}
