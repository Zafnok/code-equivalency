using Equiv.Core.Execution;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// A flat struct a driver reads and builds field by field (ticket P2-242): a non-generic, non-<c>ref</c> struct with no
/// special type, at least one instance field, and every instance field one a friend of its project can read (public,
/// internal or protected internal) of a kind the driver builds from a scalar wire value (<c>bool</c> through <c>string</c>,
/// or an enum). Its comparable form is its fields' canonical forms, in declaration order. It is built, as a parameter, by
/// an object initializer when no field is <c>readonly</c>, else by a visible constructor whose parameters are its fields,
/// in order, by type and by name ignoring case (<c>float2(float x, float y)</c> for <c>X, Y</c>); with neither, it is
/// read but not built. A nested struct, an array or another reference-typed field, or a field behind an auto-property is
/// not flat (ticket P2-246).
/// </summary>
/// <param name="Fields">The instance fields, in declaration order.</param>
/// <param name="Constructor">The constructor that builds it from its fields; null when an object initializer does, or nothing can.</param>
/// <param name="Buildable">Whether a driver can build it as an argument.</param>
internal sealed record DriverStruct(IReadOnlyList<IFieldSymbol> Fields, IMethodSymbol? Constructor, bool Buildable)
{
    /// <summary><paramref name="type"/> as a flat struct, or null when it is not one.</summary>
    public static DriverStruct? Of(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None, IsGenericType: false, IsRefLikeType: false } named
            || ReplayArguments.Hidden(named) is not null)
        {
            return null;
        }

        IFieldSymbol[] fields = [.. named.GetMembers().OfType<IFieldSymbol>().Where(static f => !f.IsStatic && !f.IsConst)];
        if (fields.Length == 0 || !fields.All(static f => ReplayArguments.Hidden(f) is null && DriverFactory.Classify(f.Type) is >= ExecutionTypeKind.Boolean and <= ExecutionTypeKind.Enum))
        {
            return null;
        }

        if (!fields.Any(static f => f.IsReadOnly))
        {
            return new DriverStruct(fields, Constructor: null, Buildable: true);
        }

        IMethodSymbol? constructor = named.InstanceConstructors.FirstOrDefault(c => ReplayArguments.Hidden(c) is null && Builds(c, fields));
        return new DriverStruct(fields, constructor, Buildable: constructor is not null);
    }

    /// <summary>Whether <paramref name="constructor"/>'s parameters are <paramref name="fields"/>, in order, by type and by name ignoring case.</summary>
    private static bool Builds(IMethodSymbol constructor, IFieldSymbol[] fields) =>
        constructor.Parameters.Length == fields.Length
        && constructor.Parameters.Zip(fields).All(static p =>
            p.First.RefKind == RefKind.None
            && SymbolEqualityComparer.Default.Equals(p.First.Type, p.Second.Type)
            && string.Equals(p.First.Name, p.Second.Name, StringComparison.OrdinalIgnoreCase));
}
