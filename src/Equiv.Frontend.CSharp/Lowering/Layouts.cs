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
/// so that sharing it is decided by that fingerprint. Both read the rule here, so the two cannot differ. The same holds
/// for a reference that hands a generic member of the solution a type (tickets P2-151 and P2-154): the member's code may
/// do any of these operations on its type parameter, and its own text is the same for every type argument.
/// </summary>
internal static class Layouts
{
    /// <summary>The reason of the opaque a lowered body has for a call that is handed a type, or for storage of an explicit layout.</summary>
    public const string Reason = "Layout";

    public const string DisableRuntimeMarshalling = "System.Runtime.CompilerServices.DisableRuntimeMarshallingAttribute";

    private const string FieldOffset = "System.Runtime.InteropServices.FieldOffsetAttribute";

    private const string StructLayout = "System.Runtime.InteropServices.StructLayoutAttribute";

    private const string InlineArray = "System.Runtime.CompilerServices.InlineArrayAttribute";

    /// <summary>
    /// The namespaces whose members are handed a type and read its layout. They are named whole so that no list of
    /// members has to be kept complete.
    /// </summary>
    private static readonly ImmutableArray<string> InteropServices = ["System.Runtime.InteropServices", "System.Runtime.CompilerServices"];

    /// <summary>
    /// The types whose declaration fixes what <paramref name="operation"/> itself does: the type of a field, an
    /// auto-property or a field-like event that is storage of an explicit layout (<see cref="IsOverlaid"/>; ticket
    /// P2-153 for the last two, whose backing field the bound tree does not name), the operand of a <c>sizeof</c>,
    /// every type under a call that is handed one to read as memory, and the type of an operation that is a pointer, a
    /// function pointer or an inline array.
    /// </summary>
    public static ImmutableArray<ITypeSymbol> Reached(IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        IEnumerable<ITypeSymbol?> reached = operation switch
        {
            ISizeOfOperation o => [o.TypeOperand],
            IMemberReferenceOperation o when IsOverlaid(o.Member) => [o.Member.ContainingType],
            _ when Handed(operation) is { } callee =>
                [callee.ContainingType, .. callee.TypeArguments, .. operation.DescendantsAndSelf().SelectMany(static o => (ITypeSymbol?[])[o.Type, (o as ITypeOfOperation)?.TypeOperand])],
            _ when IsPointer(operation.Type) || (operation.Type is INamedTypeSymbol named && Has(named, InlineArray)) => [operation.Type],
            _ => [],
        };
        return [.. reached.OfType<ITypeSymbol>()];
    }

    /// <summary>
    /// The type arguments <paramref name="operation"/> hands code of the solution (ADR 0024 as clarified by ticket
    /// P2-151): for a call, an object creation or a reference to a member that a type of the solution declares, a
    /// method's own, and those of the type that declares the member and of the types that one is nested in. A generic
    /// member's text is the same for every type argument, so what it reads of one's layout is in the text of the body
    /// that names the argument. A member of a type from a reference hands the solution nothing. A constructor also hands
    /// over what its type's base list does (ticket P2-152): creating the type runs the code it inherits, and no body
    /// names the type arguments of its base types and interfaces. A call is asked through <paramref name="called"/>, the
    /// method it calls: a forwarder's target where the caller reads one (ADR 0047).
    /// </summary>
    public static ImmutableArray<ITypeSymbol> TypeArguments(IOperation operation, Func<IMethodSymbol, IMethodSymbol> called)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(called);
        ISymbol? member = operation switch
        {
            IInvocationOperation o => called(o.TargetMethod),
            IMemberReferenceOperation o => o.Member,
            IObjectCreationOperation o => o.Constructor,
            _ => null,
        };
        List<ITypeSymbol> handed = [];
        if (member?.ContainingType is { DeclaringSyntaxReferences.IsEmpty: false } declaring)
        {
            handed.AddRange((member as IMethodSymbol)?.TypeArguments ?? []);
            IEnumerable<INamedTypeSymbol> inherited = member is IMethodSymbol { MethodKind: MethodKind.Constructor } ? [.. BaseTypes(declaring), .. declaring.AllInterfaces] : [];
            handed.AddRange(Nesting(declaring).Concat(Arguments(inherited)));
        }

