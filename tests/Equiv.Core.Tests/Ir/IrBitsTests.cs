using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Core.Tests.Ir;

/// <summary>Bitvector semantics at width boundaries; SMT-LIB rules for division by zero and wide shifts.</summary>
public sealed class IrBitsTests
{
    [Theory]
    [InlineData(IrBinaryOp.Add, 8, 0xFF, 1, 0x00)]
    [InlineData(IrBinaryOp.Add, 64, ulong.MaxValue, 1, 0)]
    [InlineData(IrBinaryOp.Sub, 8, 0, 1, 0xFF)]
    [InlineData(IrBinaryOp.Sub, 64, 0, 1, ulong.MaxValue)]
    [InlineData(IrBinaryOp.Mul, 8, 0x80, 2, 0)]
    [InlineData(IrBinaryOp.Mul, 64, 0x8000_0000_0000_0000, 3, 0x8000_0000_0000_0000)]
    [InlineData(IrBinaryOp.UDiv, 8, 200, 7, 28)]
    [InlineData(IrBinaryOp.UDiv, 8, 200, 0, 0xFF)]
    [InlineData(IrBinaryOp.URem, 8, 200, 7, 4)]
    [InlineData(IrBinaryOp.URem, 8, 200, 0, 200)]
    [InlineData(IrBinaryOp.SDiv, 8, 0xF9, 2, 0xFD)]
    [InlineData(IrBinaryOp.SDiv, 8, 7, 0, 0xFF)]
    [InlineData(IrBinaryOp.SDiv, 8, 0xF9, 0, 1)]
    [InlineData(IrBinaryOp.SDiv, 8, 0x80, 0xFF, 0x80)]
    [InlineData(IrBinaryOp.SDiv, 64, 0x8000_0000_0000_0000, ulong.MaxValue, 0x8000_0000_0000_0000)]
    [InlineData(IrBinaryOp.SRem, 8, 0xF9, 2, 0xFF)]
    [InlineData(IrBinaryOp.SRem, 8, 0xF9, 0, 0xF9)]
    [InlineData(IrBinaryOp.SRem, 64, 0x8000_0000_0000_0000, ulong.MaxValue, 0)]
    [InlineData(IrBinaryOp.And, 8, 0xF0, 0x3C, 0x30)]
    [InlineData(IrBinaryOp.Or, 8, 0xF0, 0x0F, 0xFF)]
    [InlineData(IrBinaryOp.Xor, 8, 0xFF, 0x0F, 0xF0)]
    [InlineData(IrBinaryOp.Shl, 8, 0x81, 1, 0x02)]
    [InlineData(IrBinaryOp.Shl, 8, 1, 8, 0)]
    [InlineData(IrBinaryOp.Shl, 64, 1, 63, 0x8000_0000_0000_0000)]
    [InlineData(IrBinaryOp.Shl, 64, 1, 64, 0)]
    [InlineData(IrBinaryOp.LShr, 8, 0x80, 7, 1)]
    [InlineData(IrBinaryOp.LShr, 8, 0x80, 8, 0)]
    [InlineData(IrBinaryOp.AShr, 8, 0x80, 7, 0xFF)]
    [InlineData(IrBinaryOp.AShr, 8, 0x80, 200, 0xFF)]
    [InlineData(IrBinaryOp.AShr, 8, 0x40, 200, 0)]
    [InlineData(IrBinaryOp.AShr, 64, 0x8000_0000_0000_0000, 64, ulong.MaxValue)]
    public void ArithmeticWraps(IrBinaryOp op, int width, ulong a, ulong b, ulong expected)
    {
        Assert.Equal(new IrBitVecValue(width, expected), IrBits.Binary(op, new IrBitVecValue(width, a), new IrBitVecValue(width, b)));
    }

    [Theory]
    [InlineData(IrBinaryOp.Eq, 5, 5, true)]
    [InlineData(IrBinaryOp.Ne, 5, 5, false)]
    [InlineData(IrBinaryOp.Slt, 0x80, 0x7F, true)]
    [InlineData(IrBinaryOp.Sle, 0x80, 0x80, true)]
    [InlineData(IrBinaryOp.Sgt, 0x7F, 0x80, true)]
    [InlineData(IrBinaryOp.Sge, 0x7F, 0x80, true)]
    [InlineData(IrBinaryOp.Ult, 0x80, 0x7F, false)]
    [InlineData(IrBinaryOp.Ule, 0x7F, 0x7F, true)]
    [InlineData(IrBinaryOp.Ugt, 0x80, 0x7F, true)]
    [InlineData(IrBinaryOp.Uge, 0x7F, 0x80, false)]
    public void ComparisonsReadSignednessFromTheOperation(IrBinaryOp op, ulong a, ulong b, bool expected)
    {
        Assert.Equal(new IrBoolValue(expected), IrBits.Binary(op, new IrBitVecValue(8, a), new IrBitVecValue(8, b)));
    }

