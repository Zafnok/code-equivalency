using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.RuntimeChanges;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Which operands of a call are paths that .NET Framework's validation accepts, by what the method that makes the call
/// does with them (ticket P2-074; the header of <c>runtime-changes.json</c> has the rules). An operand is one when it is
/// the result of a valid-path source or validator, of a transform of valid paths or of a valid path's start; a
/// conditional expression both of whose branches are one; a local variable that only ever holds such results; or a
/// parameter or local variable that nothing assigns, used where a validating guard has passed on it. A call to one of
/// the solution's own methods or properties yields one when the body that runs is known, because no override can replace
/// it or the receiver's type is sealed (ticket P2-200), and every value that body returns is one by these rules, read in
/// that body and in the bodies its own returned calls run, at most <see cref="CalleeDepth"/> calls deep (tickets P2-188
/// and P2-200). A read of a <c>readonly</c> field or a get-only auto-property of a class yields one when its initializer and
/// every write in a constructor or <c>init</c> accessor of its type is one by these rules (ticket P2-201), each a step of
/// that depth. A read of a field or auto-property where a validating guard has passed on the same member of the same
/// receiver, with nothing between that runs code or writes the member, is one too (ticket P2-217). A path from a caller is
/// not followed.
/// </summary>
internal static class ValidPaths
{
    /// <summary>
    /// How many calls deep a returned value is followed into the solution's own bodies (ticket P2-200), a field's stored
    /// values counting as one call (ticket P2-201). The deepest chain
    /// in the corpus is three, from a sealed snapshot class's override through a nested type's method to a helper ending
    /// in <c>Path.GetFullPath</c>; the bound keeps the work per argument small, recursion included.
    /// </summary>
    private const int CalleeDepth = 4;

    /// <summary>
    /// The operations that run no code and write nothing (ticket P2-217); <see cref="Quiet"/> reads operators, field reads
    /// and assignments itself.
    /// </summary>
    private static readonly FrozenSet<OperationKind> QuietKinds = FrozenSet.ToFrozenSet(
        [
            OperationKind.Literal,
            OperationKind.DefaultValue,
            OperationKind.TypeOf,
            OperationKind.SizeOf,
            OperationKind.NameOf,
            OperationKind.InstanceReference,
            OperationKind.LocalReference,
            OperationKind.ParameterReference,
            OperationKind.ArrayElementReference,
            OperationKind.ArrayCreation,
            OperationKind.ArrayInitializer,
            OperationKind.Discard,
            OperationKind.Conditional,
            OperationKind.Coalesce,
            OperationKind.ConditionalAccess,
            OperationKind.ConditionalAccessInstance,
            OperationKind.IsNull,
            OperationKind.IsType,
            OperationKind.Block,
            OperationKind.ExpressionStatement,
            OperationKind.Empty,
            OperationKind.VariableDeclarationGroup,
            OperationKind.VariableDeclaration,
            OperationKind.VariableDeclarator,
            OperationKind.VariableInitializer,
            OperationKind.Return,
            OperationKind.Throw,
            OperationKind.Branch,
            OperationKind.Try,
            OperationKind.CatchClause,
        ]);

    /// <summary>
    /// The operands of the call <paramref name="site"/> makes to <paramref name="method"/>, as
    /// <see cref="CallArguments.Of(IOperation?, IMethodSymbol)"/> gives them, each marked when it is a valid path. The
    /// site is read where <paramref name="compilation"/> binds its syntax, which has the whole method around it; the
    /// operation of a control-flow graph does not.
    /// </summary>
    public static ImmutableArray<CallArgument> Of(IOperation site, IMethodSymbol method, Compilation compilation) =>
        CallArguments.Of(compilation.GetSemanticModel(site.Syntax.SyntaxTree).GetOperation(site.Syntax), method, value => IsValid(value, [], compilation, depth: 0));

