using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.RuntimeChanges;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Which operands of a call are paths that .NET Framework's validation accepts, by what the method that makes the call
/// does with them (ticket P2-074; the header of <c>runtime-changes.json</c> has the rules). An operand is one when it is
/// the result of a valid-path source or validator, of a transform of valid paths or of a valid path's start; a
/// conditional expression both of whose branches are one; a local variable that only ever holds such results; or a
/// parameter or local variable that nothing assigns, used where a validating guard has passed on it. A call to one of
/// the solution's own methods or properties that no override can replace yields one when every value its body returns is
/// one by these rules, read in that body without following its own calls further (ticket P2-188). A path from a caller
/// or a field is not followed.
/// </summary>
internal static class ValidPaths
{
    /// <summary>
    /// The operands of the call <paramref name="site"/> makes to <paramref name="method"/>, as
    /// <see cref="CallArguments.Of(IOperation?, IMethodSymbol)"/> gives them, each marked when it is a valid path. The
    /// site is read where <paramref name="compilation"/> binds its syntax, which has the whole method around it; the
    /// operation of a control-flow graph does not.
    /// </summary>
    public static ImmutableArray<CallArgument> Of(IOperation site, IMethodSymbol method, Compilation compilation) =>
        CallArguments.Of(compilation.GetSemanticModel(site.Syntax.SyntaxTree).GetOperation(site.Syntax), method, value => IsValid(value, [], compilation));

    /// <summary>
    /// Whether <paramref name="value"/> is a valid path; <paramref name="seen"/> are the locals whose assignments are being
    /// followed, and <paramref name="compilation"/>, when it is not null, is the one a solution callee's body is read from.
    /// </summary>
    private static bool IsValid(IOperation value, ImmutableHashSet<ISymbol> seen, Compilation? compilation) => value switch
    {
        // A property that overrides only the setter has no getter of its own to be a source (ticket P2-158).
        IPropertyReferenceOperation property => property.Property.GetMethod is { } getter
            && (RuntimeChangeTable.Load().YieldsValidPath(Identity(getter), []) || ReturnsOnlyValidPaths(getter, compilation)),
        IInvocationOperation call => RuntimeChangeTable.Load().YieldsValidPath(
                Identity(call.TargetMethod), CallArguments.Of(call, call.TargetMethod, operand => IsValid(operand, seen, compilation)))
            || ReturnsOnlyValidPaths(call.TargetMethod, compilation),

        // Only an expression is a value, and a conditional expression has both branches.
        IConditionalOperation conditional => IsValid(conditional.WhenTrue, seen, compilation) && IsValid(conditional.WhenFalse!, seen, compilation),
        ILocalReferenceOperation local => IsGuarded(local.Local, value) || HoldsOnlyValidPaths(local.Local, Root(value), seen, compilation),
        IParameterReferenceOperation parameter => IsGuarded(parameter.Parameter, value),
        _ => false,
    };

    private static CallIdentity Identity(IMethodSymbol method) => new(CallIdentityFactory.Name(method, RenameMap.Empty));

    /// <summary>
    /// Whether every value that the body of <paramref name="method"/> returns is a valid path, when the method is one of the
    /// solution's own methods or property getters and no override can replace it (ticket P2-188). The values are read by
    /// the rules in that body alone, so its own calls of the solution's methods are not followed: <paramref name="compilation"/>
    /// is null there. False for a body that returns nothing, an auto-property's getter among them.
    /// </summary>
    private static bool ReturnsOnlyValidPaths(IMethodSymbol method, Compilation? compilation) =>
        compilation is not null
        && method is
        {
            MethodKind: MethodKind.Ordinary or MethodKind.PropertyGet,
            IsVirtual: false,
            IsAbstract: false,
            IsOverride: false,
            DeclaringSyntaxReferences: [SyntaxReference declaration],
        }
        && Body(declaration, compilation) is { } body
        && Returned(body).ToImmutableArray() is [_, ..] returned
        && returned.All(static value => IsValid(value, [], compilation: null));

    /// <summary>
    /// The operation of the member <paramref name="declaration"/> declares, read with the compilation whose source holds it:
    /// <paramref name="compilation"/> itself or a project it references as source. Null for a member with no body.
    /// </summary>
    private static IOperation? Body(SyntaxReference declaration, Compilation compilation) =>
        compilation.References.OfType<CompilationReference>().Select(static reference => reference.Compilation).Prepend(compilation)
            .Where(owner => owner.ContainsSyntaxTree(declaration.SyntaxTree))
            .Select(owner => owner.GetSemanticModel(declaration.SyntaxTree).GetOperation(declaration.GetSyntax()))
            .FirstOrDefault();

    /// <summary>
    /// The values the <c>return</c> statements of <paramref name="operation"/> return, leaving out those of the lambdas and
    /// local functions in it. A string-valued body, the only one a path is read from, has a value at each.
    /// </summary>
    private static IEnumerable<IOperation> Returned(IOperation operation) => operation switch
    {
        IReturnOperation returned => [returned.ReturnedValue!],
        IAnonymousFunctionOperation or ILocalFunctionOperation => [],
        _ => operation.ChildOperations.SelectMany(Returned),
    };

