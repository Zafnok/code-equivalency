using System.Collections.Frozen;
using System.Collections.Immutable;

using static Equiv.TestSupport.Mutations.PairSyntax;

namespace Equiv.TestSupport.Mutations;

/// <summary>
/// Applies one <see cref="MutationOperator"/> at one site of a <see cref="Method"/> (ticket M0-012). The sites of an
/// operator are the nodes it applies to, numbered in one fixed walk of the method, so <see cref="Sites"/> and
/// <see cref="Apply"/> agree. <see cref="MutationOperator.RenameLocals"/> has one site, the whole method, and
/// <see cref="MutationOperator.InlineTemporary"/> the same sites as <see cref="MutationOperator.IntroduceTemporary"/>:
/// <see cref="PairGen"/> puts the introduced temporary on the legacy side.
/// </summary>
internal static class Mutator
{
    private static readonly FrozenDictionary<string, string> Renames =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["x"] = "x1", ["y"] = "y1", ["z"] = "z1" }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> Flips =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["<"] = "<=", ["<="] = "<", [">"] = ">=", [">="] = ">", ["=="] = "!=", ["!="] = "==" }
            .ToFrozenDictionary(StringComparer.Ordinal);

    public static int Sites(MutationOperator op, Method method)
    {
        if (op == MutationOperator.RenameLocals)
        {
            return 1;
        }

        Walker walker = Walker.For(op, site: -1);
        walker.Block(method.Body);
        return walker.Seen;
    }

    public static Method Apply(MutationOperator op, Method method, int site)
    {
        if (op == MutationOperator.RenameLocals)
        {
            Walker rename = new(RenameName, RenameTarget, Walker.Everywhere);
            return method with { Locals = rename.Block(method.Locals), Body = rename.Block(method.Body) };
        }

        Walker walker = Walker.For(op, site);
        return method with { Body = walker.Block(method.Body) };
    }

    private static IExpr? RenameName(IExpr expr) =>
        expr is Name name && Renames.TryGetValue(name.Id, out string? renamed) ? name with { Id = renamed } : null;

    private static ImmutableArray<IStmt>? RenameTarget(ImmutableArray<IStmt> block, int i) => block[i] switch
    {
        Declare declare when Renames.TryGetValue(declare.Local, out string? renamed) => block.SetItem(i, declare with { Local = renamed }),
        Assign assign when Renames.TryGetValue(assign.Target, out string? renamed) => block.SetItem(i, assign with { Target = renamed }),
        _ => null,
    };

    private static bool Independent(Assign first, Assign second) =>
        first.Value.CannotThrow && second.Value.CannotThrow
        && !string.Equals(first.Target, second.Target, StringComparison.Ordinal)
        && !Reads(first.Value).Contains(second.Target) && !Reads(second.Value).Contains(first.Target);

    private static HashSet<string> Reads(IExpr expr)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        Walker walker = new(Collect, static (_, _) => null, site: -1);
        walker.Expr(expr);
        return names;

        IExpr? Collect(IExpr e)
        {
            if (e is Name name)
            {
                names.Add(name.Id);
            }

            return null;
        }
    }

    private static bool IsEffect(IStmt statement) => statement is Assign { Target: Field } or Store;

    private static bool Throws(IStmt statement) => statement is If branch && (branch.Then.Any(static s => s is Throw) || branch.Else.Any(static s => s is Throw));

    /// <summary>
    /// One walk of a method: every block position is offered to the block rewrite, then every expression, parent first,
    /// to the expression rewrite. Each offer a rewrite accepts is a site; the walker applies the rewrite at
    /// <c>site</c> (or at every site, for <see cref="Everywhere"/>) and counts them all in <see cref="Seen"/>.
    /// </summary>
    private sealed class Walker(Func<IExpr, IExpr?> onExpr, Func<ImmutableArray<IStmt>, int, ImmutableArray<IStmt>?> onBlock, int site)
    {
        public const int Everywhere = int.MaxValue;

        public int Seen { get; private set; }

        public static Walker For(MutationOperator op, int site) => op switch
        {
            MutationOperator.ReorderIndependentStatements => new(NoExpr, Reorder, site),
            MutationOperator.InvertIf => new(NoExpr, InvertIf, site),
            MutationOperator.Commute => new(Commute, NoBlock, site),
            MutationOperator.IntroduceTemporary or MutationOperator.InlineTemporary => new(NoExpr, IntroduceTemporary, site),
            MutationOperator.FlipComparison => new(FlipComparison, NoBlock, site),
            MutationOperator.ChangeConstant => new(ChangeConstant, NoBlock, site),
            MutationOperator.DropNullCheck => new(NoExpr, DropNullCheck, site),
            MutationOperator.DropFieldWrite => new(NoExpr, DropFieldWrite, site),
            MutationOperator.SwapArguments => new(SwapArguments, NoBlock, site),
            _ => new(NoExpr, MoveThrow, site),
        };

        private static IExpr? Commute(IExpr expr) => expr switch
        {
            Binary binary when binary.Op is "+" or "*" or "&" or "|" or "^" && binary.Left.CannotThrow && binary.Right.CannotThrow =>
                binary with { Left = binary.Right, Right = binary.Left },
            Relation relation when relation.Op is "==" or "!=" && relation.Left.CannotThrow && relation.Right.CannotThrow =>
                relation with { Left = relation.Right, Right = relation.Left },
            _ => null,
        };

        private static IExpr? FlipComparison(IExpr expr) => expr is Relation relation ? relation with { Op = Flips[relation.Op] } : null;

        /// <summary>Literals are only ever right operands; a divisor never becomes zero, which would not compile.</summary>
        private static IExpr? ChangeConstant(IExpr expr)
        {
            if (expr is not Binary { Right: Literal literal } binary)
            {
                return null;
            }

            long changed = literal.Value + (literal.Value == -1 && binary.Op is ("/" or "%") ? -1 : 1);
            return binary with { Right = literal with { Value = literal.Type == typeof(int) ? unchecked((int)changed) : unchecked(changed) } };
        }

        private static IExpr? SwapArguments(IExpr expr) => expr switch
        {
            Binary binary when binary.Op is not ("<<" or ">>") => binary with { Left = binary.Right, Right = binary.Left },
            Relation relation => relation with { Left = relation.Right, Right = relation.Left },
            _ => null,
        };

        private static ImmutableArray<IStmt>? Reorder(ImmutableArray<IStmt> block, int i) =>
            i + 1 < block.Length && block[i] is Assign first && block[i + 1] is Assign second && Independent(first, second)
                ? block.SetItem(i, second).SetItem(i + 1, first)
                : null;

        private static ImmutableArray<IStmt>? InvertIf(ImmutableArray<IStmt> block, int i) =>
            block[i] is If branch
                ? block.SetItem(i, new If(new Unary("!", branch.Condition, IsChecked: false), Then: branch.Else, Else: branch.Then))
                : null;

        private static ImmutableArray<IStmt>? IntroduceTemporary(ImmutableArray<IStmt> block, int i)
        {
            (IExpr? value, IStmt? rewritten) = block[i] switch
            {
                Assign assign => (assign.Value, assign with { Value = new Name(assign.Value.Type, Temporary) }),
                Store store => (store.Value, store with { Value = new Name(store.Value.Type, Temporary) }),
                Return { Value: { } result } => (result, new Return(new Name(result.Type, Temporary))),
                _ => ((IExpr?)null, (IStmt?)null),
            };
            return value is null ? null : block.SetItem(i, rewritten!).Insert(i, new Declare(Temporary, value));
        }

        private static ImmutableArray<IStmt>? DropNullCheck(ImmutableArray<IStmt> block, int i)
        {
            if (block[i] is not If { Condition: NullTest test } branch)
            {
                return null;
            }

            ImmutableArray<IStmt> kept = test.IsNull ? branch.Else : branch.Then;
            return block.RemoveAt(i).InsertRange(i, kept);
        }

        private static ImmutableArray<IStmt>? DropFieldWrite(ImmutableArray<IStmt> block, int i) =>
            block[i] is Assign { Target: Field } ? block.RemoveAt(i) : null;

        private static ImmutableArray<IStmt>? MoveThrow(ImmutableArray<IStmt> block, int i) =>
            i + 1 < block.Length && ((IsEffect(block[i]) && Throws(block[i + 1])) || (Throws(block[i]) && IsEffect(block[i + 1])))
                ? block.SetItem(i, block[i + 1]).SetItem(i + 1, block[i])
                : null;

        public ImmutableArray<IStmt> Block(ImmutableArray<IStmt> block)
        {
            for (int i = 0; i < block.Length; i++)
            {
                if (onBlock(block, i) is { } rewritten && Take())
                {
                    if (site != Everywhere)
                    {
                        return rewritten;
                    }

                    block = rewritten;
                }

                block = block.SetItem(i, Stmt(block[i]));
            }

            return block;
        }

        public IExpr Expr(IExpr expr) => onExpr(expr) is { } rewritten && Take()
            ? rewritten
            : expr switch
            {
                Binary binary => binary with { Left = Expr(binary.Left), Right = Expr(binary.Right) },
                Unary unary => unary with { Operand = Expr(unary.Operand) },
                Conversion conversion => conversion with { Operand = Expr(conversion.Operand) },
                Conditional conditional => new Conditional(Expr(conditional.Condition), Expr(conditional.Then), Expr(conditional.Else)),
                Relation relation => relation with { Left = Expr(relation.Left), Right = Expr(relation.Right) },
                _ => expr,
            };

        private static IExpr? NoExpr(IExpr expr) => null;

        private static ImmutableArray<IStmt>? NoBlock(ImmutableArray<IStmt> block, int i) => null;

        private IStmt Stmt(IStmt statement) => statement switch
        {
            Declare declare => declare with { Value = Expr(declare.Value) },
            Assign assign => assign with { Value = Expr(assign.Value) },
            Store store => store with { Value = Expr(store.Value) },
            Return { Value: { } value } => new Return(Expr(value)),
            If branch => new If(Expr(branch.Condition), Block(branch.Then), Block(branch.Else)),
            Switch choice => new Switch(Expr(choice.Scrutinee), [.. choice.Cases.Select(c => c with { Body = Block(c.Body) })], Block(choice.Default)),
            While loop => loop with { Condition = Expr(loop.Condition), Body = Block(loop.Body) },
            For loop => loop with { Body = Block(loop.Body) },
            _ => statement,
        };

        private bool Take() => site == Everywhere || Seen++ == site;
    }
}
