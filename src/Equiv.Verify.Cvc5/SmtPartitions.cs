using System.Collections.Immutable;
using System.Text;

namespace Equiv.Verify.Cvc5;

/// <summary>
/// The text of a query split into partitions (ticket P1-039): the partitions cvc5 wrote, each partition's script, and the
/// script that checks the partitions cover every case. A partition is one Boolean term over the query's own symbols, and
/// a script ends with <c>check-sat</c> and, after it, the <c>get-value</c> the read-back needs.
/// </summary>
internal static class SmtPartitions
{
    private const string CheckSat = "(check-sat)";

    /// <summary>
    /// The partitions in what <c>--write-partitions-to</c> wrote: one a line, in the order written, a line written twice
    /// once (a cube strategy writes each cube twice).
    /// </summary>
    public static ImmutableArray<string> Read(string text) =>
        [.. text.Split('\n').Select(static line => line.Trim()).Where(static line => line.Length > 0).Distinct(StringComparer.Ordinal)];

    /// <summary><paramref name="script"/> with <paramref name="partition"/> asserted before its last <c>check-sat</c>.</summary>
    public static string With(string script, string partition)
    {
        int at = script.LastIndexOf(CheckSat, StringComparison.Ordinal);
        return new StringBuilder(script.Length + partition.Length + 10)
            .Append(script, 0, at)
            .Append("(assert ").Append(partition).Append(")\n")
            .Append(script, at, script.Length - at)
            .ToString();
    }

    /// <summary>
    /// The cover check: <paramref name="script"/>'s logic and declarations, and the assertion that no partition holds.
    /// It is unsatisfiable exactly when every assignment satisfies one of <paramref name="partitions"/>, whatever the
    /// query's own assertions say.
    /// </summary>
    public static string Cover(string script, IReadOnlyList<string> partitions)
    {
        StringBuilder cover = new();
        foreach ((int start, int end) in TopLevel(script))
        {
            ReadOnlySpan<char> form = script.AsSpan(start, end - start);
            if (form.StartsWith("(set-logic ", StringComparison.Ordinal) || form.StartsWith("(declare-", StringComparison.Ordinal) || form.StartsWith("(define-", StringComparison.Ordinal))
            {
                cover.Append(form).Append('\n');
            }
        }

        cover.Append("(assert (not (or false");
        foreach (string partition in partitions)
        {
            cover.Append(' ').Append(partition);
        }

        return cover.Append(")))\n").Append(CheckSat).Append('\n').ToString();
    }

    /// <summary>
    /// Where each of <paramref name="script"/>'s top-level forms starts and ends. A quoted symbol or a string may hold
    /// parentheses; an unclosed one ends the script.
    /// </summary>
    private static List<(int Start, int End)> TopLevel(string script)
    {
        List<(int Start, int End)> forms = [];
        int depth = 0;
        int start = 0;
        int i = 0;
        while (i < script.Length)
        {
            char c = script[i];
            if (c is '|' or '"')
            {
                // A quoted symbol or a string runs to its closing mark.
                int close = script.IndexOf(c, i + 1);
                if (close < 0)
                {
                    break;
                }

                i = close;
            }
            else if (c == '(')
            {
                start = depth++ == 0 ? i : start;
            }
            else if (c == ')' && --depth == 0)
            {
                forms.Add((start, i + 1));
            }

            i++;
        }

        return forms;
    }
}
