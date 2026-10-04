using System.Collections.Immutable;
using System.Text.RegularExpressions;

using Equiv.Core;

namespace Equiv.Verify.Cvc5;

/// <summary>
/// Reads what a solver process printed for a script that ends with <c>check-sat</c> and one <c>get-value</c> (ticket
/// P1-033). The first line is the answer. After <c>sat</c>, the rest is <c>((name value) ...)</c>, read pair by pair
/// and by name: a name is a symbol, with its <c>|...|</c> when it has them, and a value is one atom or one balanced
/// term, kept as printed. Anything else on the first line is unknown, with that line as the reason.
/// </summary>
internal static partial class SmtOutput
{
    private const int ReasonLength = 200;

    public static SmtAnswer Parse(string output)
    {
        string text = output.TrimStart();
        int end = text.IndexOfAny(['\r', '\n']);
        string first = (end < 0 ? text : text[..end]).TrimEnd();
        return first switch
        {
            "unsat" => new SmtUnsat(),
            "sat" => new SmtSat(Values(text[first.Length..])),
            _ => new SmtUnknown(first.Length > ReasonLength ? first[..ReasonLength] : first),
        };
    }

    /// <summary>
    /// The version <c>cvc5 --version</c> prints first, <c>cvc5 1.4.1 [git ...]</c> (releases before 1.1 wrote
    /// <c>This is cvc5 version 1.0.0</c>); <c>unknown</c> without one.
    /// </summary>
    public static string Version(string? output) =>
        output is not null && VersionLine.Match(output) is { Success: true } match ? match.Groups["version"].Value : "unknown";

    private static ImmutableDictionary<string, string> Values(string text)
    {
        ImmutableDictionary<string, string>.Builder values = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (Match pair in Pair.Matches(text))
        {
            values[pair.Groups["name"].Value] = pair.Groups["value"].Value;
        }

        return values.ToImmutable();
    }

    // (name value): a value is an atom, or a term with one level of parentheses inside it, such as (_ bv5 32).
    [GeneratedRegex(@"\((?<name>\|[^|]*\||[^\s()|]+)\s+(?<value>[^\s()]+|\(([^()]|\([^()]*\))*\))\s*\)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 60_000)]
    private static partial Regex Pair { get; }

    [GeneratedRegex(@"cvc5\s+(version\s+)?(?<version>\d\S*)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 60_000)]
    private static partial Regex VersionLine { get; }
}
