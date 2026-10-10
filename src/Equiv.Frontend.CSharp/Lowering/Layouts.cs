using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The operations whose result a type's declaration fixes (ADR 0024 as clarified by tickets P2-149 and P2-150): what
/// <c>sizeof(S)</c> yields, where a read through an <c>S*</c> lands, whether a write to one field of an explicit layout
/// changes another, and what a callee that is handed a type reads of it. The bound tree names the type and holds
/// nothing of its declaration, and neither does the IR. The fingerprint writes the declaration after such an
/// operation's line, and the lowering makes one it would otherwise model an opaque with reason <see cref="Reason"/>,
/// so that sharing it is decided by that fingerprint. Both read the rule here, so the two cannot differ.
/// </summary>
internal static class Layouts
{
    /// <summary>The reason of the opaque a lowered body has for a call that is handed a type, or for storage of an explicit layout.</summary>
    public const string Reason = "Layout";

    public const string DisableRuntimeMarshalling = "System.Runtime.CompilerServices.DisableRuntimeMarshallingAttribute";

    private const string FieldOffset = "System.Runtime.InteropServices.FieldOffsetAttribute";

    private const string InlineArray = "System.Runtime.CompilerServices.InlineArrayAttribute";

    /// <summary>
    /// The namespaces whose members are handed a type and read its layout. They are named whole so that no list of
    /// members has to be kept complete.
    /// </summary>
    private static readonly ImmutableArray<string> InteropServices = ["System.Runtime.InteropServices", "System.Runtime.CompilerServices"];

    /// <summary>
    /// The types whose declaration fixes what <paramref name="operation"/> itself does: the type of a field that has a
    /// <c>[FieldOffset]</c>, the operand of a <c>sizeof</c>, every type under a call that is handed one to read as
    /// memory, and the type of an operation that is a pointer, a function pointer or an inline array.
    /// </summary>
    public static ImmutableArray<ITypeSymbol> Reached(IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        IEnumerable<ITypeSymbol?> reached = operation switch
        {
            ISizeOfOperation o => [o.TypeOperand],
            IFieldReferenceOperation o when Has(o.Field, FieldOffset) => [o.Field.ContainingType],
            _ when Handed(operation) is { } callee =>
                [callee.ContainingType, .. callee.TypeArguments, .. operation.DescendantsAndSelf().SelectMany(static o => (ITypeSymbol?[])[o.Type, (o as ITypeOfOperation)?.TypeOperand])],
            _ when IsPointer(operation.Type) || (operation.Type is INamedTypeSymbol named && Has(named, InlineArray)) => [operation.Type],
            _ => [],
        };
        return [.. reached.OfType<ITypeSymbol>()];
    }

    /// <summary>
    /// Whether <paramref name="operation"/> is a call the runtime may marshal: one through a function pointer, or one
    /// into the interop services. The <c>[DisableRuntimeMarshalling]</c> of the body's assembly says whether it does.
    /// </summary>
    public static bool Marshals(IOperation operation) =>
        operation is IFunctionPointerInvocationOperation || (Callee(operation) is { } callee && IsInterop(callee));

    /// <summary>
    /// Whether <paramref name="operation"/> is a call or an object creation that is handed a type whose declaration
    /// the fingerprint writes: one declared in the solution, or, for a call the runtime may marshal, the
    /// <c>[DisableRuntimeMarshalling]</c> of <paramref name="assembly"/>, the body's own. Such a call is not one
    /// function for both sides of a pair (ticket P2-150).
    /// </summary>
    public static bool IsHanded(IOperation operation, IAssemblySymbol assembly)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(assembly);
        return Handed(operation) is not null && (Reached(operation).Any(IsDeclared) || (Marshals(operation) && Has(assembly, DisableRuntimeMarshalling)));
    }

    /// <summary>
    /// Whether what <paramref name="operation"/> itself does is fixed by a declaration: it reads or writes storage of
    /// an explicit layout, or the fingerprint writes a layout line after its line.
    /// </summary>
    public static bool Reads(IOperation operation, IAssemblySymbol assembly)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(assembly);
        return (operation is IMemberReferenceOperation reference && IsOverlaid(reference.Member))
            || Reached(operation).Any(IsDeclared)
            || (Marshals(operation) && Has(assembly, DisableRuntimeMarshalling));
    }

    /// <summary>
    /// Whether <paramref name="member"/> is storage of an explicit layout, which another member of its type may share: a
    /// field that has a <c>[FieldOffset]</c>, an auto-property whose backing field has one, or a field-like instance
    /// event of a type that has such a field. Every instance field of an explicit layout has the attribute, and no
    /// other field may.
    /// </summary>
    public static bool IsOverlaid(ISymbol member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member switch
        {
            IFieldSymbol field => Has(field, FieldOffset),
            IPropertySymbol property => HeapLowerer.BackingField(property) is { } backing && Has(backing, FieldOffset),
            IEventSymbol { IsStatic: false } raised =>
                raised.AddMethod!.IsImplicitlyDeclared && raised.ContainingType.GetMembers().OfType<IFieldSymbol>().Any(static f => Has(f, FieldOffset)),
            _ => false,
        };
    }

    /// <summary>
    /// Whether the fingerprint writes a declaration for <paramref name="type"/>: it is, or is made of, a type declared
    /// in the solution, through an array, a pointer, a function pointer's signature or a type argument. A type from a
    /// reference is its name alone.
    /// </summary>
    public static bool IsDeclared(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => IsDeclared(array.ElementType),
        IPointerTypeSymbol pointer => IsDeclared(pointer.PointedAtType),
        IFunctionPointerTypeSymbol function => IsDeclared(function.Signature.ReturnType) || function.Signature.Parameters.Any(static p => IsDeclared(p.Type)),
        INamedTypeSymbol named => !named.DeclaringSyntaxReferences.IsEmpty || named.TypeArguments.Any(IsDeclared),
        _ => false,
    };

    public static bool IsPointer(ITypeSymbol? type) => type is IPointerTypeSymbol or IFunctionPointerTypeSymbol;

    public static bool Has(ISymbol symbol, string attribute) =>
        symbol.GetAttributes().Any(a => string.Equals(a.AttributeClass!.ToDisplayString(), attribute, StringComparison.Ordinal));

    /// <summary>
    /// The callee of a call or an object creation that is handed a type to read as memory: one declared under the
    /// interop services, or one with a parameter that is a pointer or a function pointer. Null for any other operation.
    /// </summary>
    private static IMethodSymbol? Handed(IOperation operation) =>
        Callee(operation) is { } callee && (IsInterop(callee) || callee.Parameters.Any(static p => IsPointer(p.Type))) ? callee : null;

    private static IMethodSymbol? Callee(IOperation operation) => operation switch
    {
        IInvocationOperation o => o.TargetMethod,
        IObjectCreationOperation o => o.Constructor,
        _ => null,
    };

    private static bool IsInterop(IMethodSymbol callee) =>
        InteropServices.Any(prefix => callee.ContainingNamespace.ToDisplayString().StartsWith(prefix, StringComparison.Ordinal));
}
