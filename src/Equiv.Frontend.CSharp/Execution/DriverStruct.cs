using System.Collections.Immutable;

using Equiv.Core.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// A flat struct a driver reads and builds field by field (tickets P2-242 and P2-246): a non-generic, non-<c>ref</c> struct
/// with no special type and at least one instance field, each of which is either a field a friend of its project can read
/// (public, internal or protected internal) or the backing field of an auto-property whose getter a friend can call. Each
/// is of a kind the driver builds from a scalar wire value (<c>bool</c> through <c>string</c>, or an enum) or is itself a
/// flat struct, to any depth without a cycle. Its comparable form is its fields' canonical forms, in declaration order, a
/// backing field read through its property's getter and a nested struct as a nested array. It is built, as a parameter,
/// by an object initializer when every field is writable and every property has a visible, non-<c>init</c> setter, else
/// by a visible constructor whose parameters are its fields and properties, in order, by type and by name ignoring case
/// (<c>float2(float x, float y)</c> for <c>X, Y</c>); with neither, or with a nested struct that is not built, it is read
/// but not built. A field may also be a one-dimensional array or another reference type (ticket P2-245), which the driver's
/// value builder reads from its wire form: such a struct is built, by the same rule, but has no comparable form, so a
/// result of it is not compared. A property with a body is never a field.
/// </summary>
/// <param name="Fields">The instance fields, in declaration order, each by the name a driver reads and sets it by.</param>
/// <param name="Constructor">The constructor that builds it from its fields; null when an object initializer does, or nothing can.</param>
/// <param name="Buildable">Whether a driver can build it as an argument.</param>
internal sealed record DriverStruct(IReadOnlyList<DriverStruct.Field> Fields, IMethodSymbol? Constructor, bool Buildable)
{
    /// <summary>Whether a result of it has a comparable form: every field is a scalar kind or a comparable struct.</summary>
    public bool Comparable { get; init; } = true;

    private const string BackingSuffix = ">k__BackingField";

    /// <summary>One instance field: a visible field by its own name, or an auto-property's backing field by the property's.</summary>
    internal sealed record Field(string Name, ITypeSymbol Type);

    /// <summary>A field, whether an object initializer can set it, whether a driver can build its value, and whether it compares.</summary>
    private sealed record Member(Field Field, bool Settable, bool Buildable, bool Comparable = true);

    /// <summary><paramref name="type"/> as a flat struct, or null when it is not one.</summary>
    public static DriverStruct? Of(ITypeSymbol type) => Of(type, ImmutableHashSet.Create<ITypeSymbol>(SymbolEqualityComparer.Default));

