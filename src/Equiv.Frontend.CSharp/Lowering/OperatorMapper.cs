using Equiv.Core.Ir;

using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The operator table of ticket M2-003: a C# <see cref="BinaryOperatorKind"/> plus operand signedness and
/// IR operand types to an <see cref="IrBinaryOp"/>, and a checked operator to its <see cref="IrOverflowOp"/>.
/// Null means the operation has no IR counterpart and lowers to <see cref="IrOpaque"/>.
/// </summary>
internal static class OperatorMapper
{
    /// <summary>
    /// The IR operation, or null for an operator kind C# never puts in a CFG (VB's <c>Power</c>, the
    /// short-circuit kinds) or for operands the IR operation does not take: bitvectors of one width
    /// (a shift count may differ), or Bool for <c>And/Or/Xor/Eq/Ne</c>.
    /// </summary>
    public static IrBinaryOp? Binary(BinaryOperatorKind kind, bool signed, IrType left, IrType right) =>
        (Kind(kind, signed), left, right) switch
        {
            ({ } op, IrBitVec, IrBitVec) when IsShift(op) => op,
            ({ } op, IrBitVec, IrBitVec) when left == right => op,
            ({ } op and (IrBinaryOp.And or IrBinaryOp.Or or IrBinaryOp.Xor or IrBinaryOp.Eq or IrBinaryOp.Ne), IrBool, IrBool) => op,
            _ => null,
        };

    public static bool IsShift(IrBinaryOp op) => op is IrBinaryOp.Shl or IrBinaryOp.AShr or IrBinaryOp.LShr;

    /// <summary>Whether a C# operator is a shift, whose operands need not share a width.</summary>
    public static bool IsShiftKind(BinaryOperatorKind kind) =>
        kind is BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift;

    /// <summary>The overflow test for a checked <paramref name="op"/>; null when it cannot overflow.</summary>
    public static IrOverflowOp? Overflow(IrBinaryOp op, bool signed) => op switch
    {
        IrBinaryOp.Add => signed ? IrOverflowOp.SAdd : IrOverflowOp.UAdd,
        IrBinaryOp.Sub => signed ? IrOverflowOp.SSub : IrOverflowOp.USub,
        IrBinaryOp.Mul => signed ? IrOverflowOp.SMul : IrOverflowOp.UMul,
        _ => null,
    };

    private static IrBinaryOp? Kind(BinaryOperatorKind kind, bool signed) => kind switch
    {
        BinaryOperatorKind.Add => IrBinaryOp.Add,
        BinaryOperatorKind.Subtract => IrBinaryOp.Sub,
        BinaryOperatorKind.Multiply => IrBinaryOp.Mul,
        BinaryOperatorKind.Divide => signed ? IrBinaryOp.SDiv : IrBinaryOp.UDiv,
        BinaryOperatorKind.Remainder => signed ? IrBinaryOp.SRem : IrBinaryOp.URem,
        BinaryOperatorKind.And => IrBinaryOp.And,
        BinaryOperatorKind.Or => IrBinaryOp.Or,
        BinaryOperatorKind.ExclusiveOr => IrBinaryOp.Xor,
        BinaryOperatorKind.LeftShift => IrBinaryOp.Shl,
        BinaryOperatorKind.RightShift => signed ? IrBinaryOp.AShr : IrBinaryOp.LShr,
        BinaryOperatorKind.UnsignedRightShift => IrBinaryOp.LShr,
        BinaryOperatorKind.Equals => IrBinaryOp.Eq,
        BinaryOperatorKind.NotEquals => IrBinaryOp.Ne,
        BinaryOperatorKind.LessThan => signed ? IrBinaryOp.Slt : IrBinaryOp.Ult,
        BinaryOperatorKind.LessThanOrEqual => signed ? IrBinaryOp.Sle : IrBinaryOp.Ule,
        BinaryOperatorKind.GreaterThan => signed ? IrBinaryOp.Sgt : IrBinaryOp.Ugt,
        BinaryOperatorKind.GreaterThanOrEqual => signed ? IrBinaryOp.Sge : IrBinaryOp.Uge,
        _ => null,
    };
}
