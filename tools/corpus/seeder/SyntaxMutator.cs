using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Equiv.Corpus.Seeder;

/// <summary>
/// Applies one <see cref="MutationOperator"/> at one site of a method's Roslyn syntax (tickets M0-012, M4-010). The
/// sites of an operator are the nodes it applies to, found in one traversal of <see cref="SyntaxNode.DescendantNodesAndSelf"/>
/// so that <see cref="Sites"/> and <see cref="Apply"/> agree on numbering (both call <see cref="Candidates"/> afresh on
/// the same <paramref name="method"/> instance, so a candidate's closure always resolves against the tree it was found
/// in). Unlike M0-012's original DSL-based mutator, this one works on real C# syntax: no semantic model, so every
/// predicate here is syntactic and deliberately conservative (an operator that might change meaning in a way it cannot
/// see just does not offer that site). Corpus mutants that still fail to compile are caught by
/// <see cref="CompileCheck"/>, not by this class.
/// </summary>
public static class SyntaxMutator
{
    private const string TemporaryName = "equivSeedTemp";

    private static readonly Dictionary<SyntaxKind, SyntaxKind> ComparisonFlips = new()
    {
        [SyntaxKind.LessThanExpression] = SyntaxKind.LessThanOrEqualExpression,
        [SyntaxKind.LessThanOrEqualExpression] = SyntaxKind.LessThanExpression,
        [SyntaxKind.GreaterThanExpression] = SyntaxKind.GreaterThanOrEqualExpression,
        [SyntaxKind.GreaterThanOrEqualExpression] = SyntaxKind.GreaterThanExpression,
        [SyntaxKind.EqualsExpression] = SyntaxKind.NotEqualsExpression,
        [SyntaxKind.NotEqualsExpression] = SyntaxKind.EqualsExpression,
    };

    private static readonly Dictionary<SyntaxKind, SyntaxKind> ComparisonTokens = new()
    {
        [SyntaxKind.LessThanExpression] = SyntaxKind.LessThanToken,
        [SyntaxKind.LessThanOrEqualExpression] = SyntaxKind.LessThanEqualsToken,
        [SyntaxKind.GreaterThanExpression] = SyntaxKind.GreaterThanToken,
        [SyntaxKind.GreaterThanOrEqualExpression] = SyntaxKind.GreaterThanEqualsToken,
        [SyntaxKind.EqualsExpression] = SyntaxKind.EqualsEqualsToken,
        [SyntaxKind.NotEqualsExpression] = SyntaxKind.ExclamationEqualsToken,
    };

    private static readonly SyntaxKind[] ArithmeticKinds =
    [
        SyntaxKind.AddExpression, SyntaxKind.SubtractExpression, SyntaxKind.MultiplyExpression, SyntaxKind.DivideExpression,
        SyntaxKind.ModuloExpression, SyntaxKind.BitwiseAndExpression, SyntaxKind.BitwiseOrExpression, SyntaxKind.ExclusiveOrExpression,
        SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression, SyntaxKind.UnsignedRightShiftExpression,
    ];

    private static readonly SyntaxKind[] CommutativeKinds =
    [
        SyntaxKind.AddExpression, SyntaxKind.MultiplyExpression, SyntaxKind.BitwiseAndExpression,
        SyntaxKind.BitwiseOrExpression, SyntaxKind.ExclusiveOrExpression, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression,
    ];

    private static readonly SyntaxKind[] ShiftKinds = [SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression, SyntaxKind.UnsignedRightShiftExpression];

    /// <summary>The first six members behave identically on every input; the rest usually change behaviour but may not.</summary>
    public static bool IsPreserving(MutationOperator op) => op <= MutationOperator.InlineTemporary;

    public static int Sites(MutationOperator op, MethodDeclarationSyntax method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Candidates(op, method).Count;
    }

    /// <summary>
    /// The method with <paramref name="op"/> applied at <paramref name="site"/> (0-based, per <see cref="Sites"/>), or
    /// <see langword="null"/> if <paramref name="site"/> is out of range.
    /// </summary>
    public static MethodDeclarationSyntax? Apply(MutationOperator op, MethodDeclarationSyntax method, int site)
    {
        ArgumentNullException.ThrowIfNull(method);
        IReadOnlyList<Func<MethodDeclarationSyntax>> candidates = Candidates(op, method);
        return site >= 0 && site < candidates.Count ? candidates[site]() : null;
    }

