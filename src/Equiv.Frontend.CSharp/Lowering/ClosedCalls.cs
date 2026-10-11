using System.Collections.Frozen;
using System.Collections.Immutable;

using Equiv.Core;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Which calls reach no heap map (ADR 0041; ticket P2-060). A call is closed when its callee's containing type, every
/// parameter type (<c>ref</c>, <c>out</c> and <c>in</c> included) and every type argument of the method are inert:
/// <c>bool</c>, <c>char</c>, the 8- to 64-bit integers, <c>float</c>, <c>double</c>, <c>decimal</c>, <c>string</c>, an
/// enum, or <c>Nullable&lt;T&gt;</c> of an inert <c>T</c>. An inert value holds no reference to a mutable object and
/// runs no user method, so a closed callee is handed nothing that leads to the program's fields or arrays, and its
/// containing type stores no callback it could run later. Both lowerings ask this one rule.
/// <para>
/// The solver chooses a closed call's answer, and the replay taints it (ADR 0056; ticket P2-081). A side would then
/// branch on a tainted <c>threw</c> flag after every closed call. <see cref="NeverThrows"/> is the short list of closed
/// members that throw for no argument, a non-null receiver given: a call to one has no <c>threw</c> flag.
/// </para>
/// </summary>
internal static class ClosedCalls
{
    /// <summary>Whether a call to <paramref name="method"/> reaches no heap map.</summary>
    public static bool IsClosed(IMethodSymbol method) =>
        IsInert(method.ContainingType) && method.Parameters.Select(static p => p.Type).Concat(method.TypeArguments).All(IsInert);

    /// <summary>
    /// Whether a call to <paramref name="callee"/> has no <c>threw</c> flag (ADR 0056): the member is on the list, by its
    /// identity, and throws for no argument. It is read from the identity and not the symbol, so that a call an
    /// API-equivalence entry rewrites to a listed member (ADR 0020) and a direct call to that member agree.
    /// <c>String.Concat</c> of two to four strings reads a null as empty. The case and trim members take nothing that can
    /// be out of range; the overloads that take a <c>CultureInfo</c> or a <c>char[]</c> are not listed.
    /// <c>String.IsNullOrEmpty</c> and <c>IsNullOrWhiteSpace</c> test their argument. The parameterless <c>ToString</c>
    /// of <c>bool</c>, <c>char</c> and an integer formats a value that always has a text. Of <c>Nullable&lt;T&gt;</c>, the
    /// constructor, <c>HasValue</c> and <c>GetValueOrDefault</c> store or read a field; <c>Value</c> throws and is not
    /// listed.
    /// </summary>
    public static bool NeverThrows(CallIdentity callee) =>
        NeverThrowing.Contains(callee.Value) || NullableNeverThrowing.Any(member => callee.Value.StartsWith(member, StringComparison.Ordinal));

    private static readonly FrozenSet<string> NeverThrowing = FrozenSet.ToFrozenSet(
        [
            "System.String::Concat(string,string)",
            "System.String::Concat(string,string,string)",
            "System.String::Concat(string,string,string,string)",
            "System.String::IsNullOrEmpty(string)",
            "System.String::IsNullOrWhiteSpace(string)",
            "System.String::ToUpper()",
            "System.String::ToLower()",
            "System.String::ToUpperInvariant()",
            "System.String::ToLowerInvariant()",
            "System.String::Trim()",
            "System.String::Trim(char)",
            "System.String::TrimStart()",
            "System.String::TrimStart(char)",
            "System.String::TrimEnd()",
            "System.String::TrimEnd(char)",
            "System.Boolean::ToString()",
            "System.Char::ToString()",
            "System.SByte::ToString()",
            "System.Byte::ToString()",
            "System.Int16::ToString()",
            "System.UInt16::ToString()",
            "System.Int32::ToString()",
            "System.UInt32::ToString()",
            "System.Int64::ToString()",
            "System.UInt64::ToString()",
        ],
        StringComparer.Ordinal);

    /// <summary>The listed members of <c>Nullable&lt;T&gt;</c>, each followed in an identity by its type argument.</summary>
    private static readonly ImmutableArray<string> NullableNeverThrowing =
    [
        "System.Nullable`1::get_HasValue()<",
        "System.Nullable`1::GetValueOrDefault(",
        "System.Nullable`1::.ctor(",
    ];

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
