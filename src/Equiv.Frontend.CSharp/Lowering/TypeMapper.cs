using System.Globalization;

using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Maps Roslyn types to IR types (ticket M2-003, ADR 0013): <c>sbyte/byte</c> bv8,
/// <c>short/ushort/char</c> bv16, <c>int/uint</c> bv32, <c>long/ulong</c> bv64, <c>bool</c> Bool,
/// everything else an uninterpreted <see cref="IrSort"/> named by its metadata name. The overloads taking a
/// <c>sorts</c> function pass each sort name through it: on the legacy side that maps an API-equivalence type entry's
/// legacy name to its modern one (ADR 0020; ticket M3-009); elsewhere it is the identity.
/// </summary>
internal static class TypeMapper
{
    /// <summary>The sort-name function that leaves every name as it is.</summary>
    public static readonly Func<string, string> Unmapped = static name => name;

    public static IrType Map(ITypeSymbol type) => Map(type, Unmapped);

    public static IrType Map(ITypeSymbol type, Func<string, string> sorts) => type.SpecialType switch
    {
        SpecialType.System_Boolean => new IrBool(),
        SpecialType.System_SByte or SpecialType.System_Byte => new IrBitVec(8),
        SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char => new IrBitVec(16),
        SpecialType.System_Int32 or SpecialType.System_UInt32 => new IrBitVec(32),
        SpecialType.System_Int64 or SpecialType.System_UInt64 => new IrBitVec(64),
        _ => new IrSort(MetadataName(type, sorts)),
    };

    /// <summary>
    /// C# binary numeric promotion of a single operand (ECMA-334 12.4.7): anything narrower than
    /// <c>int</c> becomes a signed <c>int</c>. Null when <paramref name="type"/> is not integral.
    /// </summary>
    public static (IrBitVec Type, bool Signed)? Promote(ITypeSymbol type) => Map(type) switch
    {
        IrBitVec { Width: < 32 } => (new IrBitVec(32), true),
        IrBitVec bits => (bits, IsSigned(type)),
        _ => null,
    };

    /// <summary>
    /// The IR value of a C# compile-time constant. A constant of an uninterpreted sort (a string, a
    /// floating-point value, an enum member, <c>null</c>) is a designated element of that sort, chosen
    /// by a stable hash of the constant so that equal constants are the same element on both sides;
    /// <c>null</c> is always element 0.
    /// </summary>
    public static IrValue Constant(ITypeSymbol type, object? value) => Constant(type, value, Unmapped);

    /// <summary>As the two-argument overload, with each sort name passed through <paramref name="sorts"/>.</summary>
    public static IrValue Constant(ITypeSymbol type, object? value, Func<string, string> sorts) => Map(type, sorts) switch
    {
        IrBitVec bits when IsSigned(type) => IrBitVecValue.FromSigned(bits.Width, System.Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        IrBitVec bits => new IrBitVecValue(bits.Width, System.Convert.ToUInt64(value, CultureInfo.InvariantCulture)),
        IrBool => new IrBoolValue((bool)value!),
        var sort => new IrSortValue(((IrSort)sort).Name, Element(value)),
    };

    /// <summary>
    /// An API-equivalence adapter constant (ticket M3-009): <paramref name="text"/> of the IR type <paramref name="type"/>,
    /// which is <c>bool</c>, <c>bv</c><i>n</i> (a signed integer), or a sort name. A sort constant is the element
    /// <see cref="Constant(ITypeSymbol, object?)"/> gives a C# constant with the same invariant text, so an enum member
    /// written as its underlying value is the element the modern side's own constant is. Null when the type or the text does not parse.
    /// </summary>
    public static IrValue? Constant(string type, string text) => type switch
    {
        "bool" => bool.TryParse(text, out bool flag) ? new IrBoolValue(flag) : null,
        ['b', 'v', .. string width] => int.TryParse(width, NumberStyles.None, CultureInfo.InvariantCulture, out int bits)
            && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)
                ? IrBitVecValue.FromSigned(bits, value)
                : null,
        _ => new IrSortValue(type, Element(text)),
    };

    /// <summary>FNV-1a over the constant's invariant text; 0 is reserved for <c>null</c>.</summary>
    private static int Element(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        uint hash = 2166136261;
        foreach (char c in string.Create(CultureInfo.InvariantCulture, $"{value}"))
        {
            hash = (hash ^ c) * 16777619;
        }

        return (int)((hash & 0x7FFFFFFF) | 1);
    }

    /// <summary>Whether an integral type is signed; signedness lives on IR operations, not IR types.</summary>
    public static bool IsSigned(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64;

    /// <summary><see cref="MetadataName(ITypeSymbol)"/>, passed through <paramref name="sorts"/>.</summary>
    public static string MetadataName(ITypeSymbol type, Func<string, string> sorts) => sorts(MetadataName(type));

    /// <summary><c>Namespace.Outer+Inner`1</c> for named types; the display string for arrays, pointers and type parameters.</summary>
    public static string MetadataName(ITypeSymbol type) => type switch
    {
        INamedTypeSymbol { ContainingType: { } outer } => $"{MetadataName(outer)}+{type.MetadataName}",
        INamedTypeSymbol { ContainingNamespace.IsGlobalNamespace: false } => $"{type.ContainingNamespace.ToDisplayString()}.{type.MetadataName}",
        INamedTypeSymbol => type.MetadataName,
        _ => type.ToDisplayString(),
    };
}