    private static IReadOnlyList<Func<MethodDeclarationSyntax>> Candidates(MutationOperator op, MethodDeclarationSyntax method)
    {
        return method.Body is null ? [] : op switch
        {
            MutationOperator.RenameLocals => RenameLocalsCandidates(method),
            MutationOperator.ReorderIndependentStatements => AdjacentPairCandidates(method, IsIndependentAssignmentPair, Swap),
            MutationOperator.InvertIf => [.. Nodes<IfStatementSyntax>(method, static _ => true).Select(branch => (Func<MethodDeclarationSyntax>)(() => InvertIf(method, branch)))],
            MutationOperator.Commute => BinaryCandidates(method, IsCommutable, static b => b.WithLeft(b.Right).WithRight(b.Left)),
            MutationOperator.IntroduceTemporary or MutationOperator.InlineTemporary => TemporaryCandidates(method),
            MutationOperator.FlipComparison => BinaryCandidates(method, static b => ComparisonFlips.ContainsKey(b.Kind()), FlipComparison),
            MutationOperator.ChangeConstant => BinaryCandidates(method, IsChangeableConstant, ChangeConstant),
            MutationOperator.DropNullCheck => DropNullCheckCandidates(method),
            MutationOperator.DropFieldWrite => FieldWriteCandidates(method),
            MutationOperator.SwapArguments => BinaryCandidates(method, static b => !ShiftKinds.Contains(b.Kind()), static b => b.WithLeft(b.Right).WithRight(b.Left)),
            _ => MoveThrowCandidates(method),
        };
    }

    // -- RenameLocals: one site, renaming the first local the method declares throughout its body. --

    private static IReadOnlyList<Func<MethodDeclarationSyntax>> RenameLocalsCandidates(MethodDeclarationSyntax method)
    {
        VariableDeclaratorSyntax? first = Nodes<VariableDeclaratorSyntax>(method, static _ => true).FirstOrDefault();
        if (first is null)
        {
            return [];
        }

        string original = first.Identifier.Text;
        string renamed = FreshName(original, UsedNames(method));
        return [() => RenameIdentifier(method, original, renamed)];
    }

    private static MethodDeclarationSyntax RenameIdentifier(MethodDeclarationSyntax method, string from, string to)
    {
        Dictionary<IdentifierNameSyntax, IdentifierNameSyntax> renames = Nodes<IdentifierNameSyntax>(method, id => string.Equals(id.Identifier.Text, from, StringComparison.Ordinal))
            .ToDictionary(id => id, id => SyntaxFactory.IdentifierName(to).WithTriviaFrom(id));
        MethodDeclarationSyntax renamedBody = method.ReplaceNodes(renames.Keys, (original, _) => renames[original]);
        IEnumerable<VariableDeclaratorSyntax> declarators = Nodes<VariableDeclaratorSyntax>(renamedBody, d => string.Equals(d.Identifier.Text, from, StringComparison.Ordinal));
        return renamedBody.ReplaceNodes(declarators, (original, _) => original.WithIdentifier(SyntaxFactory.Identifier(to).WithTriviaFrom(original.Identifier)));
    }

    // -- ReorderIndependentStatements: swap two adjacent simple assignments that cannot interact. --

    private static bool IsIndependentAssignmentPair(StatementSyntax first, StatementSyntax second) =>
        TryAssignment(first, out AssignmentExpressionSyntax? a) && TryAssignment(second, out AssignmentExpressionSyntax? b)
        && IsSimple(a!.Right) && IsSimple(b!.Right)
        && !string.Equals(TargetName(a.Left), TargetName(b.Left), StringComparison.Ordinal)
        && !Reads(a.Right).Contains(TargetName(b.Left)) && !Reads(b.Right).Contains(TargetName(a.Left));

