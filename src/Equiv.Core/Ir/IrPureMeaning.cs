using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// The pure functions that have a real meaning a backend may give them, and that meaning on concrete values (ADR 0053
/// decisions 2 to 4; ticket P1-030). <see cref="IrInterpreter"/> computes an interpreted function with
/// <see cref="Evaluate"/> instead of asking its oracle, and a backend's terms must agree with it on every argument:
/// <list type="bullet">
/// <item><c>==</c> and <c>!=</c> of <c>System.IntPtr</c> and of <c>System.UIntPtr</c>: equality of the sort's elements;</item>
/// <item><c>f32.</c> and <c>f64.</c> <c>add sub mul div neg</c> and <c>eq ne lt le gt ge</c>: IEEE 754 binary32 and
/// binary64, round to nearest even, which is .NET's own <c>float</c> and <c>double</c> arithmetic;</item>
/// <item><c>conv.f32.f64</c> and <c>conv.f64.f32</c>;</item>
/// <item><c>conv.&lt;int&gt;.&lt;float&gt;</c> from an integer of at most 32 bits;</item>
/// <item><c>conv.&lt;float&gt;.&lt;int&gt;</c>, truncation toward zero, only on an argument whose truncated value the target
/// holds. On any other argument it has no meaning here (.NET 9 saturates, earlier runtimes do not), and
/// <see cref="Evaluate"/> gives null.</item>
/// </list>
/// A NaN is the one NaN of <see cref="IrFloat"/>. Nothing else is interpretable: <c>%</c>, <c>decimal</c>, a conversion
/// from a 64-bit integer, any other operator, and any <c>x87.</c> function.
/// </summary>
public static class IrPureMeaning
{
    private const string OperatorPrefix = "op:";

    private static readonly ImmutableArray<(string Code, int Width, bool Signed)> Integers =
    [
        ("i8", 8, true), ("u8", 8, false), ("i16", 16, true), ("u16", 16, false), ("char", 16, false),
        ("i32", 32, true), ("u32", 32, false), ("i64", 64, true), ("u64", 64, false),
    ];

    private static readonly FrozenDictionary<string, Func<ImmutableArray<IrValue>, IrValue?>> Meanings = Build();

