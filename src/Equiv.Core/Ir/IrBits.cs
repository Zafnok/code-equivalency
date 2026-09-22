using System.Collections.Frozen;

namespace Equiv.Core.Ir;

/// <summary>
/// Bitvector semantics shared by the interpreter. Results wrap to the operand width; division
/// by zero, <c>MinValue / -1</c> and shifts by at least the width follow SMT-LIB (Z3) exactly.
/// </summary>
internal static class IrBits
{
    private static readonly FrozenDictionary<IrBinaryOp, Func<ulong, ulong, int, ulong>> Arithmetic =
        new Dictionary<IrBinaryOp, Func<ulong, ulong, int, ulong>>
        {
            [IrBinaryOp.Add] = static (a, b, _) => a + b,
            [IrBinaryOp.Sub] = static (a, b, _) => a - b,
            [IrBinaryOp.Mul] = static (a, b, _) => a * b,
            [IrBinaryOp.UDiv] = static (a, b, w) => b == 0 ? Mask(w) : a / b,
            [IrBinaryOp.URem] = static (a, b, _) => b == 0 ? a : a % b,
            [IrBinaryOp.SDiv] = SignedDivide,
            [IrBinaryOp.SRem] = SignedRemainder,
            [IrBinaryOp.And] = static (a, b, _) => a & b,
            [IrBinaryOp.Or] = static (a, b, _) => a | b,
            [IrBinaryOp.Xor] = static (a, b, _) => a ^ b,
            [IrBinaryOp.Shl] = static (a, b, w) => b >= (ulong)w ? 0 : a << (int)b,
            [IrBinaryOp.LShr] = static (a, b, w) => b >= (ulong)w ? 0 : a >> (int)b,
            [IrBinaryOp.AShr] = static (a, b, w) => (ulong)(ToSigned(a, w) >> (int)Math.Min(b, (ulong)w - 1)),
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<IrBinaryOp, Func<ulong, ulong, int, bool>> Comparisons =
        new Dictionary<IrBinaryOp, Func<ulong, ulong, int, bool>>
        {
            [IrBinaryOp.Slt] = static (a, b, w) => ToSigned(a, w) < ToSigned(b, w),
            [IrBinaryOp.Sle] = static (a, b, w) => ToSigned(a, w) <= ToSigned(b, w),
            [IrBinaryOp.Sgt] = static (a, b, w) => ToSigned(a, w) > ToSigned(b, w),
            [IrBinaryOp.Sge] = static (a, b, w) => ToSigned(a, w) >= ToSigned(b, w),
            [IrBinaryOp.Ult] = static (a, b, _) => a < b,
            [IrBinaryOp.Ule] = static (a, b, _) => a <= b,
            [IrBinaryOp.Ugt] = static (a, b, _) => a > b,
            [IrBinaryOp.Uge] = static (a, b, _) => a >= b,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<IrBinaryOp, Func<bool, bool, bool>> Logical =
        new Dictionary<IrBinaryOp, Func<bool, bool, bool>>
        {
            [IrBinaryOp.And] = static (a, b) => a & b,
            [IrBinaryOp.Or] = static (a, b) => a | b,
            [IrBinaryOp.Xor] = static (a, b) => a ^ b,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<IrOverflowOp, Func<ulong, ulong, int, bool>> Overflow =
        new Dictionary<IrOverflowOp, Func<ulong, ulong, int, bool>>
        {
            [IrOverflowOp.SAdd] = static (a, b, w) => OutsideSigned((Int128)ToSigned(a, w) + ToSigned(b, w), w),
            [IrOverflowOp.UAdd] = static (a, b, w) => (UInt128)a + b > Mask(w),
            [IrOverflowOp.SSub] = static (a, b, w) => OutsideSigned((Int128)ToSigned(a, w) - ToSigned(b, w), w),
            [IrOverflowOp.USub] = static (a, b, _) => a < b,
            [IrOverflowOp.SMul] = static (a, b, w) => OutsideSigned((Int128)ToSigned(a, w) * ToSigned(b, w), w),
            [IrOverflowOp.UMul] = static (a, b, w) => (UInt128)a * b > Mask(w),
            [IrOverflowOp.SDiv] = static (a, b, w) => (ToSigned(a, w) == MinSigned(w)) & (ToSigned(b, w) == -1),
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<IrUnaryOp, Func<IrBitVecValue, int, ulong>> Unary =
        new Dictionary<IrUnaryOp, Func<IrBitVecValue, int, ulong>>
        {
            [IrUnaryOp.Neg] = static (v, _) => 0 - v.Bits,
            [IrUnaryOp.Not] = static (v, _) => ~v.Bits,
            [IrUnaryOp.ZExt] = static (v, _) => v.Bits,
            [IrUnaryOp.SExt] = static (v, _) => (ulong)v.TwosComplement,
            [IrUnaryOp.Trunc] = static (v, _) => v.Bits,
        }.ToFrozenDictionary();

    public static ulong Mask(int width) => ulong.MaxValue >> (64 - width);

    public static long ToSigned(ulong bits, int width) => (long)(bits << (64 - width)) >> (64 - width);

    /// <summary>Evaluates a type-correct binary operation (see <see cref="IrValidator"/>).</summary>
    public static IrValue Binary(IrBinaryOp op, IrValue a, IrValue b)
    {
        if (op is IrBinaryOp.Eq or IrBinaryOp.Ne)
        {
            return new IrBoolValue(a.Equals(b) == (op == IrBinaryOp.Eq));
        }

        if (a is IrBoolValue left)
        {
            return new IrBoolValue(Logical[op](left.Value, ((IrBoolValue)b).Value));
        }

        IrBitVecValue x = (IrBitVecValue)a;
        IrBitVecValue y = (IrBitVecValue)b;
        return Comparisons.TryGetValue(op, out Func<ulong, ulong, int, bool>? compare)
            ? new IrBoolValue(compare(x.Bits, y.Bits, x.Width))
            : new IrBitVecValue(x.Width, Arithmetic[op](x.Bits, y.Bits, x.Width) & Mask(x.Width));
    }

    public static bool Overflows(IrOverflowOp op, IrBitVecValue a, IrBitVecValue b) => Overflow[op](a.Bits, b.Bits, a.Width);

    /// <summary>Evaluates a type-correct unary operation producing a value of <paramref name="targetType"/>.</summary>
    public static IrValue UnaryOp(IrUnaryOp op, IrValue a, IrType targetType)
    {
        if (op == IrUnaryOp.BoolNot)
        {
            return new IrBoolValue(!((IrBoolValue)a).Value);
        }

        int width = ((IrBitVec)targetType).Width;
        return new IrBitVecValue(width, Unary[op]((IrBitVecValue)a, width) & Mask(width));
    }

    private static long MinSigned(int width) => -1L << (width - 1);

    private static bool OutsideSigned(Int128 value, int width) => (value < MinSigned(width)) | (value > ~MinSigned(width));

    private static ulong SignedDivide(ulong a, ulong b, int width)
    {
        long dividend = ToSigned(a, width);
        long divisor = ToSigned(b, width);
        if (divisor == 0)
        {
            return dividend < 0 ? 1 : Mask(width);
        }

        // MinValue / -1 wraps to MinValue; computed as negation so the 64-bit case cannot trap.
        return divisor == -1 ? 0 - a : (ulong)(dividend / divisor);
    }

    private static ulong SignedRemainder(ulong a, ulong b, int width)
    {
        long dividend = ToSigned(a, width);
        long divisor = ToSigned(b, width);
        return divisor switch
        {
            0 => a,
            -1 => 0,
            _ => (ulong)(dividend % divisor),
        };
    }
}
