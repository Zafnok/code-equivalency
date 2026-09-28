using System.Collections.Immutable;
using System.Globalization;

namespace Equiv.Verify.Z3.Ladder;

/// <summary>
/// The candidate relations <see cref="TraceInvariantProposer"/> mines (ticket P1-009), in the style of Daikon: over each
/// pair of a relation's arguments of one sort, <c>x = y</c>, <c>x = y + c</c> and <c>x = c*y</c> for a small constant
/// <c>c</c>, and <c>x &lt;= y</c>; and over each argument, the range <c>lo &lt;= x &lt;= hi</c> of the bounds that are
/// constant across the traces (a Bool's value when it never changes). <see cref="Mine"/> keeps every instance that held on
/// every sample of every trace. A sample holds an Int argument as a <see cref="long"/>, a Bool one as a <see cref="bool"/>
/// and any other as a value with equality only; an argument missing from a sample (a thrown side's value) is in no instance.
/// </summary>
internal static class InvariantTemplates
{
    /// <summary>The largest magnitude of <c>c</c> in <c>x = y + c</c> and <c>x = c*y</c>.</summary>
    public const long MaxConstant = 64;

    private const string Int = "Int";

    private const string Bool = "Bool";

    /// <summary>
    /// Every instance that held on every sample of <paramref name="traces"/>, each trace the samples of one pair of runs;
    /// <see cref="Template.Never"/> alone for a relation no trace reached.
    /// </summary>
    public static ImmutableArray<Conjunct> Mine(ImmutableArray<InvariantRequest.Variable> parameters, IReadOnlyList<IReadOnlyList<IReadOnlyDictionary<string, object>>> traces)
    {
        IReadOnlyDictionary<string, object>[] samples = [.. traces.SelectMany(static t => t)];
        if (samples.Length == 0)
        {
            return [new Conjunct(Template.Never, "false")];
        }

        InvariantRequest.Variable[] present = [.. parameters.Where(p => samples.All(s => s.ContainsKey(p.Name)))];
        List<Conjunct> found = [];
        for (int i = 0; i < present.Length; i++)
        {
            for (int j = i + 1; j < present.Length; j++)
            {
                if (string.Equals(present[i].Sort, present[j].Sort, StringComparison.Ordinal))
                {
                    found.AddRange(Pair(present[i].Name, present[j].Name, present[i].Sort, samples));
                }
            }
        }

        foreach (InvariantRequest.Variable variable in present)
        {
            found.AddRange(Range(variable, [.. traces.Where(static t => t.Count > 0).Select(t => t.Select(s => s[variable.Name]).ToArray())]));
        }

        return [.. found];
    }

    /// <summary>The strongest pair templates that held: equality, else an offset, else a factor and an order.</summary>
    private static List<Conjunct> Pair(string x, string y, string sort, IReadOnlyDictionary<string, object>[] samples)
    {
        if (samples.All(s => s[x].Equals(s[y])))
        {
            return [new Conjunct(Template.Equal, x, y)];
        }

        if (!string.Equals(sort, Int, StringComparison.Ordinal))
        {
            return [];
        }

        long[] xs = [.. samples.Select(s => (long)s[x])];
        long[] ys = [.. samples.Select(s => (long)s[y])];
        long offset = xs[0] - ys[0];
        if (Math.Abs(offset) <= MaxConstant && xs.Zip(ys).All(p => p.First - p.Second == offset))
        {
            return [new Conjunct(Template.Offset, x, y, offset)];
        }

        List<Conjunct> found = [.. Scale(x, xs, y, ys), .. Scale(y, ys, x, xs)];
        if (xs.Zip(ys).All(static p => p.First <= p.Second))
        {
            found.Add(new Conjunct(Template.AtMost, x, y));
        }
        else if (xs.Zip(ys).All(static p => p.Second <= p.First))
        {
            found.Add(new Conjunct(Template.AtMost, y, x));
        }

        return found;
    }

    /// <summary><c>x = c*y</c> with <c>c</c> the ratio of the first sample with <c>y</c> non-zero, when it held everywhere.</summary>
    private static IEnumerable<Conjunct> Scale(string x, long[] xs, string y, long[] ys)
    {
        int k = Array.FindIndex(ys, static v => v != 0);
        if (k < 0 || xs[k] % ys[k] != 0)
        {
            return [];
        }

        long factor = xs[k] / ys[k];
        bool small = factor is not (0 or 1) && Math.Abs(factor) <= MaxConstant;
        return small && xs.Zip(ys).All(p => p.First == factor * p.Second) ? [new Conjunct(Template.Scale, x, y, factor)] : [];
    }

