using System.Globalization;
using System.Numerics;

using Equiv.Core.Ir;

using Microsoft.Z3;

namespace Equiv.Verify.Z3;

/// <summary>
/// Rung 4's integer mode (<c>--chc-int-mode</c>; ticket P1-001; VERIFICATION-MODEL.md section 5.1). Spacer infers linear
/// integer invariants far better than bitvector ones, so a bitvector of width <c>w</c> becomes the integer its bits denote,
/// read signed, within <c>[-2^(w-1), 2^(w-1) - 1]</c>. An operation is either exact or not modelled. An exact one is the
/// integer operation that agrees with the bitvector one whenever its result is within bounds, and says when it is not
/// (<see cref="Binary"/>'s overflow condition). Any other operation (bitwise ones, shifts, a product of two unknowns, a
/// division by an unknown) gives no value: the result is any integer within bounds, which includes the real one, so
/// integer runs over-approximate the bitvector runs as long as no exact operation overflows. That is why rung 4 uses this
/// mode only after a Spacer query has shown no overflow condition is reachable: then every bitvector run is an integer run
/// and a proof over the integers is a proof over the bitvectors. A derivation over the integers may use a value no
/// bitvector run computes, which is why rung 4 replays every derivation. Unsigned comparisons and conversions stay exact
/// by reading a negative integer as itself plus <c>2^w</c>.
/// </summary>
internal sealed class IntModeTranslator(Context context)
{
    /// <summary>The sort of every bitvector, whatever its width.</summary>
    public Sort Sort => context.IntSort;

    /// <summary>The integer a bitvector's bits denote, read signed.</summary>
    public IntNum Literal(IrBitVecValue value) => Int(value.TwosComplement);

    /// <summary>Whether <paramref name="value"/> is an integer some bitvector of <paramref name="width"/> bits denotes.</summary>
    public BoolExpr InRange(Expr value, int width) =>
        context.MkAnd(context.MkGe((IntExpr)value, Int(Min(width))), context.MkLe((IntExpr)value, Int(Max(width))));

    /// <summary>
    /// A binary bitvector operation of <paramref name="width"/> bits on the integers <paramref name="a"/> and
    /// <paramref name="b"/>: its value, or null when it is not modelled exactly; and when the exact value is out of
    /// bounds, or null when it never is. Comparisons are Bool; equality is not asked for, since it is exact on any sort.
    /// </summary>
    public (Expr? Value, BoolExpr? Overflow) Binary(IrBinaryOp op, IntExpr a, IntExpr b, int width) => op switch
    {
        IrBinaryOp.Add => Checked(context.MkAdd(a, b), width),
        IrBinaryOp.Sub => Checked(context.MkSub(a, b), width),
        IrBinaryOp.Mul when a.IsIntNum || b.IsIntNum => Checked(context.MkMul(a, b), width),
        IrBinaryOp.SDiv when NonZero(b) => Checked(TruncatedDivision(a, b), width),
        IrBinaryOp.SRem when NonZero(b) => (Remainder(a, b), null),
        IrBinaryOp.UDiv when NonZero(b) => (Signed(context.MkDiv(Unsigned(a, width), Unsigned(b, width)), width), null),
        IrBinaryOp.URem when NonZero(b) => (Signed(context.MkMod(Unsigned(a, width), Unsigned(b, width)), width), null),
        IrBinaryOp.Slt => (context.MkLt(a, b), null),
        IrBinaryOp.Sle => (context.MkLe(a, b), null),
        IrBinaryOp.Sgt => (context.MkGt(a, b), null),
        IrBinaryOp.Sge => (context.MkGe(a, b), null),
        IrBinaryOp.Ult => (context.MkLt(Unsigned(a, width), Unsigned(b, width)), null),
        IrBinaryOp.Ule => (context.MkLe(Unsigned(a, width), Unsigned(b, width)), null),
        IrBinaryOp.Ugt => (context.MkGt(Unsigned(a, width), Unsigned(b, width)), null),
        IrBinaryOp.Uge => (context.MkGe(Unsigned(a, width), Unsigned(b, width)), null),
        _ => (null, null),
    };

