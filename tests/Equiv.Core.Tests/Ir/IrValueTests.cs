using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Core.Tests.Ir;

public sealed class IrValueTests
{
    private static readonly IrMap BvToBool = new(new IrBitVec(8), new IrBool());

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void BitVecAcceptsTheFourWidths(int width)
    {
        Assert.Equal(width, new IrBitVec(width).Width);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(48)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(128)]
    public void BitVecRejectsOtherWidths(int width)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IrBitVec(width));
    }

    [Fact]
    public void BitVecValueRejectsBitsOutsideItsWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IrBitVecValue(8, 0x100));
    }

    [Fact]
    public void BitVecValueHasAWidthAndATwosComplementReading()
    {
        IrBitVecValue value = IrBitVecValue.FromSigned(16, -2);
        Assert.Equal(0xFFFEUL, value.Bits);
        Assert.Equal(-2, value.TwosComplement);
        Assert.Equal(new IrBitVec(16), value.Type);
        Assert.Equal(16, value.Width);
    }

    [Fact]
    public void ScalarValuesReportTheirType()
    {
        Assert.Equal(new IrBool(), new IrBoolValue(Value: true).Type);
        Assert.Equal(new IrSort("S"), new IrSortValue("S", 3).Type);
        Assert.Equal(BvToBool, Map().Type);
    }

    [Fact]
    public void MapReadsTheDefaultUnlessWritten()
    {
        IrMapValue map = Map().Write(new IrBitVecValue(8, 1), new IrBoolValue(Value: true));
        Assert.Equal(new IrBoolValue(Value: true), map.Read(new IrBitVecValue(8, 1)));
        Assert.Equal(new IrBoolValue(Value: false), map.Read(new IrBitVecValue(8, 2)));
    }

    [Fact]
    public void MapEqualityIsExtensional()
    {
        IrMapValue empty = Map();
        IrMapValue writtenWithDefault = empty.Write(new IrBitVecValue(8, 1), new IrBoolValue(Value: false));
        IrMapValue written = empty.Write(new IrBitVecValue(8, 1), new IrBoolValue(Value: true));

        Assert.Equal(empty, writtenWithDefault);
        Assert.Equal(writtenWithDefault, empty);
        Assert.Equal(empty.GetHashCode(), writtenWithDefault.GetHashCode());
        Assert.NotEqual(empty, written);
        Assert.NotEqual(written, empty);
        Assert.NotEqual(empty, empty with { Default = new IrBoolValue(Value: true) });
        Assert.NotEqual(empty, empty with { MapType = new IrMap(new IrBitVec(16), new IrBool()) });
        Assert.False(empty.Equals((IrMapValue?)null));
    }

    private static IrMapValue Map() => new(BvToBool, new IrBoolValue(Value: false), []);
}
