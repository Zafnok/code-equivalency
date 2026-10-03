using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The arguments the compiler fills with where a call sits (ADR 0046; ticket P2-098): a <c>string</c> for a
/// <c>[CallerFilePath]</c> parameter, an <c>int</c> for a <c>[CallerLineNumber]</c> one. Such a value differs between two
/// checkouts of one body, so it is lowered and fingerprinted as an input both sides share, not as its constant. An argument
/// the source writes out is not one of these, and neither is <c>[CallerMemberName]</c>, whose value is source text.
/// </summary>
internal static class CallerLocation
{
    private const string Namespace = "System.Runtime.CompilerServices";

    /// <summary>The kind <paramref name="operation"/> is, when it is the value of an argument the compiler supplied; else null.</summary>
    public static CallerLocationKind? Of(IOperation operation) =>
        operation.Parent is IArgumentOperation { ArgumentKind: ArgumentKind.DefaultValue } argument ? Of(argument.Parameter!, operation.Type!) : null;

    /// <summary>The kind a value of <paramref name="type"/> supplied for <paramref name="parameter"/> is, or null.</summary>
    public static CallerLocationKind? Of(IParameterSymbol parameter, ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_String when Has(parameter, "CallerFilePathAttribute") => CallerLocationKind.File,
        SpecialType.System_Int32 when Has(parameter, "CallerLineNumberAttribute") => CallerLocationKind.Line,
        _ => null,
    };

    private static bool Has(IParameterSymbol parameter, string attribute) =>
        parameter.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass!.Name, attribute, StringComparison.Ordinal)
            && string.Equals(a.AttributeClass.ContainingNamespace.ToDisplayString(), Namespace, StringComparison.Ordinal));
}
