using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// What the regex source generator read and what it wrote (ADR 0055; ticket P2-118). It reads a
/// <c>[GeneratedRegex]</c> on a partial method or property and writes that member's implementing part, and a matcher
/// type per member with a type of shared helpers. All it writes carries its own <c>[GeneratedCode]</c>, which is how its
/// output is told from code somebody wrote: by the symbol in the side's own compilation, never by a file's path.
/// </summary>
internal static class RegexGenerator
{
    /// <summary>The generator's name, as its <c>[GeneratedCode]</c> gives it.</summary>
    public const string Name = "System.Text.RegularExpressions.Generator";

    private const string Declares = "System.Text.RegularExpressions.GeneratedRegexAttribute";

    private const string Stamp = "System.CodeDom.Compiler.GeneratedCodeAttribute";

    /// <summary>
    /// The <c>[GeneratedRegex]</c> that is <paramref name="method"/>'s code (ADR 0055 decision 1): the one on the method,
    /// or on the property it is an accessor of, when the generator wrote the member's implementing part, which gives the
    /// member the generator's <c>[GeneratedCode]</c>. Null for any other method.
    /// </summary>
    public static AttributeData? Declaration(IMethodSymbol method)
    {
        ImmutableArray<AttributeData> attributes = (method.AssociatedSymbol ?? method).GetAttributes();
        return attributes.Any(IsStamp) ? attributes.FirstOrDefault(static a => Is(a, Declares)) : null;
    }

    /// <summary>Whether the generator wrote <paramref name="type"/>: a matcher, or its helpers (ADR 0055 decision 4).</summary>
    public static bool Emitted(INamedTypeSymbol type) => type.GetAttributes().Any(IsStamp);

    private static bool IsStamp(AttributeData attribute) =>
        Is(attribute, Stamp) && string.Equals(attribute.ConstructorArguments.FirstOrDefault().Value as string, Name, StringComparison.Ordinal);

    private static bool Is(AttributeData attribute, string name) => string.Equals(attribute.AttributeClass!.ToDisplayString(), name, StringComparison.Ordinal);
}
