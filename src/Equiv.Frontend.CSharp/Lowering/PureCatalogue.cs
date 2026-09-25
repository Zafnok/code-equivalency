using System.Collections.Frozen;
using System.Collections.Immutable;

using Equiv.Core;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The closed catalogue of pure functions the lowerer applies with <c>IrPure</c> (ADR 0025; ticket M4-002): each name,
/// its argument and result types, and the exceptions it can raise, each of which gets its own flag and branches to a
/// throw of that exact type. It holds
/// <list type="bullet">
/// <item><c>f32.&lt;op&gt;</c> and <c>f64.&lt;op&gt;</c>: <c>add sub mul div rem</c>, <c>neg</c>, and the comparisons
/// <c>eq ne lt le gt ge</c>, which never throw;</item>
/// <item><c>dec.&lt;op&gt;</c>: the same operators, with <c>System.OverflowException</c> for <c>add sub mul div rem</c> and
/// <c>System.DivideByZeroException</c> for <c>div rem</c>, as <c>System.Decimal</c>'s operators document;</item>
/// <item><c>conv.&lt;from&gt;.&lt;to&gt;</c>: every C# numeric conversion to or from <c>float</c>, <c>double</c> or
/// <c>decimal</c>, with <c>System.OverflowException</c> for a conversion to <c>decimal</c> from floating point and from
/// <c>decimal</c> to an integral type (the BCL documents both), and for floating point to an integral type in a
/// <c>checked</c> context.</item>
/// </list>
/// Type codes are <c>i8 u8 i16 u16 char i32 u32 i64 u64 f32 f64 dec</c>. A user-defined operator or conversion is the
/// function <c>op:&lt;identity&gt;</c> (<see cref="UserDefined"/>). Anything else is not in the catalogue and stays opaque.
/// </summary>
internal static class PureCatalogue
{
    public const string Overflow = "System.OverflowException";

    public const string DivideByZero = "System.DivideByZeroException";

    /// <summary>What a user-defined operator can raise: anything, as an opaque call can, so its type is not known.</summary>
    public const string AnyException = "System.Exception";

    /// <summary>The name prefix of a user-defined operator's or conversion's function.</summary>
    public const string OperatorPrefix = "op:";

    private static readonly ImmutableArray<(SpecialType Type, string Code)> Integral =
    [
        (SpecialType.System_SByte, "i8"), (SpecialType.System_Byte, "u8"), (SpecialType.System_Int16, "i16"),
        (SpecialType.System_UInt16, "u16"), (SpecialType.System_Char, "char"), (SpecialType.System_Int32, "i32"),
        (SpecialType.System_UInt32, "u32"), (SpecialType.System_Int64, "i64"), (SpecialType.System_UInt64, "u64"),
    ];

    private static readonly ImmutableArray<(SpecialType Type, string Code)> FloatingPoint =
        [(SpecialType.System_Single, "f32"), (SpecialType.System_Double, "f64")];

    private static readonly (SpecialType Type, string Code) Decimal = (SpecialType.System_Decimal, "dec");

    private static readonly ImmutableArray<(BinaryOperatorKind Kind, string Name, bool Throws)> Arithmetic =
    [
        (BinaryOperatorKind.Add, "add", false), (BinaryOperatorKind.Subtract, "sub", false), (BinaryOperatorKind.Multiply, "mul", false),
        (BinaryOperatorKind.Divide, "div", true), (BinaryOperatorKind.Remainder, "rem", true),
    ];

    private static readonly ImmutableArray<(BinaryOperatorKind Kind, string Name)> Comparisons =
    [
        (BinaryOperatorKind.Equals, "eq"), (BinaryOperatorKind.NotEquals, "ne"), (BinaryOperatorKind.LessThan, "lt"),
        (BinaryOperatorKind.LessThanOrEqual, "le"), (BinaryOperatorKind.GreaterThan, "gt"), (BinaryOperatorKind.GreaterThanOrEqual, "ge"),
    ];

    private static readonly FrozenDictionary<(BinaryOperatorKind Kind, SpecialType Operand), Entry> BinaryEntries =
        FloatingPoint.Append(Decimal)
            .SelectMany(static t => Arithmetic.Select(a => (a.Kind, t.Type, Entry: new Entry(
                    $"{t.Code}.{a.Name}",
                    [t.Type, t.Type],
                    t.Type,
                    t == Decimal ? (a.Throws ? [DivideByZero, Overflow] : [Overflow]) : [])))
                .Concat(Comparisons.Select(c => (c.Kind, t.Type, Entry: new Entry($"{t.Code}.{c.Name}", [t.Type, t.Type], SpecialType.System_Boolean, [])))))
            .ToFrozenDictionary(static e => (e.Kind, e.Type), static e => e.Entry);

