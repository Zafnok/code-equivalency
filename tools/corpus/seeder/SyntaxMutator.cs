using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Equiv.Corpus.Seeder;

/// <summary>
/// Applies one <see cref="MutationOperator"/> at one site of a method's Roslyn syntax (tickets M0-012, M4-010). The
/// sites of an operator are the nodes it applies to, found in one traversal of <see cref="SyntaxNode.DescendantNodesAndSelf"/>
/// so that <see cref="Sites"/> and <see cref="Apply"/> agree on numbering (both call <see cref="Candidates"/> afresh on
/// the same <paramref name="method"/> instance, so a candidate's closure always resolves against the tree it was found
/// in). Unlike M0-012's original DSL-based mutator, this one works on real C# syntax, and its predicates are
/// deliberately conservative (an operator that might change meaning in a way it cannot see just does not offer that
/// site). The M0-012 operators are purely syntactic. The P2-048 cleanup operators also need types (a conditional's
/// branches of exactly the target's type, <c>string</c> operands, an array), so they bind the method's own file
/// against the BCL alone (<see cref="Model"/>): a type that does not resolve there is an error type, and no site
/// depends on one. Corpus mutants that still fail to compile are caught by <see cref="CompileCheck"/>, not by this class.
/// </summary>
public static class SyntaxMutator
{
    private const string TemporaryName = "equivSeedTemp";

    private const string ItemName = "item";

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