    /// <summary><paramref name="type"/> as a flat struct, or null when it is not one or is one of the <paramref name="enclosing"/> structs.</summary>
    private static DriverStruct? Of(ITypeSymbol type, ImmutableHashSet<ITypeSymbol> enclosing)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None, IsGenericType: false, IsRefLikeType: false } named
            || ReplayArguments.Hidden(named) is not null
            || enclosing.Contains(named))
        {
            return null;
        }

        ImmutableHashSet<ITypeSymbol> inner = enclosing.Add(named);
        List<Member> members = [];
        foreach (IFieldSymbol field in named.GetMembers().OfType<IFieldSymbol>().Where(static f => !f.IsStatic && !f.IsConst))
        {
            if (Read(named, field, inner) is not { } member)
            {
                return null;
            }

            members.Add(member);
        }

        if (members.Count == 0)
        {
            return null;
        }

        Field[] fields = [.. members.Select(static m => m.Field)];
        bool comparable = members.TrueForAll(static m => m.Comparable);
        if (!members.TrueForAll(static m => m.Buildable))
        {
            return new DriverStruct(fields, Constructor: null, Buildable: false) { Comparable = comparable };
        }

        if (members.TrueForAll(static m => m.Settable))
        {
            return new DriverStruct(fields, Constructor: null, Buildable: true) { Comparable = comparable };
        }

        IMethodSymbol? constructor = named.InstanceConstructors.FirstOrDefault(c => ReplayArguments.Hidden(c) is null && Builds(c, fields));
        return new DriverStruct(fields, constructor, Buildable: constructor is not null) { Comparable = comparable };
    }

    /// <summary>
    /// <paramref name="field"/> as a driver reads it: by its own name when it is visible, else by its auto-property's when
    /// it is a backing field whose getter is visible; null when it is neither, or of a kind a driver cannot read.
    /// </summary>
    private static Member? Read(INamedTypeSymbol owner, IFieldSymbol field, ImmutableHashSet<ITypeSymbol> enclosing) =>
        ReplayArguments.Hidden(field) is null
            ? Kind(field.Name, field.Type, settable: !field.IsReadOnly, enclosing)
            : Backed(owner, field, enclosing);

    /// <summary>A hidden <paramref name="field"/> by its auto-property's name, when it backs one whose getter is visible; else null.</summary>
    private static Member? Backed(INamedTypeSymbol owner, IFieldSymbol field, ImmutableHashSet<ITypeSymbol> enclosing) =>
        Property(owner, field) is { GetMethod: { } getter } property && ReplayArguments.Hidden(getter) is null && IsAuto(getter)
            ? Kind(property.Name, field.Type, settable: property.SetMethod is { IsInitOnly: false } setter && ReplayArguments.Hidden(setter) is null, enclosing)
            : null;

    /// <summary>
    /// A field named <paramref name="name"/> of <paramref name="type"/>: a scalar kind, a flat struct, or (built, never
    /// compared) a one-dimensional array or another reference type a driver can name; else null.
    /// </summary>
    private static Member? Kind(string name, ITypeSymbol type, bool settable, ImmutableHashSet<ITypeSymbol> enclosing) => DriverFactory.Classify(type) switch
    {
        >= ExecutionTypeKind.Boolean and <= ExecutionTypeKind.Enum => new Member(new Field(name, type), settable, Buildable: true),
        _ when Of(type, enclosing) is { } nested => new Member(new Field(name, type), settable, nested.Buildable, nested.Comparable),
        _ when type is IArrayTypeSymbol { IsSZArray: true } or INamedTypeSymbol { IsReferenceType: true } && DriverHeap.Nameable(type)
            => new Member(new Field(name, type), settable, Buildable: true, Comparable: false),
        _ => null,
    };

    /// <summary>
    /// The property <paramref name="field"/> backs: its associated symbol in source, or, from metadata, which keeps no such
    /// link, the property the C# compiler's <c>&lt;Name&gt;k__BackingField</c> names.
    /// </summary>
    private static IPropertySymbol? Property(INamedTypeSymbol owner, IFieldSymbol field) =>
        field.AssociatedSymbol as IPropertySymbol
        ?? (field.Name.StartsWith('<') && field.Name.EndsWith(BackingSuffix, StringComparison.Ordinal)
            ? owner.GetMembers(field.Name[1..^BackingSuffix.Length]).OfType<IPropertySymbol>().FirstOrDefault()
            : null);

    /// <summary>
    /// Whether <paramref name="getter"/> only returns its backing field: in source, every declaration of it is an accessor
    /// with no body (<c>get;</c>, not <c>get =&gt; field * 2;</c>); from metadata, the compiler marked it generated.
    /// </summary>
    private static bool IsAuto(IMethodSymbol getter) => getter.DeclaringSyntaxReferences.IsEmpty
        ? getter.GetAttributes().Any(static a => string.Equals(a.AttributeClass!.ToDisplayString(), "System.Runtime.CompilerServices.CompilerGeneratedAttribute", StringComparison.Ordinal))
        : getter.DeclaringSyntaxReferences.All(static r => r.GetSyntax() is AccessorDeclarationSyntax { Body: null, ExpressionBody: null });

    /// <summary>Whether <paramref name="constructor"/>'s parameters are <paramref name="fields"/>, in order, by type and by name ignoring case.</summary>
    private static bool Builds(IMethodSymbol constructor, Field[] fields) =>
        constructor.Parameters.Length == fields.Length
        && constructor.Parameters.Zip(fields).All(static p =>
            p.First.RefKind == RefKind.None
            && SymbolEqualityComparer.Default.Equals(p.First.Type, p.Second.Type)
            && string.Equals(p.First.Name, p.Second.Name, StringComparison.OrdinalIgnoreCase));
}
