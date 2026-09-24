using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// The census's congruence proxy until bound fingerprints land (ADR 0034; tickets M3-030, M3-015): two procedures are
/// token-equal when the tokens of their declarations, every declaring syntax reference in order, have the same kinds and
/// text. Trivia (whitespace, comments, directives, code a <c>#if</c> excludes) is ignored. A procedure with no source
/// declaration (an implicit constructor) has no tokens.
/// </summary>
internal static class SyntaxTokens
{
    public static bool Equal(IMethodSymbol legacy, IMethodSymbol modern) =>
        Tokens(legacy).SequenceEqual(Tokens(modern));

    private static IEnumerable<(int Kind, string Text)> Tokens(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences
            .SelectMany(static reference => reference.GetSyntax().DescendantTokens())
            .Select(static token => (token.RawKind, token.Text));
}
