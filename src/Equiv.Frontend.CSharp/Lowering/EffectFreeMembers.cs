using System.Collections.Frozen;

using Equiv.Core;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The catalogue of BCL members that run no observable code (ADR 0043; ticket P2-071). A use of one is no call: it has no
/// trace event, no <c>threw</c> flag, no heap pair and no position. A member is in it only by name:
/// <list type="bullet">
/// <item>a getter of an immutable value, <c>System.String::get_Length()</c>, which is the pure function
/// <see cref="Getter"/> of its receiver and raises nothing;</item>
/// <item>the parameterless constructor of a listed collection type declared in metadata, which only allocates: the new
/// object is the next of <c>new.&lt;Sort&gt;</c>, as a new array's reference is. A constructor with an argument is not in
/// the catalogue.</item>
/// </list>
/// <c>List&lt;T&gt;</c> and <c>Collection&lt;T&gt;</c> are one family (<see cref="IsNewList"/>): created and converted to
/// an interface at once, both are a new <c>List&lt;T&gt;</c>. Both lowerings ask this one catalogue.
/// </summary>
internal static class EffectFreeMembers
{
    /// <summary>The name prefix of a catalogued getter's function.</summary>
    public const string GetterPrefix = "get:";

    /// <summary>The metadata name of <c>List&lt;T&gt;</c>, the sort a family member converted to an interface is created as.</summary>
    public const string List = "System.Collections.Generic.List`1";

    private const string Collection = "System.Collections.ObjectModel.Collection`1";

    private static readonly FrozenSet<string> Allocating = FrozenSet.ToFrozenSet(
        [
            List,
            "System.Collections.Generic.Dictionary`2",
            "System.Collections.Generic.HashSet`1",
            "System.Collections.Generic.Queue`1",
            "System.Collections.Generic.Stack`1",
            "System.Collections.Generic.LinkedList`1",
            "System.Collections.Generic.SortedDictionary`2",
            "System.Collections.Generic.SortedList`2",
            "System.Collections.Generic.SortedSet`1",
            Collection,
            "System.Collections.Concurrent.ConcurrentBag`1",
            "System.Collections.Concurrent.ConcurrentDictionary`2",
            "System.Collections.Concurrent.ConcurrentQueue`1",
            "System.Collections.Concurrent.ConcurrentStack`1",
        ],
        StringComparer.Ordinal);

    /// <summary>Whether <paramref name="method"/> is a catalogued getter: a function of its receiver's value alone that never throws.</summary>
    public static bool IsGetter(IMethodSymbol method) =>
        method is { MethodKind: MethodKind.PropertyGet, Name: "get_Length", ContainingType.SpecialType: SpecialType.System_String };

    /// <summary>The pure function of the catalogued getter <paramref name="identity"/>: <c>get:</c> followed by its call identity.</summary>
    public static string Getter(CallIdentity identity) => GetterPrefix + identity.Value;

    /// <summary>Whether <paramref name="constructor"/> is a catalogued constructor: parameterless, of a listed type that no source declares.</summary>
    public static bool Allocates(IMethodSymbol constructor) =>
        constructor.Parameters.IsEmpty
        && constructor.ContainingType.OriginalDefinition.DeclaringSyntaxReferences.IsEmpty
        && Allocating.Contains(TypeMapper.MetadataName(constructor.ContainingType));

    /// <summary>
    /// Whether a new object made by <paramref name="constructor"/> and converted to <paramref name="target"/> at once is a
    /// new <c>List&lt;T&gt;</c> there: the catalogued constructor of <c>Collection&lt;T&gt;</c>, converted to an interface.
    /// This assumes no code asks the object for its concrete type (VERIFICATION-MODEL section 1).
    /// </summary>
    public static bool IsNewList(IMethodSymbol constructor, ITypeSymbol target) =>
        target.TypeKind == TypeKind.Interface
        && Allocates(constructor)
        && string.Equals(TypeMapper.MetadataName(constructor.ContainingType), Collection, StringComparison.Ordinal);
}
