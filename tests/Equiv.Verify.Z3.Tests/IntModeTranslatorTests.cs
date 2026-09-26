using System.Numerics;

using Equiv.Core;
using Equiv.Core.Ir;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Ticket P1-001: <see cref="IntModeTranslator"/> against <see cref="IrInterpreter"/> on 8-bit edge values. An exact operation's
/// integer is the bitvector result read signed whenever it reports no overflow, and it reports an overflow exactly when
/// the true result is out of bounds; wrapping around, it is always the bitvector result and never overflows. Comparisons
/// and overflow tests are exact, and an operation it does not model gives no value.
/// </summary>
public sealed class IntModeTranslatorTests
{
    private static readonly ulong[] Edges = [0, 1, 2, 3, 0x7E, 0x7F, 0x80, 0x81, 0xFD, 0xFE, 0xFF];

    public static TheoryData<IrBinaryOp> Arithmetic => [IrBinaryOp.Add, IrBinaryOp.Sub, IrBinaryOp.Mul, IrBinaryOp.SDiv, IrBinaryOp.SRem, IrBinaryOp.UDiv, IrBinaryOp.URem];

    public static TheoryData<IrBinaryOp> Comparisons => [IrBinaryOp.Slt, IrBinaryOp.Sle, IrBinaryOp.Sgt, IrBinaryOp.Sge, IrBinaryOp.Ult, IrBinaryOp.Ule, IrBinaryOp.Ugt, IrBinaryOp.Uge];

    public static TheoryData<IrOverflowOp> OverflowOps => [.. Enum.GetValues<IrOverflowOp>()];

    public static TheoryData<IrUnaryOp> UnaryOps => [IrUnaryOp.Neg, IrUnaryOp.Not, IrUnaryOp.ZExt, IrUnaryOp.SExt, IrUnaryOp.Trunc];

    [Theory]
    [MemberData(nameof(Arithmetic))]
    public void AnExactOperationIsTheBitVectorResultUnlessItOverflows(IrBinaryOp op)
    {
        using Context context = new();
        IntModeTranslator checks = new(context);
        IntModeTranslator wraps = new(context, wraps: true);
        foreach ((IrBitVecValue a, IrBitVecValue b) in Pairs().Where(static p => p.B.Bits != 0))
        {
            IrBitVecValue expected = (IrBitVecValue)Interpret((t, x, y) => new IrBinary(t, op, x, y), new IrBitVec(8), a, b);
            (Expr? value, BoolExpr? overflow) = checks.Binary(op, checks.Literal(a), checks.Literal(b), 8);
            BigInteger exact = Integer(value!);
            Assert.Equal(exact < -128 || exact > 127, overflow is not null && overflow.Simplify().IsTrue);
            Assert.True(overflow is not null && overflow.Simplify().IsTrue || IntModeTranslator.Decode((IntNum)value!.Simplify(), 8) == expected, $"{a} {op} {b}");
            (Expr? wrapped, BoolExpr? never) = wraps.Binary(op, wraps.Literal(a), wraps.Literal(b), 8);
            Assert.Null(never);
            Assert.Equal(expected.TwosComplement, (long)Integer(wrapped!));
        }
    }

    [Theory]
    [MemberData(nameof(Comparisons))]
    public void AComparisonIsExact(IrBinaryOp op)
    {
        using Context context = new();
        IntModeTranslator translator = new(context);
        foreach ((IrBitVecValue a, IrBitVecValue b) in Pairs())
        {
            (Expr? value, BoolExpr? overflow) = translator.Binary(op, translator.Literal(a), translator.Literal(b), 8);

            Assert.Null(overflow);
            Assert.Equal(((IrBoolValue)Interpret((t, x, y) => new IrBinary(t, op, x, y), new IrBool(), a, b)).Value, value!.Simplify().IsTrue);
        }
    }

    [Theory]
    [MemberData(nameof(OverflowOps))]
    public void AnOverflowTestIsExact(IrOverflowOp op)
    {
        using Context context = new();
        IntModeTranslator translator = new(context);
        foreach ((IrBitVecValue a, IrBitVecValue b) in Pairs())
        {
            BoolExpr overflows = translator.Overflows(op, translator.Literal(a), translator.Literal(b), 8)!;

            Assert.Equal(((IrBoolValue)Interpret((t, x, y) => new IrOverflows(t, op, x, y), new IrBool(), a, b)).Value, overflows.Simplify().IsTrue);
        }
    }