    /// <summary>
    /// Whether <paramref name="value"/> is a valid path; <paramref name="seen"/> are the locals whose assignments are being
    /// followed, <paramref name="compilation"/> is the one that holds the value's syntax, and <paramref name="depth"/> is
    /// how many solution callees deep the value is.
    /// </summary>
    private static bool IsValid(IOperation value, ImmutableHashSet<ISymbol> seen, Compilation compilation, int depth) => value switch
    {
        // A property that overrides only the setter has no getter of its own to be a source (ticket P2-158).
        IPropertyReferenceOperation property => (property.Property.GetMethod is { } getter
                && (RuntimeChangeTable.Load().YieldsValidPath(Identity(getter), [])
                    || ReturnsOnlyValidPaths(getter, property.Instance, compilation, depth)
                    || (AutoProperty(Dispatched(getter, property.Instance)) is not null && IsGuardedRead(property))))
            || StoresOnlyValidPaths(property, compilation, depth),
        IFieldReferenceOperation field => StoresOnlyValidPaths(field, compilation, depth) || IsGuardedRead(field),
        IInvocationOperation call => RuntimeChangeTable.Load().YieldsValidPath(
                Identity(call.TargetMethod), CallArguments.Of(call, call.TargetMethod, operand => IsValid(operand, seen, compilation, depth)))
            || ReturnsOnlyValidPaths(call.TargetMethod, call.Instance, compilation, depth),

        // Only an expression is a value, and a conditional expression has both branches.
        IConditionalOperation conditional =>
            IsValid(conditional.WhenTrue, seen, compilation, depth) && IsValid(conditional.WhenFalse!, seen, compilation, depth),
        ILocalReferenceOperation local =>
            IsGuarded(local.Local, value) || HoldsOnlyValidPaths(local.Local, LocalValues.Root(value), seen, compilation, depth),
        IParameterReferenceOperation parameter => IsGuarded(parameter.Parameter, value),
        _ => false,
    };

    private static CallIdentity Identity(IMethodSymbol method) => new(CallIdentityFactory.Name(method, RenameMap.Empty));

    /// <summary>
    /// Whether every value returned by the body that a call to <paramref name="method"/> on <paramref name="receiver"/> runs
    /// is a valid path, when that body is one of the solution's own methods or property getters (ticket P2-188). The values
    /// are read by these rules, and a call among them to another of the solution's members is followed the same way while
    /// <paramref name="depth"/> is under <see cref="CalleeDepth"/> (ticket P2-200). False for a body that returns nothing,
    /// an auto-property's getter among them.
    /// </summary>
    private static bool ReturnsOnlyValidPaths(IMethodSymbol method, IOperation? receiver, Compilation compilation, int depth) =>
        depth < CalleeDepth
        && Dispatched(method, receiver) is
        {
            MethodKind: MethodKind.Ordinary or MethodKind.PropertyGet,
            DeclaringSyntaxReferences: [SyntaxReference declaration],
        }
        && Body(declaration, compilation) is { } body
        && Returned(body).ToImmutableArray() is [_, ..] returned
        && returned.All(value => IsValid(value, [], body.SemanticModel!.Compilation, depth + 1));

    /// <summary>
    /// The method whose body a call to <paramref name="method"/> on <paramref name="receiver"/> runs: the method itself when
    /// no override can replace it (not virtual, abstract or an override), and otherwise, when the receiver's type is sealed,
    /// the override that type itself declares (ticket P2-200). Null otherwise: an override the call cannot see may run, or
    /// the sealed type inherits the one that runs, which is not looked for.
    /// </summary>
    private static IMethodSymbol? Dispatched(IMethodSymbol method, IOperation? receiver) =>
        method is { IsVirtual: false, IsAbstract: false, IsOverride: false } ? method : SealedOverride(method, receiver);

    /// <summary>
    /// The override of <paramref name="method"/> that <paramref name="receiver"/>'s type declares, when that type is sealed
    /// (ticket P2-200). Null when the type is not sealed or inherits the override that runs.
    /// </summary>
    private static IMethodSymbol? SealedOverride(IMethodSymbol method, IOperation? receiver) =>
        receiver?.Type is { IsSealed: true } sealedType
            ? sealedType.GetMembers(method.Name).OfType<IMethodSymbol>().FirstOrDefault(candidate => Overrides(candidate, method))
            : null;

    /// <summary>Whether <paramref name="candidate"/> is <paramref name="method"/> or overrides it, directly or through others.</summary>
    private static bool Overrides(IMethodSymbol candidate, IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, method.OriginalDefinition)
        || (candidate.OverriddenMethod is { } overridden && Overrides(overridden, method));

    /// <summary>
    /// The operation of the member <paramref name="declaration"/> declares, read with the compilation whose source holds it:
    /// <paramref name="compilation"/> itself or a project it references as source. Null for a member with no body.
    /// </summary>
    private static IOperation? Body(SyntaxReference declaration, Compilation compilation) => Operation(declaration.GetSyntax(), compilation);

