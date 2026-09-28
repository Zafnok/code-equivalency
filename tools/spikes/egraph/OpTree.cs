namespace EgraphSpike;

/// <summary>
/// One line of an ADR 0024 canonical serialisation (<c>BoundSerialiser</c>) and the lines indented under it. The label is the
/// line without its indentation, so two nodes are equal exactly when the serialiser wrote the same text for both.
/// </summary>
internal sealed class OpTree
{
    public OpTree(string label, List<OpTree> kids)
    {
        Label = label;
        Kids = kids;
        Size = 1 + kids.Sum(static k => k.Size);
        Hash = kids.Aggregate((ulong)(uint)StringComparer.Ordinal.GetHashCode(label), static (h, k) => (h * 1_000_003UL) ^ k.Hash);
        Fields = LabelFields.Parse(label);
    }

    public string Label { get; }

    public List<OpTree> Kids { get; }

    public int Size { get; }

    public ulong Hash { get; }

    public LabelFields Fields { get; }

    public bool SameAs(OpTree other) =>
        ReferenceEquals(this, other)
        || (Hash == other.Hash && string.Equals(Label, other.Label, StringComparison.Ordinal) && Kids.Count == other.Kids.Count
            && Kids.Zip(other.Kids).All(static p => p.First.SameAs(p.Second)));

    /// <summary>The body text as a tree under a synthetic root, whose first child is the signature line.</summary>
    public static OpTree Parse(string text)
    {
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int index = 0;
        List<OpTree> top = [];
        while (index < lines.Length)
        {
            top.Add(Read(lines, ref index, 0));
        }

        return new OpTree("Body", top);
    }

    private static OpTree Read(string[] lines, ref int index, int depth)
    {
        string label = lines[index][(depth * 2)..];
        index++;
        List<OpTree> kids = [];
        while (index < lines.Length && Depth(lines[index]) > depth)
        {
            kids.Add(Read(lines, ref index, depth + 1));
        }

        return new OpTree(label, kids);
    }

    private static int Depth(string line) => (line.Length - line.TrimStart(' ').Length) / 2;
}

/// <summary>The parts of a serialiser line the rules test: kind, syntax kind, type, and whether a constant, symbol or context is present.</summary>
internal sealed record LabelFields(string Kind, string Syntax, string Type, bool HasConst, bool HasSymbols, string Context)
{
    public static LabelFields Parse(string label)
    {
        int space = label.IndexOf(' ', StringComparison.Ordinal);
        string kind = space < 0 ? label : label[..space];
        return new LabelFields(kind, Token(label, " syntax="), Token(label, " type="), label.Contains(" const=", StringComparison.Ordinal),
            label.Contains(" symbols=", StringComparison.Ordinal), Token(label, " context="));
    }

    /// <summary>The names of the fields two labels of one kind disagree on, for the residual report.</summary>
    public static string Differing(string a, string b)
    {
        string[] keys = ["syntax", "implicit", "type", "const", "symbols", "context"];
        IEnumerable<string> differing = keys.Where(key => !string.Equals(Raw(a, key), Raw(b, key), StringComparison.Ordinal));
        return string.Join('+', differing);
    }

    private static string Token(string label, string key)
    {
        int start = label.IndexOf(key, StringComparison.Ordinal);
        if (start < 0)
        {
            return "";
        }

        start += key.Length;
        int end = label.IndexOf(' ', start);
        return end < 0 ? label[start..] : label[start..end];
    }

    /// <summary>A field's text up to the next known key; approximate when a quoted constant contains a key, which only mislabels a residual.</summary>
    private static string? Raw(string label, string key)
    {
        int start = label.IndexOf($" {key}=", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        int end = new[] { " syntax=", " implicit=", " type=", " const=", " symbols=", " context=" }
            .Select(k => label.IndexOf(k, start + 1, StringComparison.Ordinal))
            .Where(i => i > start)
            .DefaultIfEmpty(label.Length)
            .Min();
        return label[start..end];
    }
}
