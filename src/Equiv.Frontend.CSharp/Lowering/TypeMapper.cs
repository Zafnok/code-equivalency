using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Maps Roslyn types to IR types (ticket M2-003, ADR 0013): <c>sbyte/byte</c> bv8,
/// <c>short/ushort/char</c> bv16, <c>int/uint</c> bv32, <c>long/ulong</c> bv64, <c>bool</c> Bool,
/// everything else an uninterpreted <see cref="IrSort"/> named by its metadata name.
/// </summary>
internal static class TypeMapper
{
    public static IrType Map(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Boolean => new IrBool(),
        SpecialType.System_SByte or SpecialType.System_Byte => new IrBitVec(8),
        SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char => new IrBitVec(16),
        SpecialType.System_Int32 or SpecialType.System_UInt32 => new IrBitVec(32),
        SpecialType.System_Int64 or SpecialType.System_UInt64 => new IrBitVec(64),
        _ => new IrSort(MetadataName(type)),
    };

    /// <summary>Whether an integral type is signed; signedness lives on IR operations, not IR types.</summary>
    public static bool IsSigned(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64;

    /// <summary><c>Namespace.Outer+Inner`1</c> for named types; the display string for arrays, pointers and type parameters.</summary>
    public static string MetadataName(ITypeSymbol type) => type switch
    {
        INamedTypeSymbol { ContainingType: { } outer } => $"{MetadataName(outer)}+{type.MetadataName}",
        INamedTypeSymbol { ContainingNamespace.IsGlobalNamespace: false } => $"{type.ContainingNamespace.ToDisplayString()}.{type.MetadataName}",
        INamedTypeSymbol => type.MetadataName,
        _ => type.ToDisplayString(),
    };
}
