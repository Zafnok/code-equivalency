using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Fingerprinting;

/// <summary>
/// Whether an imported function's return value or a parameter is marshalled as a COM or WinRT object, the reach of the
/// <c>marshalling</c> entries that changed COM and WinRT interop (ADR 0058 decision 3; ticket P2-165). It errs wide: every
/// interface and <c>object</c> counts, though only some go through COM.
/// </summary>
internal static class ComMarshalling
{
    private const string MarshalAs = "System.Runtime.InteropServices.MarshalAsAttribute";

    /// <summary>
    /// The <c>UnmanagedType</c> values that marshal a COM or WinRT object: <c>IUnknown</c>, <c>IDispatch</c>, <c>Struct</c>
    /// (a VARIANT), <c>Interface</c>, <c>SafeArray</c>, <c>AsAny</c>, <c>IInspectable</c> and <c>HString</c>.
    /// </summary>
    private static readonly ImmutableHashSet<int> ComTypes = [25, 26, 27, 28, 29, 40, 46, 47];

    /// <summary>
    /// Whether <paramref name="function"/> marshals a COM or WinRT object: a <c>[MarshalAs]</c> on its return value or a
    /// parameter names one, or the type of either is, or is made of, one (<see cref="IsMadeOfComObject"/>).
    /// </summary>
    public static bool Reaches(IMethodSymbol function)
    {
        HashSet<ITypeSymbol> seen = new(SymbolEqualityComparer.Default);
        return Signature(function).Any(part => NamesComType(part.Attributes) || IsMadeOfComObject(part.Type, seen));
    }

    /// <summary>The return value and the parameters of <paramref name="function"/>, each with its type and its attributes.</summary>
    private static IEnumerable<(ITypeSymbol Type, ImmutableArray<AttributeData> Attributes)> Signature(IMethodSymbol function) =>
        [(function.ReturnType, function.GetReturnTypeAttributes()), .. function.Parameters.Select(static p => (p.Type, p.GetAttributes()))];

    /// <summary>
    /// Whether <paramref name="type"/> is marshalled as a COM or WinRT object, or holds one: an interface, <c>object</c>,
    /// <c>dynamic</c> or a <c>[ComImport]</c> class; an array or a pointer of one; a type that one is a type argument of; a
    /// delegate whose signature marshals one; and a type the solution declares whose base type, instance field or
    /// field-like event is one or carries a <c>[MarshalAs]</c> that names one. A type from a reference other than these
    /// is not followed into its fields: what it holds is outside both solutions (ticket P2-146).
    /// </summary>
    private static bool IsMadeOfComObject(ITypeSymbol type, HashSet<ITypeSymbol> seen) => type switch
    {
        { SpecialType: SpecialType.System_Object } or { TypeKind: TypeKind.Interface or TypeKind.Dynamic } or INamedTypeSymbol { IsComImport: true } => true,
        IArrayTypeSymbol array => IsMadeOfComObject(array.ElementType, seen),
        IPointerTypeSymbol pointer => IsMadeOfComObject(pointer.PointedAtType, seen),
        INamedTypeSymbol named when seen.Add(named) =>
            named.TypeArguments.Any(argument => IsMadeOfComObject(argument, seen))
            || (named.DelegateInvokeMethod is { } invoke && Signature(invoke).Any(part => NamesComType(part.Attributes) || IsMadeOfComObject(part.Type, seen)))
            || (!named.DeclaringSyntaxReferences.IsEmpty && Members(named).Any(part => NamesComType(part.Attributes) || IsMadeOfComObject(part.Type, seen))),
        _ => false,
    };

    /// <summary>
    /// The parts of a type the solution declares that are marshalled with it: its base type when the solution declares
    /// that too (every class's chain ends in <c>object</c>, which is not marshalled as a field), its instance fields with
    /// their attributes, and its field-like instance events.
    /// </summary>
    private static IEnumerable<(ITypeSymbol Type, ImmutableArray<AttributeData> Attributes)> Members(INamedTypeSymbol type) =>
    [
        .. ((INamedTypeSymbol?[])[type.BaseType]).OfType<INamedTypeSymbol>().Where(static b => !b.DeclaringSyntaxReferences.IsEmpty).Select(static b => ((ITypeSymbol)b, ImmutableArray<AttributeData>.Empty)),
        .. type.GetMembers().OfType<IFieldSymbol>().Where(static f => !f.IsStatic).Select(static f => (f.Type, f.GetAttributes())),
        .. type.GetMembers().OfType<IEventSymbol>().Where(Layouts.IsStored).Select(static e => (e.Type, ImmutableArray<AttributeData>.Empty)),
    ];

    /// <summary>Whether one of <paramref name="attributes"/> is a <c>[MarshalAs]</c> whose type or array subtype is a COM or WinRT object.</summary>
    private static bool NamesComType(ImmutableArray<AttributeData> attributes) =>
        attributes.Any(static attribute =>
            string.Equals(attribute.AttributeClass!.ToDisplayString(), MarshalAs, StringComparison.Ordinal)
            && attribute.ConstructorArguments.Concat(attribute.NamedArguments.Where(static n => n.Key is "ArraySubType").Select(static n => n.Value))
                .Any(static value => ComTypes.Contains(Convert.ToInt32(value.Value, CultureInfo.InvariantCulture))));
}
