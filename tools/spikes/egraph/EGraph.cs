namespace EgraphSpike;

/// <summary>
/// A minimal e-graph (egg, POPL 2021, without its analyses or incremental rebuild): e-nodes are serialiser labels over child
/// e-classes, hash-consed; <see cref="Saturate"/> applies the P1-011 rule set until nothing changes, the two roots meet, or a
/// budget runs out. Every rule is written so that, where it applies, it is an identity of the IR the frontend lowers to:
/// integral and <c>bool</c> operators with no user-defined operator method, and a reordering of operands only when both are
/// pure (no call, no assignment, nothing that throws), because C# evaluates operands left to right.
/// </summary>
internal sealed class EGraph
{
    public const int MaxIterations = 12;
    public const int MaxNodes = 20_000;

    private const string Not = "Unary syntax=LogicalNotExpression implicit=False type=System.Boolean context=unchecked";

    private static readonly Dictionary<string, string> Commutative = new(StringComparer.Ordinal)
    {
        ["AddExpression"] = "+", ["MultiplyExpression"] = "*", ["BitwiseAndExpression"] = "&", ["BitwiseOrExpression"] = "|",
        ["ExclusiveOrExpression"] = "^", ["LogicalAndExpression"] = "&&", ["LogicalOrExpression"] = "||",
    };

    private static readonly Dictionary<string, string> Flipped = new(StringComparer.Ordinal)
    {
        ["LessThanExpression"] = "GreaterThanExpression", ["GreaterThanExpression"] = "LessThanExpression",
        ["LessThanOrEqualExpression"] = "GreaterThanOrEqualExpression", ["GreaterThanOrEqualExpression"] = "LessThanOrEqualExpression",
        ["EqualsExpression"] = "EqualsExpression", ["NotEqualsExpression"] = "NotEqualsExpression",
    };

    private static readonly HashSet<string> Integral = new(StringComparer.Ordinal)
    {
        "System.Int32", "System.Int64", "System.UInt32", "System.UInt64",
        "System.Nullable<System.Int32>", "System.Nullable<System.Int64>", "System.Nullable<System.UInt32>", "System.Nullable<System.UInt64>",
    };

    private static readonly HashSet<string> Boolean = new(StringComparer.Ordinal) { "System.Boolean", "System.Nullable<System.Boolean>" };

    private static readonly HashSet<string> NotComparable = new(StringComparer.Ordinal)
    {
        "System.Single", "System.Double", "System.Decimal", "System.Nullable<System.Single>", "System.Nullable<System.Double>", "System.Nullable<System.Decimal>", "",
    };

    private readonly List<int> parent = [];
    private readonly Dictionary<string, int> hashcons = new(StringComparer.Ordinal);
    private List<ENode> nodes = [];
    private readonly IReadOnlySet<string> enabled;
    private Dictionary<int, List<ENode>> view = [];
    private HashSet<int> pure = [];

    private EGraph(IReadOnlySet<string> enabled) => this.enabled = enabled;

    /// <summary>The rule families of criterion 1, by the names the report uses.</summary>
    public static IReadOnlyList<string> Families { get; } = ["commutativity", "associativity", "x-y=x+(-y)", "comparison-flip", "!(a==b)=a!=b", "double-negation", "if-else-swap"];

