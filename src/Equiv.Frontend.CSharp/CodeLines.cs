using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// The analysed line count of one side (ticket M3-014), by the one rule README's "Licence" section states: a line
/// counts when it holds part of a C# token. Blank lines, comment-only lines, preprocessor directive lines and code
/// that a <c>#if</c> excludes are not counted. Every syntax tree in the side's compilations counts once per file
/// path, so a file that two projects compile (a linked file, or one project loaded once per target framework) is
/// not counted twice.
/// </summary>
internal static class CodeLines
{
    public static int Count(ImmutableArray<Compilation> compilations)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        return compilations
            .SelectMany(static compilation => compilation.SyntaxTrees)
            .Where(tree => seen.Add(tree.FilePath))
            .Sum(Count);
    }

    /// <summary>
    /// Tokens arrive in source order, so a line is new exactly when it is past the last line counted. A token that
    /// spans lines (a verbatim or raw string) counts every line it is on. The end-of-file token holds no text.
    /// </summary>
    internal static int Count(SyntaxTree tree)
    {
        TextLineCollection lines = tree.GetText().Lines;
        int count = 0;
        int last = -1;
        foreach (SyntaxToken token in tree.GetRoot().DescendantTokens().Where(static t => !t.Span.IsEmpty))
        {
            LinePositionSpan span = lines.GetLinePositionSpan(token.Span);
            count += Math.Max(0, span.End.Line - Math.Max(span.Start.Line, last + 1) + 1);
            last = span.End.Line;
        }

        return count;
    }
}