    [Theory]
    [InlineData(IrBinaryOp.And, true, false, false)]
    [InlineData(IrBinaryOp.Or, true, false, true)]
    [InlineData(IrBinaryOp.Xor, true, true, false)]
    [InlineData(IrBinaryOp.Eq, false, false, true)]
    [InlineData(IrBinaryOp.Ne, false, true, true)]
    public void BoolOperations(IrBinaryOp op, bool a, bool b, bool expected)
    {
        Assert.Equal(new IrBoolValue(expected), IrBits.Binary(op, new IrBoolValue(a), new IrBoolValue(b)));
    }

    [Fact]
    public void SortsCompareByIdNotByReference()
    {
        Assert.Equal(new IrBoolValue(Value: true), IrBits.Binary(IrBinaryOp.Eq, new IrSortValue("S", 1), new IrSortValue("S", 1)));
        Assert.Equal(new IrBoolValue(Value: true), IrBits.Binary(IrBinaryOp.Ne, new IrSortValue("S", 1), new IrSortValue("S", 2)));
    }

    [Theory]
    [InlineData(IrOverflowOp.SAdd, 8, 0x7F, 1, true)]
    [InlineData(IrOverflowOp.SAdd, 8, 0x80, 0xFF, true)]
    [InlineData(IrOverflowOp.SAdd, 8, 0x7E, 1, false)]
    [InlineData(IrOverflowOp.SAdd, 64, 0x7FFF_FFFF_FFFF_FFFF, 1, true)]
    [InlineData(IrOverflowOp.UAdd, 8, 0xFF, 1, true)]
    [InlineData(IrOverflowOp.UAdd, 8, 0xFE, 1, false)]
    [InlineData(IrOverflowOp.UAdd, 64, ulong.MaxValue, 1, true)]
    [InlineData(IrOverflowOp.SSub, 8, 0x80, 1, true)]
    [InlineData(IrOverflowOp.SSub, 8, 0x7F, 0xFF, true)]
    [InlineData(IrOverflowOp.SSub, 8, 0, 0x7F, false)]
    [InlineData(IrOverflowOp.USub, 8, 0, 1, true)]
    [InlineData(IrOverflowOp.USub, 8, 1, 1, false)]
    [InlineData(IrOverflowOp.SMul, 8, 0x40, 2, true)]
    [InlineData(IrOverflowOp.SMul, 8, 0x80, 0xFF, true)]
    [InlineData(IrOverflowOp.SMul, 8, 0xC0, 2, false)]
    [InlineData(IrOverflowOp.SMul, 64, 0x8000_0000_0000_0000, ulong.MaxValue, true)]
    [InlineData(IrOverflowOp.UMul, 8, 0x80, 2, true)]
    [InlineData(IrOverflowOp.UMul, 8, 0x7F, 2, false)]
    [InlineData(IrOverflowOp.UMul, 64, ulong.MaxValue, ulong.MaxValue, true)]
    [InlineData(IrOverflowOp.SDiv, 8, 0x80, 0xFF, true)]
    [InlineData(IrOverflowOp.SDiv, 8, 0x80, 2, false)]
    [InlineData(IrOverflowOp.SDiv, 8, 0x7F, 0xFF, false)]
    [InlineData(IrOverflowOp.SDiv, 64, 0x8000_0000_0000_0000, ulong.MaxValue, true)]
    public void OverflowPredicatesAtWidthBoundaries(IrOverflowOp op, int width, ulong a, ulong b, bool expected)
    {
        Assert.Equal(expected, IrBits.Overflows(op, new IrBitVecValue(width, a), new IrBitVecValue(width, b)));
    }

    [Theory]
    [InlineData(IrUnaryOp.Neg, 8, 1, 8, 0xFF)]
    [InlineData(IrUnaryOp.Neg, 8, 0x80, 8, 0x80)]
    [InlineData(IrUnaryOp.Not, 64, 0, 64, ulong.MaxValue)]
    [InlineData(IrUnaryOp.ZExt, 8, 0xFF, 32, 0xFF)]
    [InlineData(IrUnaryOp.SExt, 8, 0xFF, 32, 0xFFFF_FFFF)]
    [InlineData(IrUnaryOp.SExt, 32, 0x8000_0000, 64, 0xFFFF_FFFF_8000_0000)]
    [InlineData(IrUnaryOp.SExt, 8, 0x7F, 16, 0x7F)]
    [InlineData(IrUnaryOp.Trunc, 64, 0x1234_5678_9ABC_DEF0, 8, 0xF0)]
    public void UnaryOperations(IrUnaryOp op, int width, ulong a, int targetWidth, ulong expected)
    {
        Assert.Equal(new IrBitVecValue(targetWidth, expected), IrBits.UnaryOp(op, new IrBitVecValue(width, a), new IrBitVec(targetWidth)));
    }

    [Fact]
    public void BoolNotNegates()
    {
        Assert.Equal(new IrBoolValue(Value: false), IrBits.UnaryOp(IrUnaryOp.BoolNot, new IrBoolValue(Value: true), new IrBool()));
    }
}
