namespace EgraphSpike;

/// <summary>
/// Whether every subtree in which two serialisations differ is closed by the rule set (criterion 1). The walk descends while
/// both sides have the same line and arity, so a difference is decided at the smallest subtree that holds it; only when the
/// children cannot close it on their own is the e-graph asked about the pair of subtrees as a whole (that is where
/// commutativity and associativity act). A closed difference is attributed to a minimal set of rule families; an open one is
/// reported by node kind and the names of the fields that differ, never by its text.
/// </summary>
internal static class Differ
{
    /// <summary>Subtrees above this size are compared by their children only: saturating a whole body is the feature, not the spike.</summary>
    public const int MaxSaturatedSize = 400;

    private static readonly HashSet<string> All = [.. EGraph.Families];

    public static Outcome Close(OpTree a, OpTree b)
    {
        if (a.SameAs(b))
        {
            return Outcome.Equal;
        }

        Outcome byKids = string.Equals(a.Label, b.Label, StringComparison.Ordinal) && a.Kids.Count == b.Kids.Count
            ? a.Kids.Zip(b.Kids).Select(static p => Close(p.First, p.Second)).Aggregate(Outcome.Equal, Outcome.Combine)
            : Outcome.Open(Residuals(a, b));
        if (byKids.Closed || a.Size + b.Size > MaxSaturatedSize)
        {
            return byKids;
        }

        (bool closed, HashSet<string> fired) = EGraph.Saturate(a, b, All);
        return closed ? new Outcome(true, Minimal(a, b, fired), []) : byKids;
    }

    /// <summary>The rule families that suffice, found by dropping each fired family in turn and keeping it only if the pair then stays open.</summary>
    private static List<string> Minimal(OpTree a, OpTree b, HashSet<string> fired)
    {
        HashSet<string> needed = [.. EGraph.Families.Where(family => fired.Any(rule => rule.StartsWith(family, StringComparison.Ordinal)))];
        foreach (string family in needed.ToList())
        {
            needed.Remove(family);
            if (!EGraph.Saturate(a, b, needed).Closed)
            {
                needed.Add(family);
            }
        }

        (_, HashSet<string> used) = EGraph.Saturate(a, b, needed);
        return [.. used.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Why two subtrees differ, by kind: a different kind or syntax kind, the fields that differ on one kind, or, when only the
    /// number of children differs, each child the other side lacks (aligned by longest common subsequence).
    /// </summary>
    private static List<string> Residuals(OpTree a, OpTree b)
    {
        (LabelFields fa, LabelFields fb) = (a.Fields, b.Fields);
        if (fa.Kind != fb.Kind || fa.Syntax != fb.Syntax)
        {
            return [$"{Name(fa)} vs {Name(fb)}"];
        }

        if (!string.Equals(a.Label, b.Label, StringComparison.Ordinal))
        {
            return [$"{Name(fa)}: {LabelFields.Differing(a.Label, b.Label)} differ"];
        }

        // Same line, different arity: children the LCS leaves unaligned are paired in order where they are of one kind (an
        // edited statement next to an inserted one), and the rest are reported as present on one side only.
        (List<OpTree> onlyA, List<OpTree> onlyB) = Unaligned(a.Kids, b.Kids);
        List<string> residuals = [];
        while (onlyA.Count > 0 && onlyB.Count > 0 && onlyA[0].Fields.Kind == onlyB[0].Fields.Kind)
        {
            residuals.AddRange(Close(onlyA[0], onlyB[0]).Residuals);
            onlyA.RemoveAt(0);
            onlyB.RemoveAt(0);
        }

        return [.. residuals, .. onlyA.Select(k => $"{Name(fa)}: legacy-only {Name(k.Fields)}"), .. onlyB.Select(k => $"{Name(fa)}: modern-only {Name(k.Fields)}")];
    }

    private static string Name(LabelFields f) => f.Syntax.Length == 0 ? f.Kind : $"{f.Kind}[{f.Syntax}]";

    private static (List<OpTree> OnlyA, List<OpTree> OnlyB) Unaligned(List<OpTree> a, List<OpTree> b)
    {
        int[,] lcs = new int[a.Count + 1, b.Count + 1];
        for (int i = a.Count - 1; i >= 0; i--)
        {
            for (int j = b.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i].SameAs(b[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        List<OpTree> onlyA = [];
        List<OpTree> onlyB = [];
        (int x, int y) = (0, 0);
        while (x < a.Count && y < b.Count)
        {
            if (a[x].SameAs(b[y]))
            {
                (x, y) = (x + 1, y + 1);
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                onlyA.Add(a[x++]);
            }
            else
            {
                onlyB.Add(b[y++]);
            }
        }

        onlyA.AddRange(a.Skip(x));
        onlyB.AddRange(b.Skip(y));
        return (onlyA, onlyB);
    }
}

/// <summary>A comparison's result: closed or not, the rules that closed it, and what stayed open.</summary>
internal sealed record Outcome(bool Closed, List<string> Rules, List<string> Residuals)
{
    public static Outcome Equal { get; } = new(true, [], []);

    public static Outcome Open(List<string> residuals) => new(false, [], residuals);

    public static Outcome Combine(Outcome left, Outcome right) =>
        new(left.Closed && right.Closed, [.. left.Rules.Union(right.Rules, StringComparer.Ordinal)], [.. left.Residuals, .. right.Residuals]);
}