    /// <summary>The rules (family and operator) whose unions changed the graph.</summary>
    public HashSet<string> Fired { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="enabled"/> rule families make <paramref name="a"/> and <paramref name="b"/> one e-class, and the rules that fired.</summary>
    public static (bool Closed, HashSet<string> Fired) Saturate(OpTree a, OpTree b, IReadOnlySet<string> enabled)
    {
        EGraph graph = new(enabled);
        int ra = graph.Add(a);
        int rb = graph.Add(b);
        graph.Rebuild();
        for (int i = 0; i < MaxIterations && graph.Find(ra) != graph.Find(rb) && graph.nodes.Count < MaxNodes; i++)
        {
            if (!graph.Step())
            {
                break;
            }
        }

        return (graph.Find(ra) == graph.Find(rb), graph.Fired);
    }

    private int Add(OpTree tree) => Add(tree.Label, [.. tree.Kids.Select(Add)]);

    private int Add(string label, int[] kids)
    {
        int[] canonical = [.. kids.Select(Find)];
        string key = Key(label, canonical);
        if (hashcons.TryGetValue(key, out int existing))
        {
            return Find(existing);
        }

        int id = parent.Count;
        parent.Add(id);
        nodes.Add(new ENode(label, canonical, LabelFields.Parse(label), id));
        hashcons[key] = id;
        return id;
    }

    private int Find(int id)
    {
        while (parent[id] != id)
        {
            parent[id] = parent[parent[id]];
            id = parent[id];
        }

        return id;
    }

    private bool Union(int a, int b, string? rule)
    {
        (a, b) = (Find(a), Find(b));
        if (a == b)
        {
            return false;
        }

        parent[b] = a;
        if (rule is not null)
        {
            Fired.Add(rule);
        }

        return true;
    }

    private static string Key(string label, int[] kids) => $"{label}\u0001{string.Join(',', kids)}";

    /// <summary>Restores congruence: nodes whose canonical form is equal are one class, repeated to a fixed point.</summary>
    private void Rebuild()
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            hashcons.Clear();
            List<ENode> kept = [];
            foreach (ENode node in nodes)
            {
                ENode canonical = node with { Kids = [.. node.Kids.Select(Find)], Class = Find(node.Class) };
                string key = Key(canonical.Label, canonical.Kids);
                if (hashcons.TryGetValue(key, out int other))
                {
                    changed |= Union(other, canonical.Class, rule: null);
                    continue;
                }

                hashcons[key] = canonical.Class;
                kept.Add(canonical);
            }

            nodes = kept;
        }
    }

    /// <summary>One round: every enabled rule against a snapshot of the graph, then a rebuild. False when nothing changed.</summary>
    private bool Step()
    {
        view = nodes.GroupBy(n => Find(n.Class)).ToDictionary(static g => g.Key, static g => g.ToList());
        pure = Purity();
        int before = parent.Count;
        bool unions = false;
        foreach (ENode node in nodes.ToList())
        {
            int cls = Find(node.Class);
            foreach ((string rule, int term) in Rewrites(node))
            {
                unions |= Union(cls, term, rule);
            }
        }

        Rebuild();
        return unions || parent.Count != before;
    }

    /// <summary>The classes that hold a pure node: an operand whose evaluation has no effect and cannot throw.</summary>
    private HashSet<int> Purity()
    {
        HashSet<int> result = [];
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (ENode node in nodes)
            {
                int cls = Find(node.Class);
                if (!result.Contains(cls) && IsPureNode(node) && node.Kids.All(k => result.Contains(Find(k))))
                {
                    changed |= result.Add(cls);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// A node that neither has an effect nor throws once its children do not: a reference, a literal, a built-in integral or
    /// <c>bool</c> operator other than division, a compiler-inserted or integral conversion, and a static field or a field of
    /// <c>this</c> (any other instance can be null).
    /// </summary>
    private bool IsPureNode(ENode node)
    {
        LabelFields f = node.Fields;
        bool scalar = Integral.Contains(f.Type) || Boolean.Contains(f.Type);
        return f.Kind switch
        {
            "LocalReference" or "ParameterReference" or "Literal" or "InstanceReference" or "DefaultValue" => true,
            "Binary" => !f.HasSymbols && f.Context == "unchecked" && scalar && f.Syntax is not ("DivideExpression" or "ModuloExpression"),
            "Unary" => !f.HasSymbols && f.Context == "unchecked" && scalar,
            "Conversion" => !f.HasSymbols && f.Context == "unchecked"
                && (node.Label.Contains(" implicit=True", StringComparison.Ordinal) || (scalar && node.Kids.All(k => Integral.Contains(TypeOf(k)) || Boolean.Contains(TypeOf(k))))),
            "FieldReference" => node.Kids.Length == 0 || Members(node.Kids[0]).Any(static m => m.Fields.Kind == "InstanceReference"),
            _ => false,
        };
    }

    private bool IsPure(int cls) => pure.Contains(Find(cls));

    private string TypeOf(int cls) => view.TryGetValue(Find(cls), out List<ENode>? members) ? members[0].Fields.Type : "";

    private IEnumerable<ENode> Members(int cls) => view.TryGetValue(Find(cls), out List<ENode>? members) ? members : [];

    private IEnumerable<(string Rule, int Term)> Rewrites(ENode n)
    {
        LabelFields f = n.Fields;
        bool binary = f.Kind == "Binary" && !f.HasSymbols && n.Kids.Length == 2;
        bool integral = Integral.Contains(f.Type);
        if (binary && Commutative.TryGetValue(f.Syntax, out string? op))
        {
            bool typed = op is "&&" or "||" ? Boolean.Contains(f.Type) : op is "+" or "*" ? integral : integral || Boolean.Contains(f.Type);
            if (typed && enabled.Contains("commutativity") && IsPure(n.Kids[0]) && IsPure(n.Kids[1]))
            {
                yield return ($"commutativity({op})", Add(n.Label, [n.Kids[1], n.Kids[0]]));
            }

            if (typed && enabled.Contains("associativity") && (op is not ("+" or "*") || f.Context == "unchecked"))
            {
                foreach (ENode m in Members(n.Kids[0]).Where(m => m.Label == n.Label).ToList())
                {
                    yield return ($"associativity({op})", Add(n.Label, [m.Kids[0], Add(n.Label, [m.Kids[1], n.Kids[1]])]));
                }

                foreach (ENode m in Members(n.Kids[1]).Where(m => m.Label == n.Label).ToList())
                {
                    yield return ($"associativity({op})", Add(n.Label, [Add(n.Label, [n.Kids[0], m.Kids[0]]), m.Kids[1]]));
                }
            }
        }

        bool signed = f.Type is "System.Int32" or "System.Int64";
        if (binary && signed && f.Context == "unchecked" && enabled.Contains("x-y=x+(-y)"))
        {
            string negate = $"Unary syntax=UnaryMinusExpression implicit=False type={f.Type} context=unchecked";
            if (f.Syntax == "SubtractExpression")
            {
                yield return ("x-y=x+(-y)", Add(Swap(n.Label, f.Syntax, "AddExpression"), [n.Kids[0], Add(negate, [n.Kids[1]])]));
            }
            else if (f.Syntax == "AddExpression")
            {
                foreach (ENode m in Members(n.Kids[1]).Where(m => m.Label == negate).ToList())
                {
                    yield return ("x-y=x+(-y)", Add(Swap(n.Label, f.Syntax, "SubtractExpression"), [n.Kids[0], m.Kids[0]]));
                }
            }
        }

        bool comparable = binary && !NotComparable.Contains(TypeOf(n.Kids[0])) && !NotComparable.Contains(TypeOf(n.Kids[1]));
        if (comparable && Flipped.TryGetValue(f.Syntax, out string? flipped) && enabled.Contains("comparison-flip") && IsPure(n.Kids[0]) && IsPure(n.Kids[1]))
        {
            yield return ($"comparison-flip({f.Syntax})", Add(Swap(n.Label, f.Syntax, flipped), [n.Kids[1], n.Kids[0]]));
        }

        if (comparable && f.Syntax == "NotEqualsExpression" && enabled.Contains("!(a==b)=a!=b"))
        {
            yield return ("!(a==b)=a!=b", Add(Not, [Add(Swap(n.Label, f.Syntax, "EqualsExpression"), [n.Kids[0], n.Kids[1]])]));
        }

        if (f.Kind == "Unary" && n.Kids.Length == 1)
        {
            foreach (ENode m in Members(n.Kids[0]).ToList())
            {
                bool equality = m.Fields is { Kind: "Binary", HasSymbols: false, Syntax: "EqualsExpression" or "NotEqualsExpression" } && m.Kids.Length == 2
                    && !NotComparable.Contains(TypeOf(m.Kids[0])) && !NotComparable.Contains(TypeOf(m.Kids[1]));
                if (n.Label == Not && equality && enabled.Contains("!(a==b)=a!=b"))
                {
                    string other = m.Fields.Syntax == "EqualsExpression" ? "NotEqualsExpression" : "EqualsExpression";
                    yield return ("!(a==b)=a!=b", Add(Swap(m.Label, m.Fields.Syntax, other), [m.Kids[0], m.Kids[1]]));
                }

                bool involution = f.Syntax is "LogicalNotExpression" or "BitwiseNotExpression"
                    || (f.Syntax == "UnaryMinusExpression" && f.Type is "System.Int32" or "System.Int64" && f.Context == "unchecked");
                if (m.Label == n.Label && involution && m.Kids.Length == 1 && enabled.Contains("double-negation"))
                {
                    yield return ($"double-negation({f.Syntax})", m.Kids[0]);
                }
            }
        }

        if (f.Kind == "Conditional" && n.Kids.Length == 3 && TypeOf(n.Kids[0]) == "System.Boolean" && enabled.Contains("if-else-swap"))
        {
            yield return ($"if-else-swap({f.Syntax})", Add(n.Label, [Add(Not, [n.Kids[0]]), n.Kids[2], n.Kids[1]]));
        }
    }

    private static string Swap(string label, string from, string to) => label.Replace($"syntax={from}", $"syntax={to}", StringComparison.Ordinal);

    private sealed record ENode(string Label, int[] Kids, LabelFields Fields, int Class);
}
