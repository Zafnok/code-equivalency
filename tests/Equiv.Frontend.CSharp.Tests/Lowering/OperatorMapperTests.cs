using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis.Operations;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>The M2-003 operator table.</summary>
public sealed class OperatorMapperTests
{
    private static readonly IrBitVec Bv32 = new(32);
    private static readonly IrBitVec Bv64 = new(64);
    private static readonly IrBool Bool = new();

    [Theory]
    [InlineData(BinaryOperatorKind.Add, IrBinaryOp.Add, IrBinaryOp.Add)]
    [InlineData(BinaryOperatorKind.Subtract, IrBinaryOp.Sub, IrBinaryOp.Sub)]
    [InlineData(BinaryOperatorKind.Multiply, IrBinaryOp.Mul, IrBinaryOp.Mul)]
    [InlineData(BinaryOperatorKind.Divide, IrBinaryOp.SDiv, IrBinaryOp.UDiv)]
    [InlineData(BinaryOperatorKind.Remainder, IrBinaryOp.SRem, IrBinaryOp.URem)]
    [InlineData(BinaryOperatorKind.And, IrBinaryOp.And, IrBinaryOp.And)]
    [InlineData(BinaryOperatorKind.Or, IrBinaryOp.Or, IrBinaryOp.Or)]
    [InlineData(BinaryOperatorKind.ExclusiveOr, IrBinaryOp.Xor, IrBinaryOp.Xor)]
    [InlineData(BinaryOperatorKind.LeftShift, IrBinaryOp.Shl, IrBinaryOp.Shl)]
    [InlineData(BinaryOperatorKind.RightShift, IrBinaryOp.AShr, IrBinaryOp.LShr)]
    [InlineData(BinaryOperatorKind.UnsignedRightShift, IrBinaryOp.LShr, IrBinaryOp.LShr)]
    [InlineData(BinaryOperatorKind.Equals, IrBinaryOp.Eq, IrBinaryOp.Eq)]
    [InlineData(BinaryOperatorKind.NotEquals, IrBinaryOp.Ne, IrBinaryOp.Ne)]
    [InlineData(BinaryOperatorKind.LessThan, IrBinaryOp.Slt, IrBinaryOp.Ult)]
    [InlineData(BinaryOperatorKind.LessThanOrEqual, IrBinaryOp.Sle, IrBinaryOp.Ule)]
    [InlineData(BinaryOperatorKind.GreaterThan, IrBinaryOp.Sgt, IrBinaryOp.Ugt)]
    [InlineData(BinaryOperatorKind.GreaterThanOrEqual, IrBinaryOp.Sge, IrBinaryOp.Uge)]
    public void MapsEveryCSharpOperatorBySignedness(BinaryOperatorKind kind, IrBinaryOp whenSigned, IrBinaryOp whenUnsigned)
    {
        Assert.Equal(whenSigned, OperatorMapper.Binary(kind, signed: true, Bv32, Bv32));
        Assert.Equal(whenUnsigned, OperatorMapper.Binary(kind, signed: false, Bv32, Bv32));
    }

    [Theory]
    [InlineData(BinaryOperatorKind.Power)]
    [InlineData(BinaryOperatorKind.ConditionalAnd)]
    [InlineData(BinaryOperatorKind.Concatenate)]
    public void KindsWithoutAnIrOperationAreNull(BinaryOperatorKind kind) =>
        Assert.Null(OperatorMapper.Binary(kind, signed: true, Bv32, Bv32));

    [Fact]
    public void OperandsMustShareAWidthExceptForAShiftCount()
    {
        Assert.Null(OperatorMapper.Binary(BinaryOperatorKind.Add, signed: true, Bv64, Bv32));
        Assert.Equal(IrBinaryOp.Shl, OperatorMapper.Binary(BinaryOperatorKind.LeftShift, signed: true, Bv64, Bv32));
    }

    [Theory]
    [InlineData(BinaryOperatorKind.And, IrBinaryOp.And)]
    [InlineData(BinaryOperatorKind.Or, IrBinaryOp.Or)]
    [InlineData(BinaryOperatorKind.ExclusiveOr, IrBinaryOp.Xor)]
    [InlineData(BinaryOperatorKind.Equals, IrBinaryOp.Eq)]
    [InlineData(BinaryOperatorKind.NotEquals, IrBinaryOp.Ne)]
    public void BoolOperandsTakeLogicAndEquality(BinaryOperatorKind kind, IrBinaryOp op) =>
        Assert.Equal(op, OperatorMapper.Binary(kind, signed: false, Bool, Bool));

    [Fact]
    public void OtherOperandCombinationsAreNull()
    {
        Assert.Null(OperatorMapper.Binary(BinaryOperatorKind.Add, signed: false, Bool, Bool));
        Assert.Null(OperatorMapper.Binary(BinaryOperatorKind.And, signed: false, Bool, Bv32));
        Assert.Null(OperatorMapper.Binary(BinaryOperatorKind.Equals, signed: false, new IrSort("System.String"), new IrSort("System.String")));
    }

    [Theory]
    [InlineData(IrBinaryOp.Add, true, IrOverflowOp.SAdd)]
    [InlineData(IrBinaryOp.Add, false, IrOverflowOp.UAdd)]
    [InlineData(IrBinaryOp.Sub, true, IrOverflowOp.SSub)]
    [InlineData(IrBinaryOp.Sub, false, IrOverflowOp.USub)]
    [InlineData(IrBinaryOp.Mul, true, IrOverflowOp.SMul)]
    [InlineData(IrBinaryOp.Mul, false, IrOverflowOp.UMul)]
    public void CheckedOperatorsHaveAnOverflowTest(IrBinaryOp op, bool isSigned, IrOverflowOp overflow) =>
        Assert.Equal(overflow, OperatorMapper.Overflow(op, isSigned));

    [Theory]
    [InlineData(IrBinaryOp.SDiv)]
    [InlineData(IrBinaryOp.And)]
    [InlineData(IrBinaryOp.Shl)]
    public void OtherOperatorsCannotOverflowUnderChecked(IrBinaryOp op) => Assert.Null(OperatorMapper.Overflow(op, signed: true));
}
