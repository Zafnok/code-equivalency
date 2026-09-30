using System.Collections.Frozen;

using Equiv.Core.Ir;

namespace AbstractionSpike;

/// <summary>What an entry of <c>properties.abstractions</c> is, and whether the spike may give it its real meaning.</summary>
internal enum KindClass
{
    /// <summary>An <c>IrPure</c> with a closed-form meaning over the IR's own bitvector, Bool or sort-equality theory: re-queried.</summary>
    ClosedForm,

    /// <summary><c>f32.*</c>, <c>f64.*</c>, or a <c>conv.*</c> to or from floating point: post-MVP theory, counted only.</summary>
    FloatingPoint,

    /// <summary><c>dec.*</c>, or a <c>conv.*</c> to or from <c>decimal</c>: no SMT theory, counted only.</summary>
    Decimal,

    /// <summary>A <c>System.String</c> operator: post-MVP theory, counted only.</summary>
    String,

    /// <summary>Any other user-defined operator or conversion: its meaning is a method body, not a closed form. Counted only.</summary>
    UserOperator,

    /// <summary>An <c>opaque:</c> fragment (ADR 0024): out of scope, counted only.</summary>
    Opaque,
}

/// <summary>
/// The classification of an abstraction's identity. The pure catalogue (ADR 0025) holds only <c>f32</c>, <c>f64</c>,
/// <c>dec</c> and <c>conv</c> functions plus <c>op:</c> user-defined operators: integer arithmetic, integer comparisons and
/// <c>bool</c> logic already lower to <see cref="IrBinary"/>, so they never taint. The closed-form <c>IrPure</c> kinds are
/// therefore the <c>op:</c> operators whose meaning the IR states exactly, listed in <see cref="Interpretations"/>.
/// </summary>
internal static class Kinds
{
    public const string OpaquePrefix = "opaque:";

    /// <summary>
    /// <c>IntPtr</c> and <c>UIntPtr</c> lower to an uninterpreted sort (TypeMapper), and their <c>==</c> and <c>!=</c> are
    /// value equality on the one field, which is exactly the IR's sort <see cref="IrBinaryOp.Eq"/> and
    /// <see cref="IrBinaryOp.Ne"/>, on every runtime, and never throw.
    /// </summary>
    public static readonly FrozenDictionary<string, IrBinaryOp> Interpretations = new Dictionary<string, IrBinaryOp>(StringComparer.Ordinal)
    {
        ["op:System.IntPtr::op_Equality(System.IntPtr,System.IntPtr)"] = IrBinaryOp.Eq,
        ["op:System.IntPtr::op_Inequality(System.IntPtr,System.IntPtr)"] = IrBinaryOp.Ne,
        ["op:System.UIntPtr::op_Equality(System.UIntPtr,System.UIntPtr)"] = IrBinaryOp.Eq,
        ["op:System.UIntPtr::op_Inequality(System.UIntPtr,System.UIntPtr)"] = IrBinaryOp.Ne,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static KindClass Classify(string identity)
    {
        if (identity.StartsWith(OpaquePrefix, StringComparison.Ordinal))
        {
            return KindClass.Opaque;
        }

        if (Interpretations.ContainsKey(identity))
        {
            return KindClass.ClosedForm;
        }

        if (identity.StartsWith("op:System.String::", StringComparison.Ordinal))
        {
            return KindClass.String;
        }

        if (identity.StartsWith("op:", StringComparison.Ordinal))
        {
            return KindClass.UserOperator;
        }

        // Every catalogued function other than op: takes or yields float, double or decimal (PureCatalogue).
        return identity.StartsWith("dec.", StringComparison.Ordinal) || identity.Contains(".dec", StringComparison.Ordinal)
            ? KindClass.Decimal
            : KindClass.FloatingPoint;
    }
}
