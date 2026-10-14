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
/// the result of a valid-path source, or of a transform of valid paths; a local variable that only ever holds such
/// results; or a parameter or local variable that nothing assigns, used where a validating guard has passed on it. A
/// path from a caller or a field is not followed.
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
        CallArguments.Of(compilation.GetSemanticModel(site.Syntax.SyntaxTree).GetOperation(site.Syntax), method, static value => IsValid(value, []));

    /// <summary>Whether <paramref name="value"/> is a valid path; <paramref name="seen"/> are the locals whose assignments are being followed.</summary>
    private static bool IsValid(IOperation value, ImmutableHashSet<ISymbol> seen) => value switch
    {
        // A property that overrides only the setter has no getter of its own to be a source (ticket P2-158).
        IPropertyReferenceOperation property => property.Property.GetMethod is { } getter && RuntimeChangeTable.Load().YieldsValidPath(Identity(getter), []),
        IInvocationOperation call => RuntimeChangeTable.Load().YieldsValidPath(
            Identity(call.TargetMethod), CallArguments.Of(call, call.TargetMethod, operand => IsValid(operand, seen))),
        ILocalReferenceOperation local => IsGuarded(local.Local, value) || HoldsOnlyValidPaths(local.Local, LocalValues.Root(value), seen),
        IParameterReferenceOperation parameter => IsGuarded(parameter.Parameter, value),
        _ => false,
    };

    private static CallIdentity Identity(IMethodSymbol method) => new(CallIdentityFactory.Name(method, RenameMap.Empty));

    /// <summary>Whether <paramref name="local"/> is declared with a valid path and every later assignment gives it one.</summary>
    private static bool HoldsOnlyValidPaths(ILocalSymbol local, IOperation root, ImmutableHashSet<ISymbol> seen) =>
        !seen.Contains(local)
        && LocalValues.Initializer(local, root) is { } initial
        && LocalValues.Writes(local, root).Prepend(initial).All(written => written is not null && IsValid(written, seen.Add(local)));

    /// <summary>
    /// Whether a validating guard has returned true for <paramref name="symbol"/> wherever <paramref name="use"/> runs.
    /// Nothing may assign the symbol after its declaration, so the guard and the use read one value.
    /// </summary>
    private static bool IsGuarded(ISymbol symbol, IOperation use) =>
        LocalValues.Writes(symbol, LocalValues.Root(use)).IsEmpty && Ancestors(use).Any(step => Guards(step.Outer, step.Inner, symbol));

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
            && call.Arguments.Any(argument => SymbolEqualityComparer.Default.Equals(LocalValues.Symbol(argument.Value), symbol)),
        _ => false,
    };
}