    /// <summary>Whether <paramref name="local"/> is declared with a valid path and every later assignment gives it one.</summary>
    private static bool HoldsOnlyValidPaths(ILocalSymbol local, IOperation root, ImmutableHashSet<ISymbol> seen, Compilation? compilation) =>
        !seen.Contains(local)
        && Initializer(local, root) is { } initial
        && Writes(local, root).Prepend(initial).All(written => written is not null && IsValid(written, seen.Add(local), compilation));

    private static IOperation? Initializer(ILocalSymbol local, IOperation root) =>
        root.DescendantsAndSelf().OfType<IVariableDeclaratorOperation>().FirstOrDefault(d => SymbolEqualityComparer.Default.Equals(d.Symbol, local))?.Initializer?.Value;

    /// <summary>
    /// What is written to <paramref name="symbol"/> under <paramref name="root"/> after its declaration: the value of each
    /// assignment, or null for a write whose value is not known (a compound assignment, a <c>ref</c> or <c>out</c>
    /// argument, a tuple it is part of).
    /// </summary>
    private static ImmutableArray<IOperation?> Writes(ISymbol symbol, IOperation root) =>
        [.. root.DescendantsAndSelf().Where(o => SymbolEqualityComparer.Default.Equals(Symbol(o), symbol)).Select(Write).Where(static w => w.Written).Select(static w => w.Value)];

    private static (bool Written, IOperation? Value) Write(IOperation reference) => reference.Parent switch
    {
        ISimpleAssignmentOperation assignment when assignment.Target == reference => (true, assignment.Value),
        IAssignmentOperation assignment when assignment.Target == reference => (true, null),
        IArgumentOperation { Parameter.RefKind: not RefKind.None } or ITupleOperation => (true, null),
        _ => (false, null),
    };

    private static ISymbol? Symbol(IOperation operation) => operation switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        _ => null,
    };

    private static IOperation Root(IOperation operation) => operation.Parent is { } parent ? Root(parent) : operation;

    /// <summary>
    /// Whether a validating guard has returned true for <paramref name="symbol"/> wherever <paramref name="use"/> runs.
    /// Nothing may assign the symbol after its declaration, so the guard and the use read one value.
    /// </summary>
    private static bool IsGuarded(ISymbol symbol, IOperation use) =>
        Writes(symbol, Root(use)).IsEmpty && Ancestors(use).Any(step => Guards(step.Outer, step.Inner, symbol));

    /// <summary>Each operation from <paramref name="operation"/> up, with its parent.</summary>
    private static IEnumerable<(IOperation Outer, IOperation Inner)> Ancestors(IOperation operation)
    {
        for (IOperation inner = operation; inner.Parent is { } outer; inner = outer)
        {
            yield return (outer, inner);
        }
    }

    /// <summary>Whether <paramref name="inner"/>, a child of <paramref name="outer"/>, runs only once a guard has passed on <paramref name="symbol"/>.</summary>
    private static bool Guards(IOperation outer, IOperation inner, ISymbol symbol) => outer switch
    {
        IConditionalOperation conditional =>
            (inner == conditional.WhenTrue && Holds(conditional.Condition, whenTrue: true, symbol))
            || (inner == conditional.WhenFalse && Holds(conditional.Condition, whenTrue: false, symbol)),
        IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } and => inner == and.RightOperand && Holds(and.LeftOperand, whenTrue: true, symbol),
        IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } or => inner == or.RightOperand && Holds(or.LeftOperand, whenTrue: false, symbol),
        IBlockOperation block => block.Operations.TakeWhile(statement => statement != inner).Any(statement => LeavesUnlessGuarded(statement, symbol)),
        _ => false,
    };

    /// <summary>An <c>if</c> without an <c>else</c> that returns or throws when no guard has passed on <paramref name="symbol"/>.</summary>
    private static bool LeavesUnlessGuarded(IOperation statement, ISymbol symbol) =>
        statement is IConditionalOperation { WhenFalse: null } conditional && Leaves(conditional.WhenTrue) && Holds(conditional.Condition, whenTrue: false, symbol);

    private static bool Leaves(IOperation statement) =>
        statement.Kind is OperationKind.Return or OperationKind.Throw || (statement is IBlockOperation { Operations: [.., IOperation last] } && Leaves(last));

    /// <summary>Whether <paramref name="condition"/> being <paramref name="whenTrue"/> means a guard returned true for <paramref name="symbol"/>.</summary>
    private static bool Holds(IOperation condition, bool whenTrue, ISymbol symbol) => condition switch
    {
        IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } not => Holds(not.Operand, !whenTrue, symbol),
        IBinaryOperation binary when binary.OperatorKind == (whenTrue ? BinaryOperatorKind.ConditionalAnd : BinaryOperatorKind.ConditionalOr) =>
            Holds(binary.LeftOperand, whenTrue, symbol) || Holds(binary.RightOperand, whenTrue, symbol),
        IInvocationOperation call => whenTrue
            && RuntimeChangeTable.Load().ValidatesPath(Identity(call.TargetMethod))
            && call.Arguments.Any(argument => SymbolEqualityComparer.Default.Equals(Symbol(argument.Value), symbol)),
        _ => false,
    };
}
