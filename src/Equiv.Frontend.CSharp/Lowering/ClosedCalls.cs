using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Which calls reach no heap map (ADR 0041; ticket P2-060). A call is closed when its callee's containing type, every
/// parameter type (<c>ref</c>, <c>out</c> and <c>in</c> included) and every type argument of the method are inert:
/// <c>bool</c>, <c>char</c>, the 8- to 64-bit integers, <c>float</c>, <c>double</c>, <c>decimal</c>, <c>string</c>, an
/// enum, or <c>Nullable&lt;T&gt;</c> of an inert <c>T</c>. An inert value holds no reference to a mutable object and
/// runs no user method, so a closed callee is handed nothing that leads to the program's fields or arrays, and its
/// containing type stores no callback it could run later. Both lowerings ask this one rule.
/// </summary>
internal static class ClosedCalls
{
    /// <summary>Whether a call to <paramref name="method"/> reaches no heap map.</summary>
    public static bool IsClosed(IMethodSymbol method) =>
        IsInert(method.ContainingType) && method.Parameters.Select(static p => p.Type).Concat(method.TypeArguments).All(IsInert);

    private static bool IsInert(ITypeSymbol type) => type switch
    {
        { TypeKind: TypeKind.Enum } => true,
        INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable => IsInert(nullable.TypeArguments[0]),
        _ => type.SpecialType is SpecialType.System_Boolean
            or SpecialType.System_Char
            or SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal
            or SpecialType.System_String,
    };
}