    /// <summary>Every function name that has a meaning, in ordinal order.</summary>
    public static ImmutableArray<string> Functions { get; } = [.. Meanings.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The integer type codes a conversion's name uses, with each type's width and signedness.</summary>
    public static ImmutableArray<(string Code, int Width, bool Signed)> IntegerCodes => Integers;

    /// <summary>
    /// Whether <paramref name="pure"/> may be given its meaning: its function has one, the application is not
    /// runtime-sensitive (ADR 0040), and it raises nothing. An operator's one flag is that of an arbitrary user-defined
    /// operator, which these four never raise; any other application with a flag is a checked conversion.
    /// </summary>
    public static bool IsInterpretable(IrPure pure)
    {
        ArgumentNullException.ThrowIfNull(pure);
        return Meanings.ContainsKey(pure.Function)
            && !pure.RuntimeSensitive
            && (pure.Throws.IsEmpty || pure.Function.StartsWith(OperatorPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// <paramref name="function"/> applied to <paramref name="arguments"/>, or null when it has no meaning or none on these
    /// arguments. An interpreted application raises nothing.
    /// </summary>
    public static IrValue? Evaluate(string function, ImmutableArray<IrValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Meanings.TryGetValue(function, out Func<ImmutableArray<IrValue>, IrValue?>? meaning) ? meaning(arguments) : null;
    }

    private static FrozenDictionary<string, Func<ImmutableArray<IrValue>, IrValue?>> Build()
    {
        Dictionary<string, Func<ImmutableArray<IrValue>, IrValue?>> meanings = new(StringComparer.Ordinal);
        foreach (string type in (string[])["System.IntPtr", "System.UIntPtr"])
        {
            meanings[$"{OperatorPrefix}{type}::op_Equality({type},{type})"] = static a => new IrBoolValue(a[0] == a[1]);
            meanings[$"{OperatorPrefix}{type}::op_Inequality({type},{type})"] = static a => new IrBoolValue(a[0] != a[1]);
        }

        Arithmetic(meanings, "add", static (a, b) => a + b, static (a, b) => a + b);
        Arithmetic(meanings, "sub", static (a, b) => a - b, static (a, b) => a - b);
        Arithmetic(meanings, "mul", static (a, b) => a * b, static (a, b) => a * b);
        Arithmetic(meanings, "div", static (a, b) => a / b, static (a, b) => a / b);
        Comparison(meanings, "eq", static (a, b) => a == b, static (a, b) => a == b);
        Comparison(meanings, "ne", static (a, b) => a != b, static (a, b) => a != b);
        Comparison(meanings, "lt", static (a, b) => a < b, static (a, b) => a < b);
        Comparison(meanings, "le", static (a, b) => a <= b, static (a, b) => a <= b);
        Comparison(meanings, "gt", static (a, b) => a > b, static (a, b) => a > b);
        Comparison(meanings, "ge", static (a, b) => a >= b, static (a, b) => a >= b);
        meanings["f32.neg"] = static a => IrFloat.Of(-F32(a[0]));
        meanings["f64.neg"] = static a => IrFloat.Of(-F64(a[0]));
        meanings["conv.f32.f64"] = static a => IrFloat.Of((double)F32(a[0]));
        meanings["conv.f64.f32"] = static a => IrFloat.Of((float)F64(a[0]));
        foreach ((string code, int width, bool signed) in Integers)
        {
            meanings[$"conv.f32.{code}"] = a => Truncated(F32(a[0]), width, signed);
            meanings[$"conv.f64.{code}"] = a => Truncated(F64(a[0]), width, signed);
            if (width <= 32)
            {
                // A double holds every integer of at most 32 bits, so the one rounding is the narrowing to float.
                meanings[$"conv.{code}.f32"] = a => IrFloat.Of((float)Integer(a[0], signed));
                meanings[$"conv.{code}.f64"] = a => IrFloat.Of(Integer(a[0], signed));
            }
        }

        return meanings.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static void Arithmetic(Dictionary<string, Func<ImmutableArray<IrValue>, IrValue?>> meanings, string name, Func<float, float, float> single, Func<double, double, double> @double)
    {
        meanings["f32." + name] = a => IrFloat.Of(single(F32(a[0]), F32(a[1])));
        meanings["f64." + name] = a => IrFloat.Of(@double(F64(a[0]), F64(a[1])));
    }

    private static void Comparison(Dictionary<string, Func<ImmutableArray<IrValue>, IrValue?>> meanings, string name, Func<float, float, bool> single, Func<double, double, bool> @double)
    {
        meanings["f32." + name] = a => new IrBoolValue(single(F32(a[0]), F32(a[1])));
        meanings["f64." + name] = a => new IrBoolValue(@double(F64(a[0]), F64(a[1])));
    }

    private static float F32(IrValue value) => IrFloat.ToSingle((IrSortValue)value);

    private static double F64(IrValue value) => IrFloat.ToDouble((IrSortValue)value);

    private static double Integer(IrValue value, bool signed) =>
        signed ? ((IrBitVecValue)value).TwosComplement : ((IrBitVecValue)value).Bits;

    /// <summary>
    /// <paramref name="value"/> truncated toward zero as an integer of <paramref name="width"/> bits, or null when the
    /// truncated value is outside the type's range, infinite or NaN.
    /// </summary>
    private static IrBitVecValue? Truncated(double value, int width, bool signed)
    {
        double whole = Math.Truncate(value);
        double limit = Math.ScaleB(1.0, signed ? width - 1 : width);
        if (!(whole < limit && whole >= (signed ? -limit : 0.0)))
        {
            return null;
        }

        return signed ? IrBitVecValue.FromSigned(width, (long)whole) : new IrBitVecValue(width, (ulong)whole);
    }
}