    private static bool TryAssignment(StatementSyntax statement, out AssignmentExpressionSyntax? assignment)
    {
        if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } a })
        {
            assignment = a;
            return true;
        }

        assignment = null;
        return false;
    }

    private static MethodDeclarationSyntax Swap(MethodDeclarationSyntax method, StatementSyntax first, StatementSyntax second) =>
        method.ReplaceNodes([first, second], (original, _) => ReferenceEquals(original, first) ? second : first);

    // -- InvertIf: if (c) A else B -> if (!c) B else A. --

    private static MethodDeclarationSyntax InvertIf(MethodDeclarationSyntax method, IfStatementSyntax branch)
    {
        ExpressionSyntax inverted = SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, Parenthesize(branch.Condition));
        StatementSyntax newThen = branch.Else?.Statement ?? SyntaxFactory.Block();
        StatementSyntax newElse = branch.Statement;
        IfStatementSyntax rewritten = branch.WithCondition(inverted).WithStatement(newThen).WithElse(SyntaxFactory.ElseClause(newElse));

        // The brand new "else" keyword carries no trivia of its own; without normalising, it could sit directly
        // against whatever newElse starts with (e.g. "elsereturn"), which would lex as one identifier.
        return method.ReplaceNode(branch, rewritten.NormalizeWhitespace().WithTriviaFrom(branch));
    }

    private static ExpressionSyntax Parenthesize(ExpressionSyntax expr) => expr is ParenthesizedExpressionSyntax ? expr : SyntaxFactory.ParenthesizedExpression(expr);

    // -- Commute / FlipComparison / ChangeConstant / SwapArguments: single BinaryExpressionSyntax rewrites. --

    private static bool IsCommutable(BinaryExpressionSyntax binary) => CommutativeKinds.Contains(binary.Kind()) && IsSimple(binary.Left) && IsSimple(binary.Right);

    private static BinaryExpressionSyntax FlipComparison(BinaryExpressionSyntax binary)
    {
        SyntaxKind kind = ComparisonFlips[binary.Kind()];
        return SyntaxFactory.BinaryExpression(kind, binary.Left, SyntaxFactory.Token(ComparisonTokens[kind]), binary.Right);
    }

    /// <summary>A binary op in <see cref="ArithmeticKinds"/> whose right operand is an integer or long literal, however many parens surround it.</summary>
    private static bool IsChangeableConstant(BinaryExpressionSyntax binary) =>
        ArithmeticKinds.Contains(binary.Kind()) && Unwrap(binary.Right) is LiteralExpressionSyntax { Token.Value: int or long or uint or ulong };

    /// <summary>A divisor never becomes zero: -1 moves to -2 instead of 0.</summary>
    private static BinaryExpressionSyntax ChangeConstant(BinaryExpressionSyntax binary)
    {
        LiteralExpressionSyntax literal = (LiteralExpressionSyntax)Unwrap(binary.Right);
        object boxed = literal.Token.Value!;
        bool isDivisor = binary.Kind() is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression;
        LiteralExpressionSyntax changed = boxed switch
        {
            int i => Literal(i == -1 && isDivisor ? i - 1 : i + 1),
            long l => Literal(l == -1 && isDivisor ? l - 1 : l + 1),
            uint u => Literal(unchecked(u + 1)),
            ulong ul => Literal(unchecked(ul + 1)),
            _ => literal,
        };
        return binary.WithRight(changed);
    }

    private static LiteralExpressionSyntax Literal(int value) => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(value));

    private static LiteralExpressionSyntax Literal(long value) => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(value));

    private static LiteralExpressionSyntax Literal(uint value) => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(value));

    private static LiteralExpressionSyntax Literal(ulong value) => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(value));

    // -- IntroduceTemporary / InlineTemporary: t = e; then use t. Both operators apply the same rewrite (M0-012 Notes). --

    private static List<Func<MethodDeclarationSyntax>> TemporaryCandidates(MethodDeclarationSyntax method)
    {
        HashSet<string> used = UsedNames(method);
        List<Func<MethodDeclarationSyntax>> candidates = [];
        foreach (StatementSyntax statement in Nodes<StatementSyntax>(method, static _ => true))
        {
            (ExpressionSyntax? value, Func<ExpressionSyntax, StatementSyntax>? rebuild) = statement switch
            {
                ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } a } es =>
                    (a.Right, (Func<ExpressionSyntax, StatementSyntax>)(replacement => es.WithExpression(a.WithRight(replacement)))),
                ReturnStatementSyntax { Expression: { } result } ret => (result, replacement => ret.WithExpression(replacement)),
                _ => (null, null),
            };
            if (value is null || !IsSimple(value))
            {
                continue;
            }

            StatementSyntax target = statement;
            candidates.Add(() => IntroduceTemporary(method, target, value, rebuild!, FreshName(TemporaryName, used)));
        }

        return candidates;
    }

    /// <summary>
    /// Brand new tokens carry no trivia of their own, and Roslyn never inserts a space just because two tokens would
    /// otherwise merge (<c>var</c> immediately followed by the temporary's name would lex as one identifier), so the
    /// declaration is built, normalised for its own internal spacing, and then given the target statement's
    /// indentation; the temporary's use sites reuse <paramref name="value"/>'s own trivia, since they are replacing it.
    /// </summary>
    private static MethodDeclarationSyntax IntroduceTemporary(MethodDeclarationSyntax method, StatementSyntax target, ExpressionSyntax value, Func<ExpressionSyntax, StatementSyntax> rebuild, string temporary)
    {
        LocalDeclarationStatementSyntax declare = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(temporary)).WithInitializer(SyntaxFactory.EqualsValueClause(value.WithoutTrivia())))))
            .NormalizeWhitespace()
            .WithTriviaFrom(target);
        StatementSyntax rewritten = rebuild(SyntaxFactory.IdentifierName(temporary).WithTriviaFrom(value));
        return method.ReplaceNode(target, new StatementSyntax[] { declare, rewritten });
    }

    // -- DropNullCheck: if (x == null) A else B -> the branch a non-null x takes. --

    private static IReadOnlyList<Func<MethodDeclarationSyntax>> DropNullCheckCandidates(MethodDeclarationSyntax method) =>
        [.. Nodes<IfStatementSyntax>(method, static branch => Unwrap(branch.Condition) is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression or (int)SyntaxKind.NotEqualsExpression } b
                && (b.Left.IsKind(SyntaxKind.NullLiteralExpression) || b.Right.IsKind(SyntaxKind.NullLiteralExpression)))
            .Select(branch => (Func<MethodDeclarationSyntax>)(() => DropNullCheck(method, branch)))];

    private static MethodDeclarationSyntax DropNullCheck(MethodDeclarationSyntax method, IfStatementSyntax branch)
    {
        bool isEqualsNull = ((BinaryExpressionSyntax)Unwrap(branch.Condition)).IsKind(SyntaxKind.EqualsExpression);
        StatementSyntax kept = isEqualsNull ? branch.Else?.Statement ?? SyntaxFactory.Block() : branch.Statement;
        return method.ReplaceNode(branch, UnwrapBlock(kept));
    }

    // -- DropFieldWrite: removes one write of something the method itself does not declare. --

    private static IReadOnlyList<Func<MethodDeclarationSyntax>> FieldWriteCandidates(MethodDeclarationSyntax method)
    {
        HashSet<string> locals = LocalAndParameterNames(method);
        return [.. Nodes<ExpressionStatementSyntax>(method, es => IsFieldWrite(es, locals))
            .Select(es => (Func<MethodDeclarationSyntax>)(() => RemoveStatement(method, es)))];
    }

    private static bool IsFieldWrite(ExpressionStatementSyntax statement, HashSet<string> locals) =>
        statement.Expression is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } a && IsFieldLikeTarget(a.Left, locals);

    private static bool IsFieldLikeTarget(ExpressionSyntax target, HashSet<string> locals) => target switch
    {
        MemberAccessExpressionSyntax => true,
        IdentifierNameSyntax id => !locals.Contains(id.Identifier.Text),
        _ => false,
    };

    private static MethodDeclarationSyntax RemoveStatement(MethodDeclarationSyntax method, StatementSyntax statement) =>
        method.RemoveNode(statement, SyntaxRemoveOptions.KeepNoTrivia) ?? method;

    // -- MoveThrowAcrossSideEffect: swaps an adjacent throwing guard and a field/array write. --

    private static List<Func<MethodDeclarationSyntax>> MoveThrowCandidates(MethodDeclarationSyntax method)
    {
        HashSet<string> locals = LocalAndParameterNames(method);
        bool IsEffect(StatementSyntax s) => s is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } a }
            && (IsFieldLikeTarget(a.Left, locals) || a.Left is ElementAccessExpressionSyntax);
        bool Pair(StatementSyntax first, StatementSyntax second) =>
            (IsThrowingGuard(first) && IsEffect(second)) || (IsEffect(first) && IsThrowingGuard(second));
        return AdjacentPairCandidates(method, Pair, Swap);
    }

    private static bool IsThrowingGuard(StatementSyntax statement) =>
        statement is IfStatementSyntax branch && (ContainsThrow(branch.Statement) || (branch.Else is { } e && ContainsThrow(e.Statement)));

    private static bool ContainsThrow(StatementSyntax statement) =>
        statement is ThrowStatementSyntax || (statement is BlockSyntax block && block.Statements.Any(static s => s is ThrowStatementSyntax));

    // -- Shared traversal and safety helpers. --

    /// <summary>Every node of type <typeparamref name="TNode"/> in the method's body, never crossing into a nested lambda or local function.</summary>
    private static IEnumerable<TNode> Nodes<TNode>(MethodDeclarationSyntax method, Func<TNode, bool> matches)
        where TNode : SyntaxNode =>
        method.Body!.DescendantNodesAndSelf(DoesNotCrossScope).OfType<TNode>().Where(matches);

    /// <summary>Every expression this deep cannot itself throw: no calls, indexers, casts, awaits, checked contexts or division/modulo.</summary>
    private static bool IsSimple(ExpressionSyntax expr) => !expr.DescendantNodesAndSelf().Any(static n => n switch
    {
        InvocationExpressionSyntax or ElementAccessExpressionSyntax or ObjectCreationExpressionSyntax or CastExpressionSyntax
            or AwaitExpressionSyntax or AssignmentExpressionSyntax or CheckedExpressionSyntax or ConditionalAccessExpressionSyntax
            or ThrowExpressionSyntax or PostfixUnaryExpressionSyntax => true,
        PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression } => true,
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.DivideExpression) || b.IsKind(SyntaxKind.ModuloExpression) => true,
        _ => false,
    });

    private static HashSet<string> Reads(ExpressionSyntax expr) => [.. expr.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Select(static id => id.Identifier.Text)];

    private static string TargetName(ExpressionSyntax target) => target is IdentifierNameSyntax id ? id.Identifier.Text : target.ToString();

    private static HashSet<string> LocalAndParameterNames(MethodDeclarationSyntax method)
    {
        HashSet<string> names = [.. method.ParameterList.Parameters.Select(static p => p.Identifier.Text)];
        names.UnionWith(Nodes<VariableDeclaratorSyntax>(method, static _ => true).Select(static d => d.Identifier.Text));
        names.UnionWith(Nodes<CatchDeclarationSyntax>(method, static c => c.Identifier.IsKind(SyntaxKind.IdentifierToken)).Select(static c => c.Identifier.Text));
        names.UnionWith(Nodes<ForEachStatementSyntax>(method, static _ => true).Select(static f => f.Identifier.Text));
        return names;
    }

    private static HashSet<string> UsedNames(MethodDeclarationSyntax method) => [.. method.DescendantTokens().Where(static t => t.IsKind(SyntaxKind.IdentifierToken)).Select(static t => t.Text)];

    private static string FreshName(string baseName, HashSet<string> used)
    {
        if (!used.Contains(baseName))
        {
            return baseName;
        }

        int suffix = 0;
        string candidate;
        do
        {
            candidate = baseName + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            suffix++;
        }
        while (used.Contains(candidate));

        return candidate;
    }

    /// <summary>Never descends into a nested lambda, local function or anonymous method: their locals are a separate scope.</summary>
    private static bool DoesNotCrossScope(SyntaxNode node) => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);

    private static SyntaxList<StatementSyntax> UnwrapBlock(StatementSyntax statement) => statement is BlockSyntax block ? block.Statements : SyntaxFactory.SingletonList(statement);

    /// <summary>
    /// Strips any number of surrounding parens. PairGen's own renderer (ticket M0-012) wraps every non-leaf expression
    /// in its own parens on top of whatever the caller adds (an <c>if</c>'s condition, a binary operand), so a direct
    /// child-node pattern match would otherwise never see through to the real shape; real corpus code parenthesises
    /// this way too on occasion.
    /// </summary>
    private static ExpressionSyntax Unwrap(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax parenthesized)
        {
            expr = parenthesized.Expression;
        }

        return expr;
    }

    private static IReadOnlyList<Func<MethodDeclarationSyntax>> BinaryCandidates(
        MethodDeclarationSyntax method, Func<BinaryExpressionSyntax, bool> matches, Func<BinaryExpressionSyntax, BinaryExpressionSyntax> rewrite) =>
        [.. Nodes<BinaryExpressionSyntax>(method, matches).Select(target => (Func<MethodDeclarationSyntax>)(() => method.ReplaceNode(target, rewrite(target).WithTriviaFrom(target))))];

    private static List<Func<MethodDeclarationSyntax>> AdjacentPairCandidates(
        MethodDeclarationSyntax method, Func<StatementSyntax, StatementSyntax, bool> pairs, Func<MethodDeclarationSyntax, StatementSyntax, StatementSyntax, MethodDeclarationSyntax> apply)
    {
        List<Func<MethodDeclarationSyntax>> candidates = [];
        foreach (BlockSyntax block in Nodes<BlockSyntax>(method, static _ => true))
        {
            for (int i = 0; i + 1 < block.Statements.Count; i++)
            {
                StatementSyntax first = block.Statements[i];
                StatementSyntax second = block.Statements[i + 1];
                if (pairs(first, second))
                {
                    candidates.Add(() => apply(method, first, second));
                }
            }
        }

        return candidates;
    }
}
