using System.Collections.Frozen;

using Equiv.Core.Ir;

using Microsoft.Z3;

namespace Equiv.Verify.Z3.Refinement;

/// <summary>
/// The real meaning of each interpretable pure function as a Z3 term (ADR 0053 decisions 2 and 3; ticket P1-030), for
/// exactly the names <see cref="IrPureMeaning.Functions"/> lists; <see cref="IrPureMeaning.Evaluate"/> is the same
/// function on concrete values, and a property test holds the two together. Floating point is Z3's IEEE 754 theory,
/// rounding to nearest even. A floating-point to integer conversion truncates toward zero where the truncated value fits
/// the target, and is the uninterpreted function it was everywhere else.
/// </summary>
internal static class InterpretedPure
{
    private static readonly FrozenDictionary<string, Func<Context, Expr[], Func<Expr>, Expr>> Terms = Build();

    /// <summary>Whether <paramref name="function"/> has a term here.</summary>
    public static bool Has(string function) => Terms.ContainsKey(function);

    /// <summary>
    /// <paramref name="function"/> applied to <paramref name="args"/>. <paramref name="uninterpreted"/> is the application
    /// of the shared function it would otherwise be, which a partial meaning falls back to.
    /// </summary>
    public static Expr Term(Context context, string function, Expr[] args, Func<Expr> uninterpreted) => Terms[function](context, args, uninterpreted);

    private static FrozenDictionary<string, Func<Context, Expr[], Func<Expr>, Expr>> Build()
    {
        Dictionary<string, Func<Context, Expr[], Func<Expr>, Expr>> terms = new(StringComparer.Ordinal);
        foreach (string type in (string[])["System.IntPtr", "System.UIntPtr"])
        {
            terms[$"op:{type}::op_Equality({type},{type})"] = static (c, a, _) => c.MkEq(a[0], a[1]);
            terms[$"op:{type}::op_Inequality({type},{type})"] = static (c, a, _) => c.MkNot(c.MkEq(a[0], a[1]));
        }

        foreach (string format in (string[])["f32", "f64"])
        {
            terms[format + ".add"] = static (c, a, _) => c.MkFPAdd(c.MkFPRNE(), (FPExpr)a[0], (FPExpr)a[1]);
            terms[format + ".sub"] = static (c, a, _) => c.MkFPSub(c.MkFPRNE(), (FPExpr)a[0], (FPExpr)a[1]);
            terms[format + ".mul"] = static (c, a, _) => c.MkFPMul(c.MkFPRNE(), (FPExpr)a[0], (FPExpr)a[1]);
            terms[format + ".div"] = static (c, a, _) => c.MkFPDiv(c.MkFPRNE(), (FPExpr)a[0], (FPExpr)a[1]);
            terms[format + ".neg"] = static (c, a, _) => c.MkFPNeg((FPExpr)a[0]);
            terms[format + ".eq"] = static (c, a, _) => c.MkFPEq((FPExpr)a[0], (FPExpr)a[1]);
            terms[format + ".ne"] = static (c, a, _) => c.MkNot(c.MkFPEq((FPExpr)a[0], (FPExpr)a[1]));
            terms[format + ".lt"] = static (c, a, _) => c.MkFPLt((FPExpr)a[0], (FPExpr)a[1]);
            terms[format + ".le"] = static (c, a, _) => c.MkFPLEq((FPExpr)a[0], (FPExpr)a[1]);
            terms[format + ".gt"] = static (c, a, _) => c.MkFPGt((FPExpr)a[0], (FPExpr)a[1]);
            terms[format + ".ge"] = static (c, a, _) => c.MkFPGEq((FPExpr)a[0], (FPExpr)a[1]);
        }

        terms["conv.f32.f64"] = static (c, a, _) => c.MkFPToFP(c.MkFPRNE(), (FPExpr)a[0], c.MkFPSort64());
        terms["conv.f64.f32"] = static (c, a, _) => c.MkFPToFP(c.MkFPRNE(), (FPExpr)a[0], c.MkFPSort32());
        foreach ((string code, int width, bool signed) in IrPureMeaning.IntegerCodes)
        {
            terms[$"conv.f32.{code}"] = (c, a, fallback) => Truncated(c, (FPExpr)a[0], width, signed, fallback);
            terms[$"conv.f64.{code}"] = (c, a, fallback) => Truncated(c, (FPExpr)a[0], width, signed, fallback);
            if (width <= 32)
            {
                terms[$"conv.{code}.f32"] = (c, a, _) => c.MkFPToFP(c.MkFPRNE(), (BitVecExpr)a[0], c.MkFPSort32(), signed);
                terms[$"conv.{code}.f64"] = (c, a, _) => c.MkFPToFP(c.MkFPRNE(), (BitVecExpr)a[0], c.MkFPSort64(), signed);
            }
        }

        return terms.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// <paramref name="value"/> truncated toward zero as an integer of <paramref name="width"/> bits where the truncated
    /// value is in the type's range, and <paramref name="uninterpreted"/> elsewhere, which covers infinities and NaN: no
    /// comparison with a NaN holds. Both limits are powers of two, which binary32 and binary64 hold exactly.
    /// </summary>
    private static Expr Truncated(Context context, FPExpr value, int width, bool signed, Func<Expr> uninterpreted)
    {
        FPSort sort = (FPSort)value.Sort;
        FPExpr whole = context.MkFPRoundToIntegral(context.MkFPRTZ(), value);
        double limit = Math.ScaleB(1.0, signed ? width - 1 : width);
        BoolExpr fits = context.MkAnd(context.MkFPLt(whole, context.MkFP(limit, sort)), context.MkFPGEq(whole, context.MkFP(signed ? -limit : 0.0, sort)));
        return context.MkITE(fits, context.MkFPToBV(context.MkFPRTZ(), value, (uint)width, signed), uninterpreted());
    }
}