    /// <summary>The members up to <see cref="MutationOperator.ForToForeach"/> behave identically on every input; the rest usually change behaviour but may not.</summary>
    public static bool IsPreserving(MutationOperator op) => op <= MutationOperator.ForToForeach;

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
            MutationOperator.IfToConditional => IfToConditionalCandidates(method),
            MutationOperator.CoalesceNullCheck => CoalesceCandidates(method),
            MutationOperator.ConcatToInterpolation => InterpolationCandidates(method),
            MutationOperator.GuardClause => GuardClauseCandidates(method),
            MutationOperator.ForToForeach => ForeachCandidates(method),
            MutationOperator.FlipComparison => BinaryCandidates(method, static b => ComparisonFlips.ContainsKey(b.Kind()), FlipComparison),
            MutationOperator.ChangeConstant => BinaryCandidates(method, IsChangeableConstant, ChangeConstant),
            MutationOperator.DropNullCheck => DropNullCheckCandidates(method),
            MutationOperator.DropFieldWrite => FieldWriteCandidates(method),
            MutationOperator.SwapArguments => BinaryCandidates(method, static b => !ShiftKinds.Contains(b.Kind()), static b => b.WithLeft(b.Right).WithRight(b.Left)),
            _ => MoveThrowCandidates(method),
        };
    }

    // -- RenameLocals: one site, renaming the first local the method declares, and never names in an argument, throughout its body. --

    private static IReadOnlyList<Func<MethodDeclarationSyntax>> RenameLocalsCandidates(MethodDeclarationSyntax method)
    {
        VariableDeclaratorSyntax? first = Nodes<VariableDeclaratorSyntax>(method, d => !IsNamedInArgument(method, d.Identifier.Text)).FirstOrDefault();
        if (first is null)
        {
            return [];
        }

        string original = first.Identifier.Text;
        string renamed = FreshName(original, UsedNames(method));
        return [() => RenameIdentifier(method, original, renamed)];
    }

    /// <summary>
    /// Whether <paramref name="name"/> is written anywhere inside an argument in the body, lambdas included. A parameter
    /// marked <c>[CallerArgumentExpression]</c> receives its argument's source text (MSTest 4's <c>Assert.AreEqual</c>
    /// does), and <c>nameof(x)</c> is an argument too, so renaming a local written there changes a string the program
    /// sees: the rename is then not behaviour-preserving (ticket P2-036). With no semantic model the callee cannot be
    /// checked, so every argument counts.
    /// </summary>
    private static bool IsNamedInArgument(MethodDeclarationSyntax method, string name) =>
        method.Body!.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Any(id => string.Equals(id.Identifier.Text, name, StringComparison.Ordinal) && id.FirstAncestorOrSelf<ArgumentSyntax>() is not null);

    private static MethodDeclarationSyntax RenameIdentifier(MethodDeclarationSyntax method, string from, string to)
    {
        Dictionary<IdentifierNameSyntax, IdentifierNameSyntax> renames = Nodes<IdentifierNameSyntax>(method, id => string.Equals(id.Identifier.Text, from, StringComparison.Ordinal))
            .ToDictionary(id => id, id => SyntaxFactory.IdentifierName(to).WithTriviaFrom(id));
        MethodDeclarationSyntax renamedBody = method.ReplaceNodes(renames.Keys, (original, _) => renames[original]);
        IEnumerable<VariableDeclaratorSyntax> declarators = Nodes<VariableDeclaratorSyntax>(renamedBody, d => string.Equals(d.Identifier.Text, from, StringComparison.Ordinal));
        return renamedBody.ReplaceNodes(declarators, (original, _) => original.WithIdentifier(SyntaxFactory.Identifier(to).WithTriviaFrom(original.Identifier)));
    }

    // -- ReorderIndependentStatements: swap two adjacent simple assignments that cannot interact. --

    /// <summary>
    /// Both statements must be simple <c>name = value;</c> assignments: the target itself must be a bare identifier,
    /// never <c>u[i]</c> or <c>obj.Field</c>, because evaluating those can throw (index out of range, null reference)
    /// and swapping them would move that exception across the other statement's effect.
    /// </summary>
    private static bool IsIndependentAssignmentPair(StatementSyntax first, StatementSyntax second) =>
        TryAssignment(first, out AssignmentExpressionSyntax? a) && TryAssignment(second, out AssignmentExpressionSyntax? b)
        && a!.Left is IdentifierNameSyntax && b!.Left is IdentifierNameSyntax
        && IsSimple(a.Right) && IsSimple(b.Right)
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

    // -- InvertIf: an if/else with condition c and branches A, B becomes the negated condition with B, A. --

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
            if (value is null || !IsSimple(value) || !IsListElement(statement))
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
        return method.ReplaceNode(target, [declare, rewritten]);
    }

    // -- IfToConditional: an if/else whose branches assign one local, or both return, becomes one ?: (ticket P2-048). --

    /// <summary>
    /// Each branch is one statement, braced or not. Both branches' values must have exactly the target's type (the
    /// local's or parameter's, or the method's return type), so the conditional's type is that type and no conversion
    /// moves into or out of it. An <c>async</c> method's <c>return</c> is not its declared type, and a <c>ref</c> return
    /// is not a value, so neither offers the return form.
    /// </summary>
    private static List<Func<MethodDeclarationSyntax>> IfToConditionalCandidates(MethodDeclarationSyntax method)
    {
        List<(IfStatementSyntax Branch, StatementSyntax Then, StatementSyntax Else)> shapes = [];
        foreach (IfStatementSyntax branch in Nodes<IfStatementSyntax>(method, static _ => true))
        {
            if (Only(branch.Statement) is { } then && branch.Else is { } otherwise && Only(otherwise.Statement) is { } other && IsConditionalShape(then, other))
            {
                shapes.Add((branch, then, other));
            }
        }

        if (shapes.Count == 0)
        {
            return [];
        }

        SemanticModel model = Model(method);
        List<Func<MethodDeclarationSyntax>> candidates = [];
        foreach ((IfStatementSyntax branch, StatementSyntax then, StatementSyntax other) in shapes)
        {
            StatementSyntax? rewritten = (then, other) switch
            {
                (ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax a } assign, ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax b })
                    when LocalOrParameterType(model, a.Left) is { } type && HasExactType(model, a.Right, type) && HasExactType(model, b.Right, type) =>
                    assign.WithExpression(a.WithRight(Conditional(branch.Condition, a.Right, b.Right))),
                (ReturnStatementSyntax { Expression: { } p } ret, ReturnStatementSyntax { Expression: { } q })
                    when ReturnType(model, method) is { } type && HasExactType(model, p, type) && HasExactType(model, q, type) =>
                    ret.WithExpression(Conditional(branch.Condition, p, q)),
                _ => null,
            };
            if (rewritten is not null)
            {
                candidates.Add(() => method.ReplaceNode(branch, rewritten.NormalizeWhitespace().WithTriviaFrom(branch)));
            }
        }

        return candidates;
    }

    /// <summary>Two simple assignments to one bare name, or two <c>return</c>s of a value.</summary>
    private static bool IsConditionalShape(StatementSyntax then, StatementSyntax other) => (then, other) switch
    {
        (ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression, Left: IdentifierNameSyntax x } },
            ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression, Left: IdentifierNameSyntax y } }) => SameName(x, y),
        (ReturnStatementSyntax { Expression: not null }, ReturnStatementSyntax { Expression: not null }) => true,
        _ => false,
    };

    /// <summary>The statement itself, or the one statement of a block that has exactly one; otherwise null.</summary>
    private static StatementSyntax? Only(StatementSyntax statement) => statement switch
    {
        BlockSyntax { Statements.Count: 1 } block => block.Statements[0],
        BlockSyntax => null,
        _ => statement,
    };

    private static ITypeSymbol? ReturnType(SemanticModel model, MethodDeclarationSyntax method) =>
        method.Modifiers.Any(SyntaxKind.AsyncKeyword) || method.ReturnType is RefTypeSyntax ? null : model.GetDeclaredSymbol(method)?.ReturnType;

    private static ConditionalExpressionSyntax Conditional(ExpressionSyntax condition, ExpressionSyntax whenTrue, ExpressionSyntax whenFalse) =>
        SyntaxFactory.ConditionalExpression(AsOperand(condition), AsOperand(whenTrue), AsOperand(whenFalse));

    // -- CoalesceNullCheck: x != null ? x : y, or x == null ? y : x, becomes x ?? y (ticket P2-048). --

    /// <summary>
    /// <c>x</c> must be a reference-typed local or parameter (a nullable value type's <c>??</c> unwraps, which changes
    /// the result's type) and <c>y</c> of exactly its type, so both forms have one type. The null test must be the
    /// built-in reference comparison, or <c>string</c>'s, which is the same on <c>null</c>; <c>??</c> never calls a
    /// user-defined <c>==</c>, so a type with one offers no site.
    /// </summary>
    private static List<Func<MethodDeclarationSyntax>> CoalesceCandidates(MethodDeclarationSyntax method)
    {
        List<(ConditionalExpressionSyntax Conditional, BinaryExpressionSyntax Test, IdentifierNameSyntax Tested, ExpressionSyntax Fallback)> shapes = [];
        foreach (ConditionalExpressionSyntax conditional in Nodes<ConditionalExpressionSyntax>(method, static _ => true))
        {
            if (CoalesceParts(conditional) is { } parts)
            {
                shapes.Add((conditional, parts.Test, parts.Tested, parts.Fallback));
            }
        }

        if (shapes.Count == 0)
        {
            return [];
        }

        SemanticModel model = Model(method);
        return [.. shapes
            .Where(s => LocalOrParameterType(model, s.Tested) is { IsReferenceType: true } type && HasExactType(model, s.Fallback, type) && IsReferenceNullTest(model, s.Test))
            .Select(s => (Func<MethodDeclarationSyntax>)(() => method.ReplaceNode(
                s.Conditional,
                SyntaxFactory.BinaryExpression(SyntaxKind.CoalesceExpression, s.Tested.WithoutTrivia(), AsOperand(s.Fallback)).NormalizeWhitespace().WithTriviaFrom(s.Conditional))))];
    }

    private static (BinaryExpressionSyntax Test, IdentifierNameSyntax Tested, ExpressionSyntax Fallback)? CoalesceParts(ConditionalExpressionSyntax conditional)
    {
        if (Unwrap(conditional.Condition) is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression or (int)SyntaxKind.NotEqualsExpression } test)
        {
            return null;
        }

        ExpressionSyntax? tested = null;
        if (test.Right.IsKind(SyntaxKind.NullLiteralExpression))
        {
            tested = test.Left;
        }
        else if (test.Left.IsKind(SyntaxKind.NullLiteralExpression))
        {
            tested = test.Right;
        }

        bool isNotNull = test.IsKind(SyntaxKind.NotEqualsExpression);
        ExpressionSyntax kept = isNotNull ? conditional.WhenTrue : conditional.WhenFalse;
        ExpressionSyntax fallback = isNotNull ? conditional.WhenFalse : conditional.WhenTrue;
        return tested is not null && Unwrap(tested) is IdentifierNameSyntax x && Unwrap(kept) is IdentifierNameSyntax k && SameName(x, k) ? (test, x, fallback) : null;
    }

    private static bool IsReferenceNullTest(SemanticModel model, BinaryExpressionSyntax test) =>
        model.GetSymbolInfo(test).Symbol is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator } or IMethodSymbol { ContainingType.SpecialType: SpecialType.System_String };

    // -- ConcatToInterpolation: a + chain of strings becomes one interpolated string (ticket P2-048). --

    /// <summary>
    /// Every operand must be <c>string</c>, so no <c>ToString</c> (culture-sensitive or user-defined) enters on either
    /// side, and both sides treat a <c>null</c> operand as empty. A chain Roslyn folds to a constant offers no site,
    /// because an interpolated constant needs a newer language version than the code may use; nor does one with an
    /// operand that spans lines, which a non-verbatim interpolation hole cannot hold before C# 11.
    /// </summary>
    private static List<Func<MethodDeclarationSyntax>> InterpolationCandidates(MethodDeclarationSyntax method)
    {
        BinaryExpressionSyntax[] chains = [.. Nodes<BinaryExpressionSyntax>(method, static b => b.IsKind(SyntaxKind.AddExpression) && b.Parent is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression })];
        if (chains.Length == 0)
        {
            return [];
        }

        SemanticModel model = Model(method);
        ITypeSymbol text = model.Compilation.GetSpecialType(SpecialType.System_String);
        List<Func<MethodDeclarationSyntax>> candidates = [];
        foreach (BinaryExpressionSyntax chain in chains)
        {
            ExpressionSyntax[] operands = [.. ChainOperands(chain)];
            if (operands.All(o => HasExactType(model, o, text) && !o.WithoutTrivia().ToFullString().Contains('\n', StringComparison.Ordinal)) && !model.GetConstantValue(chain).HasValue)
            {
                candidates.Add(() => method.ReplaceNode(chain, Interpolation(operands).WithTriviaFrom(chain)));
            }
        }

        return candidates;
    }

    private static IEnumerable<ExpressionSyntax> ChainOperands(ExpressionSyntax expr) =>
        expr is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } add ? ChainOperands(add.Left).Concat(ChainOperands(add.Right)) : [expr];

    private static InterpolatedStringExpressionSyntax Interpolation(IEnumerable<ExpressionSyntax> operands) =>
        SyntaxFactory.InterpolatedStringExpression(SyntaxFactory.Token(SyntaxKind.InterpolatedStringStartToken), SyntaxFactory.List(operands.Select(InterpolationPart)));

    /// <summary>A plain (not verbatim, not raw) string literal becomes text, its braces doubled; anything else a hole.</summary>
    private static InterpolatedStringContentSyntax InterpolationPart(ExpressionSyntax operand)
    {
        if (operand is LiteralExpressionSyntax { Token: { RawKind: (int)SyntaxKind.StringLiteralToken } token } && token.Text.StartsWith('"'))
        {
            string text = token.Text[1..^1].Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
            return SyntaxFactory.InterpolatedStringText(SyntaxFactory.Token(default, SyntaxKind.InterpolatedStringTextToken, text, token.ValueText, default));
        }

        // A top-level "::" (global::X) would end the hole as a format specifier's ':' does.
        ExpressionSyntax hole = AsOperand(operand);
        return SyntaxFactory.Interpolation(hole is ParenthesizedExpressionSyntax || !hole.DescendantTokens().Any(static t => t.IsKind(SyntaxKind.ColonColonToken)) ? hole : SyntaxFactory.ParenthesizedExpression(hole));
    }

    // -- GuardClause: a void method's trailing if (c) { S } becomes if (!c) return; S (ticket P2-048). --

    /// <summary>
    /// The condition must be <c>bool</c>, so that <c>!</c> negates what <c>if</c> tested (a type with its own
    /// <c>operator true</c> could disagree). <c>S</c>'s declarations move out to the method's scope, so no name one of
    /// them declares may appear anywhere else in the method, where it would clash or be read before it is declared.
    /// </summary>
    private static List<Func<MethodDeclarationSyntax>> GuardClauseCandidates(MethodDeclarationSyntax method)
    {
        if (method.ReturnType is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword }
            || method.Body!.Statements.LastOrDefault() is not IfStatementSyntax { Else: null } last)
        {
            return [];
        }

        HashSet<string> moved = [.. last.Statement.DescendantNodes().Select(DeclaredName).OfType<string>()];
        bool clashes = method.DescendantTokens().Any(t => t.IsKind(SyntaxKind.IdentifierToken) && !last.Statement.FullSpan.Contains(t.SpanStart) && moved.Contains(t.Text));
        return clashes || Model(method).GetTypeInfo(last.Condition).Type is not { SpecialType: SpecialType.System_Boolean } ? [] : [() => GuardClause(method, last)];
    }

    private static string? DeclaredName(SyntaxNode node) => node switch
    {
        VariableDeclaratorSyntax d => d.Identifier.Text,
        SingleVariableDesignationSyntax v => v.Identifier.Text,
        ForEachStatementSyntax f => f.Identifier.Text,
        CatchDeclarationSyntax c => c.Identifier.Text,
        LocalFunctionStatementSyntax l => l.Identifier.Text,
        ParameterSyntax p => p.Identifier.Text,
        LabeledStatementSyntax l => l.Identifier.Text,
        _ => null,
    };

    private static MethodDeclarationSyntax GuardClause(MethodDeclarationSyntax method, IfStatementSyntax last)
    {
        IfStatementSyntax guard = SyntaxFactory.IfStatement(SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, AsOperand(last.Condition)), SyntaxFactory.ReturnStatement())
            .NormalizeWhitespace()
            .WithTriviaFrom(last);
        return method.ReplaceNode(last, [guard, .. UnwrapBlock(last.Statement)]);
    }

    // -- ForToForeach: an index loop over an array that only reads a[i] becomes a foreach (ticket P2-048). --

    /// <summary>
    /// <c>a</c> must be a single-dimensional array held in a local or by-value parameter that the body never writes and
    /// that nothing in the method can write behind the loop's back (no lambda or local function names it, no <c>ref</c>
    /// is taken to it), since <c>foreach</c> reads <c>a</c> once and the <c>for</c> reads it every iteration. Every use
    /// of <c>i</c> is an <c>a[i]</c> read outside any lambda (a captured <c>i</c> is one variable, a captured item one
    /// per iteration), never written or passed by reference, and, for a value-type element, never the receiver of a
    /// member access, which could mutate the element in place where the item is a copy.
    /// </summary>
    private static List<Func<MethodDeclarationSyntax>> ForeachCandidates(MethodDeclarationSyntax method)
    {
        List<(ForStatementSyntax Loop, string Index, IdentifierNameSyntax Array)> shapes = [];
        foreach (ForStatementSyntax loop in Nodes<ForStatementSyntax>(method, static _ => true))
        {
            if (ArrayLoop(loop) is { } shape)
            {
                shapes.Add((loop, shape.Index, shape.Array));
            }
        }

        if (shapes.Count == 0)
        {
            return [];
        }

        SemanticModel model = Model(method);
        HashSet<string> used = UsedNames(method);
        return [.. shapes
            .Where(s => IsForeachable(method, model, s.Loop, s.Index, s.Array))
            .Select(s => (Func<MethodDeclarationSyntax>)(() => ToForeach(method, s.Loop, s.Index, s.Array, FreshName(ItemName, used))))];
    }

    private static (string Index, IdentifierNameSyntax Array)? ArrayLoop(ForStatementSyntax loop)
    {
        if (loop is not
            {
                Declaration: { Type: PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.IntKeyword }, Variables: [{ Initializer.Value: LiteralExpressionSyntax { Token.Value: 0 } } index] },
                Initializers.Count: 0,
                Condition: BinaryExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.LessThanExpression,
                    Left: IdentifierNameSyntax bound,
                    Right: MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression, Expression: IdentifierNameSyntax array, Name.Identifier.Text: "Length" },
                },
                Incrementors: [ExpressionSyntax increment],
            })
        {
            return null;
        }

        ExpressionSyntax? step = increment switch
        {
            PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PostIncrementExpression } post => post.Operand,
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression } pre => pre.Operand,
            _ => null,
        };
        string name = index.Identifier.Text;
        return step is IdentifierNameSyntax stepped && string.Equals(bound.Identifier.Text, name, StringComparison.Ordinal) && string.Equals(stepped.Identifier.Text, name, StringComparison.Ordinal)
            ? (name, array)
            : null;
    }

    private static bool IsForeachable(MethodDeclarationSyntax method, SemanticModel model, ForStatementSyntax loop, string index, IdentifierNameSyntax array)
    {
        if (ArrayType(model, array) is not { } type)
        {
            return false;
        }

        string name = array.Identifier.Text;
        bool escapes = method.Body!.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Any(id => string.Equals(id.Identifier.Text, name, StringComparison.Ordinal)
                && (IsInsideNestedFunction(id, method.Body) || IsByReference(id) || (loop.Statement.Span.Contains(id.Span) && IsWritten(id))));
        return !escapes && loop.Statement.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Where(id => string.Equals(id.Identifier.Text, index, StringComparison.Ordinal))
            .All(id => !IsInsideNestedFunction(id, loop.Statement) && LoopRead(id, name) is { } read && !IsWritten(read)
                && (type.ElementType.IsReferenceType || read.Parent is not (MemberAccessExpressionSyntax or ConditionalAccessExpressionSyntax)));
    }

    /// <summary>The type of a single-dimensional array held in a by-value local or parameter, or null.</summary>
    private static IArrayTypeSymbol? ArrayType(SemanticModel model, IdentifierNameSyntax array) => model.GetSymbolInfo(array).Symbol switch
    {
        ILocalSymbol { IsRef: false, Type: IArrayTypeSymbol { Rank: 1 } local } => local,
        IParameterSymbol { RefKind: RefKind.None, Type: IArrayTypeSymbol { Rank: 1 } parameter } => parameter,
        _ => null,
    };

    /// <summary>The <c>a[i]</c> whose whole argument <paramref name="index"/> is, or null.</summary>
    private static ElementAccessExpressionSyntax? LoopRead(IdentifierNameSyntax index, string array) =>
        index.Parent is ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.None, NameColon: null, Parent: BracketedArgumentListSyntax { Arguments.Count: 1, Parent: ElementAccessExpressionSyntax { Expression: IdentifierNameSyntax target } read } }
            && string.Equals(target.Identifier.Text, array, StringComparison.Ordinal) ? read : null;

    private static bool IsInsideNestedFunction(SyntaxNode node, SyntaxNode scope) =>
        node.Ancestors().TakeWhile(a => a != scope).Any(static a => a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);

    private static bool IsByReference(ExpressionSyntax expr) =>
        (expr.Parent is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None)) || expr.Parent is RefExpressionSyntax;

    private static bool IsWritten(ExpressionSyntax expr) => IsByReference(expr) || expr.Parent switch
    {
        AssignmentExpressionSyntax assignment => assignment.Left == expr,
        PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression } => true,
        PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PostIncrementExpression or (int)SyntaxKind.PostDecrementExpression } => true,
        _ => false,
    };

    /// <summary>
    /// The header is built alone and normalised, since brand new tokens carry no trivia (<c>var</c> against the item's
    /// name would lex as one identifier); the loop's own body, each <c>a[i]</c> now the item, then goes back in as it was.
    /// </summary>
    private static MethodDeclarationSyntax ToForeach(MethodDeclarationSyntax method, ForStatementSyntax loop, string index, IdentifierNameSyntax array, string item)
    {
        string name = array.Identifier.Text;
        ElementAccessExpressionSyntax[] reads = [.. loop.Statement.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Where(id => string.Equals(id.Identifier.Text, index, StringComparison.Ordinal))
            .Select(id => LoopRead(id, name)!)];
        StatementSyntax body = loop.Statement.ReplaceNodes(reads, (original, _) => SyntaxFactory.IdentifierName(item).WithTriviaFrom(original));
        ForEachStatementSyntax header = SyntaxFactory.ForEachStatement(SyntaxFactory.IdentifierName("var"), SyntaxFactory.Identifier(item), array.WithoutTrivia(), SyntaxFactory.Block()).NormalizeWhitespace();
        ForEachStatementSyntax rewritten = header
            .WithCloseParenToken(header.CloseParenToken.WithTrailingTrivia(loop.CloseParenToken.TrailingTrivia))
            .WithStatement(body)
            .WithTriviaFrom(loop);
        return method.ReplaceNode(loop, rewritten);
    }

    // -- Semantic helpers for the P2-048 operators. --

    /// <summary>
    /// The method's own file bound against the BCL alone, as <see cref="CompileCheck"/> compiles it. Built only once an
    /// operator has found a syntactic site, since binding costs far more than a syntax walk.
    /// </summary>
    private static SemanticModel Model(MethodDeclarationSyntax method) =>
        CSharpCompilation.Create("EquivSeederModel", [method.SyntaxTree], CompileCheck.References.Value, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .GetSemanticModel(method.SyntaxTree);

    private static ITypeSymbol? LocalOrParameterType(SemanticModel model, ExpressionSyntax expr) => model.GetSymbolInfo(expr).Symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        _ => null,
    };

    /// <summary>Whether <paramref name="expr"/> has exactly <paramref name="type"/> and is not converted where it stands; an error type never counts.</summary>
    private static bool HasExactType(SemanticModel model, ExpressionSyntax expr, ITypeSymbol type) =>
        type.TypeKind != TypeKind.Error && model.GetTypeInfo(expr) is { Type: { } actual, ConvertedType: { } converted }
        && SymbolEqualityComparer.Default.Equals(actual, type) && SymbolEqualityComparer.Default.Equals(converted, type);

    private static bool SameName(IdentifierNameSyntax x, IdentifierNameSyntax y) => string.Equals(x.Identifier.Text, y.Identifier.Text, StringComparison.Ordinal);

    /// <summary>
    /// <paramref name="expr"/> without its outer trivia, parenthesised unless it is a primary expression, so it keeps
    /// its meaning as a <c>?:</c> or <c>??</c> operand, after <c>!</c>, or in an interpolation hole.
    /// </summary>
    private static ExpressionSyntax AsOperand(ExpressionSyntax expr)
    {
        ExpressionSyntax bare = expr.WithoutTrivia();
        return bare is IdentifierNameSyntax or LiteralExpressionSyntax or ParenthesizedExpressionSyntax or MemberAccessExpressionSyntax
            or InvocationExpressionSyntax or ElementAccessExpressionSyntax or CheckedExpressionSyntax ? bare : SyntaxFactory.ParenthesizedExpression(bare);
    }

    // -- DropNullCheck: an if/else testing a variable against null becomes the branch a non-null value takes. --

    private static IReadOnlyList<Func<MethodDeclarationSyntax>> DropNullCheckCandidates(MethodDeclarationSyntax method) =>
        [.. Nodes<IfStatementSyntax>(method, static branch => IsListElement(branch) && Unwrap(branch.Condition) is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression or (int)SyntaxKind.NotEqualsExpression } b
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
        return [.. Nodes<ExpressionStatementSyntax>(method, es => IsListElement(es) && IsFieldWrite(es, locals))
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

    /// <summary>
    /// Every expression this deep cannot itself throw: no calls, indexers, casts, awaits, checked contexts or division/modulo.
    /// An interpolated string is a call (<c>string.Concat</c>, <c>string.Format</c> or a handler's), which reads and writes
    /// the heap (ADR 0018), so an assignment of one is never independent of a field write (ticket P1-017).
    /// </summary>
    private static bool IsSimple(ExpressionSyntax expr) => !expr.DescendantNodesAndSelf().Any(static n => n switch
    {
        InvocationExpressionSyntax or InterpolatedStringExpressionSyntax or ElementAccessExpressionSyntax or ObjectCreationExpressionSyntax or CastExpressionSyntax
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

    /// <summary>
    /// Whether <paramref name="statement"/> sits in a statement list (a block or a switch section) rather than being the
    /// single embedded statement of an <c>if</c>, <c>else</c>, loop, <c>using</c> or label (ticket P2-035). Only a list
    /// element can be replaced by several statements, or by none, so an operator that does either offers no site on an
    /// embedded one: Roslyn throws "The item specified is not the element of a list" for the first and a null
    /// <c>statement</c> for the second.
    /// </summary>
    private static bool IsListElement(StatementSyntax statement) => statement.Parent is BlockSyntax or SwitchSectionSyntax;

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
        foreach (SyntaxList<StatementSyntax> statements in Nodes<BlockSyntax>(method, static _ => true).Select(static block => block.Statements))
        {
            for (int i = 0; i + 1 < statements.Count; i++)
            {
                StatementSyntax first = statements[i];
                StatementSyntax second = statements[i + 1];
                if (pairs(first, second))
                {
                    candidates.Add(() => apply(method, first, second));
                }
            }
        }

        return candidates;
    }
}
