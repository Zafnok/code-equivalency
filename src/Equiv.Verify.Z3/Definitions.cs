using Microsoft.Z3;

namespace Equiv.Verify.Z3;

/// <summary>
/// The definitions <see cref="Z3Backend.Inline"/> substitutes into a query, in assertion order: a constant each, and the
/// term it stands for with the earlier definitions already substituted. <see cref="Apply"/> gives the term Z3's
/// substitution of every definition gives, and hands Z3 only the definitions whose constant the term holds. Handing it
/// all of them costs their number on every call, so inlining <c>n</c> definitions cost <c>n</c> squared (ticket P2-076).
/// <para>
/// Every Z3 object made on the way is released by <see cref="Dispose"/>. Left to the finalizer, each would hold its term
/// until after the query's context is disposed, and Z3 frees the terms a disposed context still holds one layer of
/// nesting per pass over all of them. Inlining nests the definitions <c>n</c> deep, so that cost <c>n</c> squared too,
/// and more time than the substitution itself.
/// </para>
/// </summary>
internal sealed class Definitions : IDisposable
{
    private readonly List<Expr> names = [];
    private readonly List<Expr> values = [];
    private readonly Dictionary<uint, List<int>> defined = [];
    private readonly List<Expr> owned = [];

    /// <summary><paramref name="made"/>, released with this object.</summary>
    public T Own<T>(T made)
        where T : Expr
    {
        owned.Add(made);
        return made;
    }

    /// <summary>
    /// The next definition: the constant <paramref name="name"/> is <paramref name="value"/>. A name defined again keeps
    /// both definitions, as the list handed to Z3 always did.
    /// </summary>
    public void Add(Expr name, Expr value)
    {
        if (!defined.TryGetValue(name.Id, out List<int>? definitions))
        {
            defined[name.Id] = definitions = [];
        }

        definitions.Add(names.Count);
        names.Add(name);
        values.Add(value);
    }

    /// <summary><paramref name="term"/> with every definition so far substituted in.</summary>
    public Expr Apply(Expr term)
    {
        int[]? used = Used(term);
        return used is null
            ? term.Substitute([.. names], [.. values])
            : term.Substitute([.. used.Select(i => names[i])], [.. used.Select(i => values[i])]);
    }

    public void Dispose()
    {
        foreach (Expr made in owned)
        {
            made.Dispose();
        }
    }

    /// <summary>
    /// The definitions of the constants <paramref name="term"/> holds, in order, or null for all of them when it holds
    /// anything but applications and numerals (a quantifier, a bound variable), which Z3's substitution has rules of its
    /// own for.
    /// </summary>
    private int[]? Used(Expr term)
    {
        SortedSet<int> used = [];
        HashSet<uint> seen = [];
        Stack<Expr> pending = new([term]);
        bool ground = true;
        while (pending.TryPop(out Expr? node))
        {
            if (ground && seen.Add(node.Id))
            {
                Z3_ast_kind kind = node.ASTKind;
                ground = kind is Z3_ast_kind.Z3_APP_AST or Z3_ast_kind.Z3_NUMERAL_AST;
                if (defined.TryGetValue(node.Id, out List<int>? definitions))
                {
                    used.UnionWith(definitions);
                }

                uint arguments = kind == Z3_ast_kind.Z3_APP_AST ? node.NumArgs : 0;
                for (uint i = 0; i < arguments; i++)
                {
                    pending.Push(node.Arg(i));
                }
            }

            if (!ReferenceEquals(node, term))
            {
                node.Dispose();
            }
        }

        return ground ? [.. used] : null;
    }
}