        return [.. handed];
    }

    /// <summary>
    /// The type arguments that the interface lists of the value types <paramref name="types"/> are, or are made of (an
    /// array's element, a type argument), hand generic interfaces of the solution, and the types those are nested in
    /// (ADR 0024 as clarified by ticket P2-157). A struct is made with no constructor, by a <c>default</c>, as an
    /// array's element or in a field never assigned, so no reference to a constructor holds what its interface list
    /// hands over (ticket P2-152), and the code it inherits from an interface runs on a value wherever there is one.
    /// A class is made by a constructor only, and hands nothing over here.
    /// </summary>
    public static ImmutableArray<ITypeSymbol> Implemented(IEnumerable<ITypeSymbol?> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return [.. Arguments(types.SelectMany(Named).Where(static type => type.IsValueType).SelectMany(static type => type.AllInterfaces))];
    }

    /// <summary>
    /// Whether <paramref name="operation"/>'s type is, or is made of, a struct whose interface list hands a generic
    /// interface of the solution (<see cref="Implemented"/>) a type whose layout the interface's code can read through
    /// its type parameter (<see cref="IsLaidOut"/>). The value such an operation yields runs that code wherever it is
    /// boxed or handed on, and its sort names the struct alone, so it is not one value for both sides of a pair (ticket
    /// P2-161). The fingerprint follows the operation with the declaration of every type of the solution the interface
    /// list hands over, so this holds only where the fingerprint has the declaration. A struct handed over as a type
    /// argument is itself a type with a layout, which <see cref="HandsOver"/> already says.
    /// </summary>
    public static bool Inherits(IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Implemented([operation.Type]).Any(IsLaidOut);
    }

    /// <summary>The named types <paramref name="type"/> is, or is made of: an array's element, and a type argument.</summary>
    private static IEnumerable<INamedTypeSymbol> Named(ITypeSymbol? type) => type switch
    {
        IArrayTypeSymbol array => Named(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.SelectMany(Named).Prepend(named),
        _ => [],
    };

    /// <summary>The type arguments of each of <paramref name="types"/> that the solution declares, and of the types it is nested in.</summary>
    private static IEnumerable<ITypeSymbol> Arguments(IEnumerable<INamedTypeSymbol> types) =>
        types.Where(static type => !type.DeclaringSyntaxReferences.IsEmpty).SelectMany(Nesting);

    /// <summary>The type arguments of <paramref name="type"/> and of the types it is nested in.</summary>
    private static IEnumerable<ITypeSymbol> Nesting(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? outer = type; outer is not null; outer = outer.ContainingType)
        {
            foreach (ITypeSymbol argument in outer.TypeArguments)
            {
                yield return argument;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? inherited = type.BaseType; inherited is not null; inherited = inherited.BaseType)
        {
            yield return inherited;
        }
    }

    /// <summary>
    /// Whether <paramref name="operation"/> hands a generic member of the solution (<see cref="TypeArguments"/>) a type
    /// whose layout the member's code can read through its type parameter. Such a call is not one function for both
    /// sides of a pair, and such a field is not one map (ticket P2-154). The fingerprint follows the operation with the
    /// declaration of every type of the solution it hands over; this asks for one that has a layout to read
    /// (<see cref="IsLaidOut"/>), so it holds only where the fingerprint has the declaration.
    /// </summary>
    public static bool HandsOver(IOperation operation, Func<IMethodSymbol, IMethodSymbol> called) => TypeArguments(operation, called).Any(IsLaidOut);

    /// <summary>
    /// Whether code that has <paramref name="type"/> as a type argument can read a layout the solution declares: the
    /// type is, or is made of, a value type of the solution or a class of the solution that has a
    /// <c>[StructLayout]</c>. A size, an element's stride and a marshalled size are read through a type parameter
    /// (<c>sizeof(T)</c>, <c>Unsafe.SizeOf&lt;T&gt;()</c>, a <c>T[]</c>, <c>Marshal.SizeOf&lt;T&gt;()</c>). Of any other
    /// class they are the size of a reference, or are refused by the marshaller, whatever the class declares, and so
    /// for an interface, a delegate, an anonymous type and a type parameter.
    /// A tuple is a type from a reference, laid out as its elements are, whose syntax Roslyn gives as its declaration.
    /// </summary>
    public static bool IsLaidOut(ITypeSymbol type) =>
        IsMadeOf(type, static named => !named.IsTupleType && !named.DeclaringSyntaxReferences.IsEmpty && (named.IsValueType || Has(named, StructLayout)));

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
    /// event of a type whose <c>[StructLayout]</c> is explicit. Every instance field of an explicit layout has the
    /// attribute, and no other field may; an event's backing field is no symbol of its type, so the type is asked.
    /// </summary>
    public static bool IsOverlaid(ISymbol member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member switch
        {
            IFieldSymbol field => Has(field, FieldOffset),
            IPropertySymbol property => HeapLowerer.BackingField(property) is { } backing && Has(backing, FieldOffset),
            IEventSymbol raised => IsStored(raised) && IsExplicit(raised.ContainingType),
            _ => false,
        };
    }

    /// <summary>
    /// Whether <paramref name="raised"/> is a field-like instance event: the compiler writes its accessors and stores its
    /// delegate in a backing field of each object, which no symbol of the type is.
    /// </summary>
    public static bool IsStored(IEventSymbol raised)
    {
        ArgumentNullException.ThrowIfNull(raised);
        return !raised.IsStatic && !raised.IsAbstract && raised.AddMethod!.IsImplicitlyDeclared;
    }

    /// <summary>
    /// Whether the fingerprint writes a declaration for <paramref name="type"/>: it is, or is made of, a type declared
    /// in the solution, through an array, a pointer, a function pointer's signature or a type argument. A type from a
    /// reference is its name alone.
    /// </summary>
    public static bool IsDeclared(ITypeSymbol type) => IsMadeOf(type, static named => !named.DeclaringSyntaxReferences.IsEmpty);

    /// <summary>Whether <paramref name="type"/> is, or is made of, a named type that <paramref name="holds"/>.</summary>
    private static bool IsMadeOf(ITypeSymbol type, Func<INamedTypeSymbol, bool> holds) => type switch
    {
        IArrayTypeSymbol array => IsMadeOf(array.ElementType, holds),
        IPointerTypeSymbol pointer => IsMadeOf(pointer.PointedAtType, holds),
        IFunctionPointerTypeSymbol function => IsMadeOf(function.Signature.ReturnType, holds) || function.Signature.Parameters.Any(p => IsMadeOf(p.Type, holds)),
        INamedTypeSymbol named => holds(named) || named.TypeArguments.Any(argument => IsMadeOf(argument, holds)),
        _ => false,
    };

    /// <summary>Whether <paramref name="type"/> has a <c>[StructLayout(LayoutKind.Explicit)]</c>, as the compiler reads one before it allows a <c>[FieldOffset]</c>.</summary>
    private static bool IsExplicit(INamedTypeSymbol type) => type.GetAttributes().Any(static a =>
        string.Equals(a.AttributeClass!.ToDisplayString(), StructLayout, StringComparison.Ordinal)
        && a.ConstructorArguments.Any(static kind => kind.Value is (int)System.Runtime.InteropServices.LayoutKind.Explicit));

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