    [Theory]
    [MemberData(nameof(UnaryOps))]
    public void AUnaryOperationIsTheBitVectorResultUnlessItOverflows(IrUnaryOp op)
    {
        using Context context = new();
        IntModeTranslator checks = new(context);
        IntModeTranslator wraps = new(context, wraps: true);
        (int from, int to) = op switch
        {
            IrUnaryOp.ZExt or IrUnaryOp.SExt => (8, 16),
            IrUnaryOp.Trunc => (16, 8),
            _ => (8, 8),
        };
        foreach (IrBitVecValue a in Edges.Select(e => new IrBitVecValue(from, e | (from == 16 ? e << 8 : 0))))
        {
            IrBitVecValue expected = (IrBitVecValue)Interpret((t, x, _) => new IrUnary(t, op, x), new IrBitVec(to), a, a);
            (Expr value, BoolExpr? overflow) = checks.Unary(op, checks.Literal(a), from, to);
            bool overflows = overflow is not null && overflow.Simplify().IsTrue;

            Assert.Equal(op == IrUnaryOp.Neg && a.Bits == 0x80, overflows);
            Assert.True(overflows || IntModeTranslator.Decode((IntNum)value.Simplify(), to) == expected, $"{op} {a}");
            Assert.Equal(expected.TwosComplement, (long)Integer(wraps.Unary(op, wraps.Literal(a), from, to).Value));
        }
    }

    [Fact]
    public void WhatItDoesNotModelHasNoValue()
    {
        using Context context = new();
        IntModeTranslator translator = new(context);
        IntExpr x = context.MkIntConst("x");
        IntExpr y = context.MkIntConst("y");
        IntExpr zero = translator.Literal(new IrBitVecValue(8, 0));

        Assert.Equal((null, null), translator.Binary(IrBinaryOp.Mul, x, y, 8));
        Assert.Equal((null, null), translator.Binary(IrBinaryOp.SDiv, x, zero, 8));
        Assert.Equal((null, null), translator.Binary(IrBinaryOp.URem, x, y, 8));
        Assert.All(
            (IrBinaryOp[])[IrBinaryOp.And, IrBinaryOp.Or, IrBinaryOp.Xor, IrBinaryOp.Shl, IrBinaryOp.AShr, IrBinaryOp.LShr],
            op => Assert.Equal((null, null), translator.Binary(op, x, y, 8)));
        Assert.Null(translator.Overflows(IrOverflowOp.SMul, x, y, 8));
        Assert.Null(translator.Overflows(IrOverflowOp.UMul, x, y, 8));
    }

    [Fact]
    public void AnIntegerDecodesToTheBitsItDenotesModuloTheWidth()
    {
        using Context context = new();

        Assert.Equal(new IrBitVecValue(8, 0xFF), IntModeTranslator.Decode(context.MkInt(-1), 8));
        Assert.Equal(new IrBitVecValue(8, 0x01), IntModeTranslator.Decode(context.MkInt(257), 8));
        Assert.Equal(new IrBitVecValue(8, 0x80), IntModeTranslator.Decode(context.MkInt(-128), 8));
        Assert.Equal(context.IntSort, new IntModeTranslator(context).Sort);
    }

    private static IEnumerable<(IrBitVecValue A, IrBitVecValue B)> Pairs() =>
        Edges.SelectMany(static a => Edges.Select(b => (new IrBitVecValue(8, a), new IrBitVecValue(8, b))));

    private static BigInteger Integer(Expr value) => ((IntNum)value.Simplify()).BigInteger;

    /// <summary>What the interpreter makes of <paramref name="operation"/> on the constants <paramref name="a"/> and <paramref name="b"/>.</summary>
    private static IrValue Interpret(Func<IrVar, IrVar, IrVar, IrInstruction> operation, IrType result, IrValue a, IrValue b)
    {
        IrVar x = new("a", a.Type);
        IrVar y = new("b", b.Type);
        IrVar r = new("r", result);
        IrProcedure procedure = new(
            new ProcedureIdentity("Ops::M()"),
            [],
            result,
            [new IrBlock(new IrBlockId(0), [new IrConst(x, a), new IrConst(y, b), operation(r, x, y)], new IrReturn(r, []))],
            new IrBlockId(0));
        return ((IrReturned)IrInterpreter.Run(procedure, new IrInputs([]), SpacerRung.NoCalls.Instance, 100).Outcome).Value!;
    }
}