    private static readonly FrozenDictionary<SpecialType, Entry> Negations =
        FloatingPoint.Append(Decimal).ToFrozenDictionary(static t => t.Type, static t => new Entry($"{t.Code}.neg", [t.Type], t.Type, []));

    private static readonly FrozenDictionary<(SpecialType From, SpecialType To), Entry> ConversionEntries =
        Integral.SelectMany(static i => FloatingPoint.Select(f => (From: i, To: f, Throws: None, Checked: None))
                .Concat(FloatingPoint.Select(f => (From: f, To: i, Throws: None, Checked: OverflowOnly)))
                .Append((From: i, To: Decimal, Throws: None, Checked: None))
                .Append((From: Decimal, To: i, Throws: OverflowOnly, Checked: OverflowOnly)))
            .Concat(FloatingPoint.SelectMany(static f => FloatingPoint.Where(g => g != f).Select(g => (From: f, To: g, Throws: None, Checked: None))
                .Append((From: f, To: Decimal, Throws: OverflowOnly, Checked: OverflowOnly))
                .Append((From: Decimal, To: f, Throws: None, Checked: None))))
            .ToFrozenDictionary(
                static c => (c.From.Type, c.To.Type),
                static c => new Entry($"conv.{c.From.Code}.{c.To.Code}", [c.From.Type], c.To.Type, c.Throws) { CheckedThrows = c.Checked });

    private static ImmutableArray<string> None => [];

    private static ImmutableArray<string> OverflowOnly => [Overflow];

    /// <summary>Every catalogued function by name.</summary>
    public static FrozenDictionary<string, Entry> Entries { get; } =
        BinaryEntries.Values.Concat(Negations.Values).Concat(ConversionEntries.Values).ToFrozenDictionary(static e => e.Function, StringComparer.Ordinal);

    /// <summary>The function a binary operator applies to operands of <paramref name="left"/> and <paramref name="right"/>, or null.</summary>
    public static Entry? Binary(BinaryOperatorKind kind, ITypeSymbol left, ITypeSymbol right) =>
        left.SpecialType == right.SpecialType ? BinaryEntries.GetValueOrDefault((kind, left.SpecialType)) : null;

    /// <summary>The function unary <c>-</c> applies to an operand of <paramref name="operand"/>, or null.</summary>
    public static Entry? Negation(ITypeSymbol operand) => Negations.GetValueOrDefault(operand.SpecialType);

    /// <summary>Whether <paramref name="type"/> is <c>float</c>, <c>double</c> or <c>decimal</c>, whose unary <c>+</c> is its operand.</summary>
    public static bool IsCatalogued(ITypeSymbol type) => Negations.ContainsKey(type.SpecialType);

    /// <summary>The function a numeric conversion from <paramref name="from"/> to <paramref name="to"/> applies, or null.</summary>
    public static Entry? Conversion(ITypeSymbol from, ITypeSymbol to) => ConversionEntries.GetValueOrDefault((from.SpecialType, to.SpecialType));

    /// <summary>
    /// The function of a user-defined operator or conversion: <c>op:</c> followed by its normalised call identity. Its
    /// arguments are the method's parameters and its result the method's return type; it can raise an exception of any
    /// type, as an opaque call can.
    /// </summary>
    public static string UserDefined(CallIdentity identity) => OperatorPrefix + identity.Value;

    /// <summary>Whether the legacy compilation's floating-point arithmetic runs on the 32-bit x87 JIT (ticket M3-015's rule).</summary>
    public static bool IsX87(Compilation compilation) => compilation.Options.Platform is Platform.X86 or Platform.AnyCpu32BitPreferred;

    /// <summary>
    /// One catalogued function. <see cref="Throws"/> are the exceptions it can raise in an unchecked context, and
    /// <see cref="CheckedThrows"/> those in a <c>checked</c> one, the same unless set.
    /// </summary>
    public sealed record Entry(string Function, ImmutableArray<SpecialType> Arguments, SpecialType Result, ImmutableArray<string> Throws)
    {
        public ImmutableArray<string> CheckedThrows { get; init; } = Throws;

        /// <summary>The exceptions it can raise in a context that is <paramref name="isChecked"/>.</summary>
        public ImmutableArray<string> Raises(bool isChecked) => isChecked ? CheckedThrows : Throws;

        /// <summary>
        /// Whether its behaviour differs between .NET Framework and .NET, so the sides never share it (ADR 0025): a
        /// floating-point to integer conversion (saturating since .NET 9), and, on a legacy side whose floating point
        /// runs on x87, anything that takes or yields floating point.
        /// </summary>
        public bool RuntimeSensitive(bool x87)
        {
            bool fromFloat = Arguments.Any(IsFloatingPoint);
            return (fromFloat && Integral.Any(i => i.Type == Result)) || (x87 && (fromFloat || IsFloatingPoint(Result)));
        }

        private static bool IsFloatingPoint(SpecialType type) => type is SpecialType.System_Single or SpecialType.System_Double;
    }
}