    /// <summary>
    /// A unary bitvector operation from <paramref name="from"/> to <paramref name="to"/> bits, as <see cref="Binary"/>:
    /// negation overflows only at the minimum, and bitwise not, sign extension, zero extension and truncation are exact.
    /// </summary>
    public (Expr Value, BoolExpr? Overflow) Unary(IrUnaryOp op, IntExpr a, int from, int to) => op switch
    {
        IrUnaryOp.Neg => Checked(context.MkUnaryMinus(a), to),
        IrUnaryOp.Not => (context.MkSub(context.MkUnaryMinus(a), Int(1)), null),
        IrUnaryOp.SExt => (a, null),
        IrUnaryOp.ZExt => (Unsigned(a, from), null),
        _ => (Signed(context.MkMod(a, Int(BigInteger.One << to)), to), null),
    };

    /// <summary>
    /// <see cref="IrOverflows"/> of <paramref name="width"/> bits: whether the checked operation overflows, or null for a
    /// product of two unknowns, which is not modelled (the flag is then any Bool).
    /// </summary>
    public BoolExpr? Overflows(IrOverflowOp op, IntExpr a, IntExpr b, int width) => op switch
    {
        IrOverflowOp.SAdd => context.MkNot(InRange(context.MkAdd(a, b), width)),
        IrOverflowOp.UAdd => context.MkGt(context.MkAdd(Unsigned(a, width), Unsigned(b, width)), Int(MaxUnsigned(width))),
        IrOverflowOp.SSub => context.MkNot(InRange(context.MkSub(a, b), width)),
        IrOverflowOp.USub => context.MkLt(Unsigned(a, width), Unsigned(b, width)),
        IrOverflowOp.SMul when a.IsIntNum || b.IsIntNum => context.MkNot(InRange(context.MkMul(a, b), width)),
        IrOverflowOp.UMul when a.IsIntNum || b.IsIntNum => context.MkGt(context.MkMul(Unsigned(a, width), Unsigned(b, width)), Int(MaxUnsigned(width))),
        IrOverflowOp.SDiv => context.MkAnd(context.MkEq(a, Int(Min(width))), context.MkEq(b, Int(-1))),
        _ => null,
    };

    /// <summary>The bitvector whose bits an integer denotes: the integer modulo <c>2^width</c>.</summary>
    public static IrBitVecValue Decode(IntNum value, int width)
    {
        BigInteger modulus = BigInteger.One << width;
        return new IrBitVecValue(width, (ulong)(((value.BigInteger % modulus) + modulus) % modulus));
    }

    private static BigInteger Min(int width) => -(BigInteger.One << (width - 1));

    private static BigInteger Max(int width) => (BigInteger.One << (width - 1)) - 1;

    private static BigInteger MaxUnsigned(int width) => (BigInteger.One << width) - 1;

    private static bool NonZero(IntExpr divisor) => divisor is IntNum { BigInteger.IsZero: false };

    private IntNum Int(BigInteger value) => context.MkInt(value.ToString(CultureInfo.InvariantCulture));

    private (Expr Value, BoolExpr? Overflow) Checked(ArithExpr value, int width) => (value, context.MkNot(InRange(value, width)));

    /// <summary>C#'s division, which truncates toward zero; SMT-LIB's <c>div</c> is Euclidean.</summary>
    private IntExpr TruncatedDivision(IntExpr a, IntExpr b) =>
        (IntExpr)context.MkITE(context.MkGe(a, Int(0)), context.MkDiv(a, b), context.MkUnaryMinus(context.MkDiv((IntExpr)context.MkUnaryMinus(a), b)));

    /// <summary>C#'s remainder, which takes the dividend's sign; SMT-LIB's <c>mod</c> is never negative.</summary>
    private IntExpr Remainder(IntExpr a, IntExpr b) =>
        (IntExpr)context.MkITE(context.MkGe(a, Int(0)), context.MkMod(a, b), context.MkUnaryMinus(context.MkMod((IntExpr)context.MkUnaryMinus(a), b)));

    /// <summary>The bits of <paramref name="a"/> read unsigned: a negative integer plus <c>2^width</c>.</summary>
    private IntExpr Unsigned(IntExpr a, int width) =>
        a is IntNum numeral
            ? Int(numeral.BigInteger.Sign < 0 ? numeral.BigInteger + (BigInteger.One << width) : numeral.BigInteger)
            : (IntExpr)context.MkITE(context.MkGe(a, Int(0)), a, context.MkAdd(a, Int(BigInteger.One << width)));

    /// <summary>An unsigned reading within <c>[0, 2^width)</c> read signed again.</summary>
    private IntExpr Signed(ArithExpr unsigned, int width) =>
        (IntExpr)context.MkITE(context.MkGe(unsigned, Int(BigInteger.One << (width - 1))), context.MkSub(unsigned, Int(BigInteger.One << width)), unsigned);
}
