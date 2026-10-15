using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Which interpolated strings are the concatenation of their parts whichever way they bind (ticket P2-086). The same text
/// binds <c>string.Format</c> on .NET Framework and <c>DefaultInterpolatedStringHandler</c> on .NET 6 and later. Both
/// append a <c>string</c> hole as it is, null as nothing, and format a hole of an 8- to 64-bit integer type as its
/// parameterless <c>ToString()</c> does, with the current culture. They differ in when: <c>string.Format</c> formats
/// after every hole is evaluated, the handler formats each hole before the next one is evaluated. So a string is covered
/// when every hole is a <c>string</c> or such an integer with no format or alignment clause, and every hole after the
/// first integer hole only reads a local, a parameter, a constant or a field of <c>this</c>: with no call and no throw
/// after the first formatting, the two orders are one.
/// <para>
/// A hole of a sealed class that does not implement <c>IFormattable</c> is covered too (ticket P2-139). Its value is
/// exactly that class or null, so neither binding finds an <c>IFormattable</c> or an <c>ISpanFormattable</c> to format it
/// through: both append nothing for null and otherwise what the class's own <c>ToString()</c> returns, null as nothing,
/// and neither passes it a culture. That call runs the class's code, which may write the heap, so every hole after the
/// first such hole must be a constant, or a local or parameter passed by value that nothing in the body captures, which no
/// call writes: a field read there would come before the call under <c>string.Format</c> and after it under the handler.
/// </para>
/// Any other hole type is formatted through members the two bindings do not share, or through ones only its run-time
/// type decides, and stays opaque.
/// </summary>
internal static class InterpolatedStrings
{
    /// <summary>Whether <paramref name="interpolated"/> is the concatenation of its parts under both bindings.</summary>
    public static bool IsConcatenation(IInterpolatedStringOperation interpolated)
    {
        IInterpolationOperation[] holes = [.. interpolated.Parts.OfType<IInterpolationOperation>()];
        return holes.All(static h => h.Alignment is null && h.FormatString is null) && AreJoined([.. holes.Select(static h => h.Expression)]);
    }

    /// <summary>
    /// Whether <paramref name="holes"/>, the holes of one string in order, none with a clause, are joined as the
    /// concatenation of the parts under both bindings: each is a <c>string</c>, an integer or a sealed class
    /// <see cref="Formatter"/> names, and the holes after the first integer hole and after the first such class hole are
    /// ones no formatting can tell apart from when they were read.
    /// </summary>
    public static bool AreJoined(IOperation[] holes) =>
        holes.All(static h => IsInteger(h) || h.Type!.SpecialType == SpecialType.System_String || Formatter(h) is not null)
        && holes.SkipWhile(static h => !IsInteger(h)).Skip(1).All(IsRead)
        && holes.SkipWhile(static h => Formatter(h) is null).Skip(1).All(IsUnwritten);

    /// <summary>Whether <paramref name="hole"/> is of an 8- to 64-bit integer type.</summary>
    public static bool IsInteger(IOperation hole) => hole.Type!.SpecialType
        is SpecialType.System_SByte
        or SpecialType.System_Byte
        or SpecialType.System_Int16
        or SpecialType.System_UInt16
        or SpecialType.System_Int32
        or SpecialType.System_UInt32
        or SpecialType.System_Int64
        or SpecialType.System_UInt64;

    /// <summary>
    /// The <c>ToString()</c> both bindings format <paramref name="hole"/> with when it is not null (ticket P2-139): the one
    /// a sealed class that is not <c>IFormattable</c> declares or inherits in <c>object</c>'s slot. Null for any other hole,
    /// and for a class whose nearest parameterless <c>ToString</c> hides that slot, which a written <c>o.ToString()</c>
    /// would call and neither binding does.
    /// </summary>
    public static IMethodSymbol? Formatter(IOperation hole)
    {
        if (hole.Type is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true, SpecialType: not SpecialType.System_String } type
            || type.AllInterfaces.Any(static i => string.Equals(i.Name, nameof(IFormattable), StringComparison.Ordinal)))
        {
            return null;
        }

        IMethodSymbol nearest = Nearest(type);
        IMethodSymbol slot = nearest;
        while (slot.OverriddenMethod is { } overridden)
        {
            slot = overridden;
        }

        return slot.ContainingType.SpecialType == SpecialType.System_Object ? nearest : null;
    }

    /// <summary>The parameterless <c>ToString</c> nearest to <paramref name="type"/>: its own, or its nearest base type's; <c>object</c> has one.</summary>
    private static IMethodSymbol Nearest(INamedTypeSymbol type) =>
        type.GetMembers(nameof(ToString)).OfType<IMethodSymbol>().FirstOrDefault(static m => m.Parameters.IsEmpty) ?? Nearest(type.BaseType!);

    /// <summary>
    /// Whether <paramref name="hole"/> reads what no call writes: a constant, or a local or parameter that is passed by
    /// value and that no lambda, local function or query of its body names, since a call may run one of those. A
    /// parameter of a primary constructor is the type's state, which any of its methods may write, and is not one.
    /// </summary>
    private static bool IsUnwritten(IOperation hole) => hole switch
    {
        { ConstantValue.HasValue: true } => true,
        ILocalReferenceOperation { Local: { RefKind: RefKind.None } local } => IsUncaptured(local),
        IParameterReferenceOperation { Parameter: { RefKind: RefKind.None } parameter } => IsUncaptured(parameter),
        _ => false,
    };

    /// <summary>
    /// Whether no lambda, anonymous method, local function or query in the body that declares <paramref name="variable"/>
    /// has an identifier of its name, and that body is no type declaration, which is what declares a primary constructor's parameter.
    /// </summary>
    private static bool IsUncaptured(ISymbol variable) =>
        variable.ContainingSymbol.DeclaringSyntaxReferences.Select(static r => r.GetSyntax()).All(body =>
            body is not TypeDeclarationSyntax
            && !body.DescendantNodes().Any(n =>
                n is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or QueryExpressionSyntax
                && n.DescendantTokens().Any(t => string.Equals(t.ValueText, variable.Name, StringComparison.Ordinal))));

    /// <summary>Whether evaluating <paramref name="hole"/> calls nothing and cannot throw.</summary>
    public static bool IsRead(IOperation hole) =>
        hole.ConstantValue.HasValue || hole is ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation { Instance: IInstanceReferenceOperation };
}