    /// <summary>
    /// The operation of <paramref name="syntax"/>, read with the compilation whose source holds it: <paramref name="compilation"/>
    /// itself or a project it references as source. Null when neither holds it or the syntax is no operation.
    /// </summary>
    private static IOperation? Operation(SyntaxNode syntax, Compilation compilation) =>
        compilation.References.OfType<CompilationReference>().Select(static reference => reference.Compilation).Prepend(compilation)
            .Where(owner => owner.ContainsSyntaxTree(syntax.SyntaxTree))
            .Select(owner => owner.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax))
            .FirstOrDefault();

    /// <summary>
    /// Whether the field or property <paramref name="reference"/> reads only ever holds valid paths (ticket P2-201): it is
    /// <see cref="Stored"/> of a class, it is given a value at least once, and every value it is given, read by these rules
    /// in the body that gives it, is a valid path while <paramref name="depth"/> is under <see cref="CalleeDepth"/>. A
    /// struct's is not read: <c>this = ...</c> in any member of a struct writes every field.
    /// </summary>
    private static bool StoresOnlyValidPaths(IOperation reference, Compilation compilation, int depth) =>
        depth < CalleeDepth
        && Stored(reference) is { ContainingType.TypeKind: TypeKind.Class } member
        && Writes(member, compilation).ToImmutableArray() is [_, ..] written
        && written.All(value => value is not null && IsValid(value, [], value.SemanticModel!.Compilation, depth + 1));

    /// <summary>
    /// The definition of the member whose stored value <paramref name="reference"/> reads, when only its own type's
    /// construction can write it: a <c>readonly</c> field, or a get-only auto-property (no setter, a getter with no body,
    /// not a partial definition whose implementation has one) whose getter is the one that runs, as for a solution callee.
    /// Null for any other read.
    /// </summary>
    private static ISymbol? Stored(IOperation reference) => reference switch
    {
        IFieldReferenceOperation { Field: { IsReadOnly: true } field } => field.OriginalDefinition,
        IPropertyReferenceOperation { Property.GetMethod: { } getter } property =>
            AutoProperty(Dispatched(getter, property.Instance)) is { SetMethod: null } auto ? auto.OriginalDefinition : null,
        _ => null,
    };

    /// <summary>
    /// The auto-property whose getter is <paramref name="getter"/>: a getter with no body, of a property that is not a
    /// partial definition whose implementation has one. Null for any other getter, and for none.
    /// </summary>
    private static IPropertySymbol? AutoProperty(IMethodSymbol? getter) =>
        getter?.AssociatedSymbol is IPropertySymbol { IsPartialDefinition: false, GetMethod.DeclaringSyntaxReferences: [SyntaxReference accessor] } auto
        && accessor.GetSyntax() is AccessorDeclarationSyntax { Body: null, ExpressionBody: null }
            ? auto
            : null;

    /// <summary>
    /// Every value given to <paramref name="member"/>: its initializer's, then each write in a constructor or <c>init</c>
    /// accessor of its type, the only places the language lets a <c>readonly</c> field or a get-only auto-property be
    /// written or passed by writable <c>ref</c>. Null for a value that is not known (<see cref="LocalValues.Writes"/>).
    /// </summary>
    private static IEnumerable<IOperation?> Writes(ISymbol member, Compilation compilation) =>
        member.DeclaringSyntaxReferences
            .SelectMany(static declaration => declaration.GetSyntax().ChildNodes().OfType<EqualsValueClauseSyntax>())
            .Select(initializer => Operation(initializer.Value, compilation))
            .Concat(member.ContainingType.GetMembers().OfType<IMethodSymbol>()
                .Where(static method => method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor || method.IsInitOnly)
                .SelectMany(static method => method.DeclaringSyntaxReferences)
                .Select(declaration => Body(declaration, compilation))
                .OfType<IOperation>()
                .SelectMany(body => LocalValues.Writes(member, body)));

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
    private static bool HoldsOnlyValidPaths(ILocalSymbol local, IOperation root, ImmutableHashSet<ISymbol> seen, Compilation compilation, int depth) =>
        !seen.Contains(local)
        && LocalValues.Initializer(local, root) is { } initial
        && LocalValues.Writes(local, root).Prepend(initial).All(written => written is not null && IsValid(written, seen.Add(local), compilation, depth));

    /// <summary>
    /// Whether a validating guard has returned true for <paramref name="symbol"/> wherever <paramref name="use"/> runs.
    /// Nothing may assign the symbol after its declaration, so the guard and the use read one value.
    /// </summary>
    private static bool IsGuarded(ISymbol symbol, IOperation use) =>
        LocalValues.Writes(symbol, LocalValues.Root(use)).IsEmpty
        && Ancestors(use).Any(step => Guards(step.Outer, step.Inner, argument => SymbolEqualityComparer.Default.Equals(LocalValues.Symbol(argument), symbol)) is not null);

    /// <summary>
    /// Whether a validating guard has returned true for a read of the member <paramref name="use"/> reads, of the same
    /// receiver, with nothing <see cref="Quiet"/> between the guard and the use (ticket P2-217): the two reads then return
    /// one value. The receiver is <c>this</c>, a parameter nothing assigns, or none. A method with a <c>goto</c> is not
    /// read, nor a use in a lambda or local function, which can run after anything; a use in a loop under the guard has the
    /// whole loop between, as the loop's earlier turns run there.
    /// </summary>
    private static bool IsGuardedRead(IMemberReferenceOperation use)
    {
        IOperation root = LocalValues.Root(use);
        if (!IsStable(use.Instance, root) || root.DescendantsAndSelf().Any(static operation => operation is IBranchOperation { BranchKind: BranchKind.GoTo }))
        {
            return false;
        }

        ISymbol member = use.Member.OriginalDefinition;
        List<IOperation> between = [];
        foreach ((IOperation outer, IOperation inner) in Ancestors(use))
        {
            if (outer is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return false;
            }

            if (Guards(outer, inner, read => Rereads(read, use)) is { } after && after.Concat(between).All(operation => Quiet(operation, member)))
            {
                return true;
            }

            between.AddRange(outer is ILoopOperation ? [outer] : outer.ChildOperations.TakeWhile(child => child != inner));
        }

        return false;
    }

    /// <summary>Whether <paramref name="receiver"/> is one object wherever the method reads it: <c>this</c>, a parameter nothing assigns under <paramref name="root"/>, or none.</summary>
    private static bool IsStable(IOperation? receiver, IOperation root) => receiver switch
    {
        null or IInstanceReferenceOperation => true,
        IParameterReferenceOperation parameter => LocalValues.Writes(parameter.Parameter, root).IsEmpty,
        _ => false,
    };

    /// <summary>Whether <paramref name="read"/> reads the member <paramref name="use"/> reads, of the same receiver.</summary>
    private static bool Rereads(IOperation read, IMemberReferenceOperation use) =>
        read is IMemberReferenceOperation other
        && SymbolEqualityComparer.Default.Equals(other.Member.OriginalDefinition, use.Member.OriginalDefinition)
        && (other.Instance, use.Instance) switch
        {
            (null, null) or (IInstanceReferenceOperation, IInstanceReferenceOperation) => true,
            (IParameterReferenceOperation first, IParameterReferenceOperation second) => SymbolEqualityComparer.Default.Equals(first.Parameter, second.Parameter),
            _ => false,
        };

    /// <summary>
    /// Whether <paramref name="operation"/> and everything in it runs no code and writes <paramref name="member"/> by no
    /// name or alias (ticket P2-217): each part is one of <see cref="QuietKinds"/>, an operator or conversion with no
    /// operator method, an instance field's read, or an assignment <see cref="IsQuietTarget"/> allows. A static field's read
    /// can run its type's static constructor.
    /// </summary>
    private static bool Quiet(IOperation operation, ISymbol member) => operation.DescendantsAndSelf().All(step => step switch
    {
        IFieldReferenceOperation field => !field.Field.IsStatic,
        IConversionOperation conversion => conversion.OperatorMethod is null,
        IUnaryOperation unary => unary.OperatorMethod is null,
        IBinaryOperation binary => binary.OperatorMethod is null,
        ISimpleAssignmentOperation assignment => IsQuietTarget(assignment.Target, member),
        _ => QuietKinds.Contains(step.Kind),
    });

    /// <summary>
    /// Whether an assignment to <paramref name="target"/> writes neither <paramref name="member"/> nor through a <c>ref</c>
    /// that can alias it, and calls no setter: a local variable or parameter that is not a <c>ref</c>, another field, an
    /// array element or a discard.
    /// </summary>
    private static bool IsQuietTarget(IOperation target, ISymbol member) => target switch
    {
        ILocalReferenceOperation local => local.Local.RefKind == RefKind.None,
        IParameterReferenceOperation parameter => parameter.Parameter.RefKind == RefKind.None,
        IFieldReferenceOperation field => !SymbolEqualityComparer.Default.Equals(field.Field.OriginalDefinition, member),
        _ => target.Kind is OperationKind.ArrayElementReference or OperationKind.Discard,
    };

    /// <summary>Each operation from <paramref name="operation"/> up, with its parent.</summary>
    private static IEnumerable<(IOperation Outer, IOperation Inner)> Ancestors(IOperation operation)
    {
        for (IOperation inner = operation; inner.Parent is { } outer; inner = outer)
        {
            yield return (outer, inner);
        }
    }

    /// <summary>
    /// When <paramref name="inner"/>, a child of <paramref name="outer"/>, runs only once a guard has passed on an argument
    /// <paramref name="guarded"/> accepts, the operations that run after that guard and before <paramref name="inner"/>;
    /// null otherwise.
    /// </summary>
    private static IEnumerable<IOperation>? Guards(IOperation outer, IOperation inner, Func<IOperation, bool> guarded) => outer switch
    {
        IConditionalOperation conditional when inner == conditional.WhenTrue => Holds(conditional.Condition, whenTrue: true, guarded),
        IConditionalOperation conditional when inner == conditional.WhenFalse => Holds(conditional.Condition, whenTrue: false, guarded),
        IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } both when inner == both.RightOperand => Holds(both.LeftOperand, whenTrue: true, guarded),
        IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } either when inner == either.RightOperand => Holds(either.LeftOperand, whenTrue: false, guarded),
        IBlockOperation block => Exits(block, inner, guarded),
        _ => null,
    };

    /// <summary>
    /// For the last statement of <paramref name="block"/> before <paramref name="inner"/> that leaves unless a guard has
    /// passed, the rest of its condition and the statements after it; null when there is none.
    /// </summary>
    private static IEnumerable<IOperation>? Exits(IBlockOperation block, IOperation inner, Func<IOperation, bool> guarded)
    {
        ImmutableArray<IOperation> before = [.. block.Operations.TakeWhile(statement => statement != inner)];
        for (int index = before.Length - 1; index >= 0; index--)
        {
            if (LeavesUnlessGuarded(before[index], guarded) is { } after)
            {
                return after.Concat(before.Skip(index + 1));
            }
        }

        return null;
    }

    /// <summary>
    /// For an <c>if</c> without an <c>else</c> that returns or throws when no guard has passed, what its condition runs
    /// after the guard; null for any other statement.
    /// </summary>
    private static IEnumerable<IOperation>? LeavesUnlessGuarded(IOperation statement, Func<IOperation, bool> guarded) =>
        statement is IConditionalOperation { WhenFalse: null } conditional && Leaves(conditional.WhenTrue) ? Holds(conditional.Condition, whenTrue: false, guarded) : null;

    private static bool Leaves(IOperation statement) =>
        statement.Kind is OperationKind.Return or OperationKind.Throw || (statement is IBlockOperation { Operations: [.., IOperation last] } && Leaves(last));

    /// <summary>
    /// When <paramref name="condition"/> being <paramref name="whenTrue"/> means a guard returned true for an argument
    /// <paramref name="guarded"/> accepts, the operands of the condition that run after that guard; null otherwise.
    /// </summary>
    private static IEnumerable<IOperation>? Holds(IOperation condition, bool whenTrue, Func<IOperation, bool> guarded) => condition switch
    {
        IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } not => Holds(not.Operand, !whenTrue, guarded),
        IBinaryOperation binary when binary.OperatorKind == (whenTrue ? BinaryOperatorKind.ConditionalAnd : BinaryOperatorKind.ConditionalOr) =>
            Holds(binary.RightOperand, whenTrue, guarded) ?? Holds(binary.LeftOperand, whenTrue, guarded)?.Append(binary.RightOperand),
        IInvocationOperation call when whenTrue
            && RuntimeChangeTable.Load().ValidatesPath(Identity(call.TargetMethod))
            && call.Arguments.Any(argument => guarded(argument.Value)) => [],
        _ => null,
    };
}