    /// <summary>
    /// The bounds of <paramref name="variable"/> that are the same in every trace: an Int's smallest and largest value, a
    /// Bool's only value.
    /// </summary>
    private static List<Conjunct> Range(InvariantRequest.Variable variable, object[][] traces)
    {
        string x = variable.Name;
        if (string.Equals(variable.Sort, Bool, StringComparison.Ordinal))
        {
            bool first = (bool)traces[0][0];
            return traces.All(t => t.All(v => (bool)v == first)) ? [new Conjunct(Template.Truth, x, C: first ? 1 : 0)] : [];
        }

        if (!string.Equals(variable.Sort, Int, StringComparison.Ordinal))
        {
            return [];
        }

        long[] lows = [.. traces.Select(static t => t.Min(static v => (long)v))];
        long[] highs = [.. traces.Select(static t => t.Max(static v => (long)v))];
        List<Conjunct> found = [];
        if (lows.All(l => l == lows[0]))
        {
            found.Add(new Conjunct(Template.Lower, x, C: lows[0]));
        }

        if (highs.All(h => h == highs[0]))
        {
            found.Add(new Conjunct(Template.Upper, x, C: highs[0]));
        }

        return found;
    }

    /// <summary>A template's shape.</summary>
    internal enum Template
    {
        /// <summary><c>false</c>: no trace reached the relation.</summary>
        Never,

        /// <summary><c>x = y</c>.</summary>
        Equal,

        /// <summary><c>x = y + c</c>.</summary>
        Offset,

        /// <summary><c>x = c*y</c>.</summary>
        Scale,

        /// <summary><c>x &lt;= y</c>.</summary>
        AtMost,

        /// <summary><c>c &lt;= x</c>, the range's lower bound.</summary>
        Lower,

        /// <summary><c>x &lt;= c</c>, the range's upper bound.</summary>
        Upper,

        /// <summary>A Bool's range: <c>x</c> when <c>c</c> is 1, else <c>(not x)</c>.</summary>
        Truth,
    }

    /// <summary>One template instance over the arguments <paramref name="X"/> and <paramref name="Y"/> with constant <paramref name="C"/>.</summary>
    internal sealed record Conjunct(Template Template, string X, string Y = "", long C = 0)
    {
        /// <summary>The instance in SMT-LIB.</summary>
        public string Smt => Template switch
        {
            Template.Never => "false",
            Template.Equal => $"(= {X} {Y})",
            Template.Offset => $"(= {X} (+ {Y} {Number(C)}))",
            Template.Scale => $"(= {X} (* {Number(C)} {Y}))",
            Template.AtMost => $"(<= {X} {Y})",
            Template.Lower => $"(<= {Number(C)} {X})",
            Template.Upper => $"(<= {X} {Number(C)})",
            _ => C == 1 ? X : $"(not {X})",
        };

        /// <summary>
        /// Whether <paramref name="values"/>, a counterexample's argument values as Z3 prints them, make the instance false.
        /// An argument the values lack, or a value that is neither an integer nor a Boolean, falsifies nothing.
        /// </summary>
        public bool Falsified(IReadOnlyDictionary<string, string> values)
        {
            if (Template == Template.Never)
            {
                return true;
            }

            object? x = Value(values, X);
            object? y = Template is Template.Equal or Template.Offset or Template.Scale or Template.AtMost ? Value(values, Y) : C;
            return (x, y) switch
            {
                (long a, long b) => !Holds(a, b),
                (bool a, bool b) => a != b,
                (bool a, long _) => a != (C == 1),
                _ => false,
            };
        }

        /// <summary>Whether an Int instance holds of <paramref name="x"/> and <paramref name="y"/>, which is <c>c</c> for a bound.</summary>
        private bool Holds(long x, long y) => Template switch
        {
            Template.Equal => x == y,
            Template.Offset => x == y + C,
            Template.Scale => x == C * y,
            Template.Lower => y <= x,
            _ => x <= y,
        };

        /// <summary>An integer as SMT-LIB writes it: <c>(- 5)</c> for a negative one.</summary>
        private static string Number(long value) =>
            value < 0 ? $"(- {(-value).ToString(CultureInfo.InvariantCulture)})" : value.ToString(CultureInfo.InvariantCulture);

        private static object? Value(IReadOnlyDictionary<string, string> values, string name) =>
            values.TryGetValue(name, out string? text) ? Read(text) : null;
    }

    /// <summary>
    /// A value as Z3 prints it, read back: an integer (<c>5</c>, <c>-5</c>, or <c>(- 5)</c> in SMT-LIB) as a
    /// <see cref="long"/>, <c>true</c> or <c>false</c>, else null.
    /// </summary>
    public static object? Read(string text)
    {
        bool negative = text.StartsWith("(- ", StringComparison.Ordinal) && text.EndsWith(')');
        string digits = negative ? text[3..^1] : text;
        return (text, long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number)) switch
        {
            ("true", _) => true,
            ("false", _) => false,
            (_, true) => negative ? -number : number,
            _ => null,
        };
    }
}
