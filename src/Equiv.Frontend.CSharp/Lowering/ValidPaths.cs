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
/// receiver, with nothing between that runs code or writes the member, is one too (ticket P2-217). A local variable or
/// parameter read after a plain assignment of a valid path that nothing writes over is one, and so is a parameter of a
/// private method that every call in its type passes a valid path (ticket P2-207). Any other path from a caller is not
/// followed.
/// </summary>
internal static class ValidPaths
{
    /// <summary>
    /// The methods the compiler calls by a name the source does not write, so that a private one can have callers no name in
    /// its type shows (ticket P2-207): a collection initializer's <c>Add</c> and an interpolated string handler's
    /// <c>AppendLiteral</c> and <c>AppendFormatted</c>, the patterns whose methods take a string.
    /// </summary>
    private static readonly FrozenSet<string> ImplicitlyCalled = FrozenSet.ToFrozenSet(["Add", "AppendLiteral", "AppendFormatted"], StringComparer.Ordinal);

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
            IsGuarded(local.Local, value)
            || HoldsOnlyValidPaths(local.Local, LocalValues.Root(value), seen, compilation, depth)
            || (LastWrite(local.Local, local.Local.RefKind, value) is { } written && IsValid(written, seen, compilation, depth)),
        IParameterReferenceOperation parameter =>
            IsGuarded(parameter.Parameter, value)
            || (LastWrite(parameter.Parameter, parameter.Parameter.RefKind, value) is { } written && IsValid(written, seen, compilation, depth))
            || CallsPassOnlyValidPaths(parameter.Parameter, value, compilation, depth),
        _ => false,
    };

    /// <summary>
    /// The value the read <paramref name="use"/> of the local variable or parameter <paramref name="symbol"/> sees, when one
    /// statement shows it (ticket P2-207): the last statement before the read, in a block that encloses it, that writes or
    /// declares the symbol, when that statement is <c>symbol = value;</c> or a declaration that gives it a value, and nothing
    /// from that statement up to the read writes the symbol again. Only a symbol <see cref="IsWrittenInPlace"/> is read, and
    /// a read in a lambda or local function, which can run after any later write, is not. Each step reads an earlier
    /// statement, so a chain of them ends.
    /// </summary>
    private static IOperation? LastWrite(ISymbol symbol, RefKind refKind, IOperation use)
    {
        IOperation root = LocalValues.Root(use);
        if (refKind != RefKind.None || !IsWrittenInPlace(symbol, root))
        {
            return null;
        }

        foreach ((IOperation outer, IOperation inner) in Ancestors(use))
        {
            if (outer is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return null;
            }

            if (outer is IBlockOperation block
                && block.Operations.TakeWhile(statement => statement != inner)
                    .LastOrDefault(statement => !LocalValues.Writes(symbol, statement).IsEmpty || Assigned(statement, symbol) is not null) is { } writer)
            {
                return LocalValues.Writes(symbol, Assigned(inner, symbol) ?? inner).IsEmpty ? Assigned(writer, symbol) : null;
            }
        }

        return null;
    }

    /// <summary>
    /// The value <paramref name="statement"/> leaves in <paramref name="symbol"/> when it is <c>symbol = value;</c>, or a
    /// declaration that gives the symbol a value and writes it nowhere else; null for any other statement.
    /// </summary>
    private static IOperation? Assigned(IOperation statement, ISymbol symbol) => statement switch
    {
        IExpressionStatementOperation { Operation: ISimpleAssignmentOperation assignment }
            when SymbolEqualityComparer.Default.Equals(LocalValues.Symbol(assignment.Target), symbol) => assignment.Value,
        _ => LocalValues.Writes(symbol, statement).IsEmpty ? Declarator(statement, symbol)?.Initializer?.Value : null,
    };

    /// <summary>The declarator of <paramref name="symbol"/> in <paramref name="statement"/> when it is a declaration statement; null otherwise.</summary>
    private static IVariableDeclaratorOperation? Declarator(IOperation statement, ISymbol symbol) =>
        statement is IVariableDeclarationGroupOperation group
            ? group.Declarations.SelectMany(static declaration => declaration.Declarators)
                .FirstOrDefault(declarator => SymbolEqualityComparer.Default.Equals(declarator.Symbol, symbol))
            : null;

    /// <summary>
    /// Whether every write of <paramref name="symbol"/> under <paramref name="root"/> is one a statement-by-statement reading
    /// sees where it runs (ticket P2-207): a parameter belongs to the method <paramref name="root"/> is the body of (a
    /// primary constructor's parameter is written by other members too), the method has no <c>goto</c>, and each write is
    /// a plain assignment or an <c>out</c> argument whose call keeps no <c>ref</c> to it, in no lambda or local function,
    /// which could run between any two statements.
    /// </summary>
    private static bool IsWrittenInPlace(ISymbol symbol, IOperation root) =>
        (symbol is not IParameterSymbol parameter
            || SymbolEqualityComparer.Default.Equals(root.SemanticModel!.GetDeclaredSymbol(root.Syntax), parameter.ContainingSymbol))
        && !root.DescendantsAndSelf().Any(static operation => operation is IBranchOperation { BranchKind: BranchKind.GoTo })
        && LocalValues.References(symbol, root).All(static reference =>
            !LocalValues.IsWrite(reference)
            || (IsInPlace(reference) && !Ancestors(reference).Any(static step => step.Outer is IAnonymousFunctionOperation or ILocalFunctionOperation)));

    /// <summary>
    /// Whether the write <paramref name="reference"/> leaves no alias behind: the target of a plain assignment, or an
    /// <c>out</c> argument of a method that returns no <c>ref</c> and has no ref struct in its signature or as its type, the
    /// only ways a callee can keep a <c>ref</c> to the argument past the call.
    /// </summary>
    private static bool IsInPlace(IOperation reference) => reference.Parent switch
    {
        ISimpleAssignmentOperation assignment => assignment.Target == reference,
        IArgumentOperation { Parameter.RefKind: RefKind.Out } argument => KeepsNoRef((IMethodSymbol)argument.Parameter.ContainingSymbol),
        _ => false,
    };

    private static bool KeepsNoRef(IMethodSymbol method) =>
        method is { RefKind: RefKind.None, ReturnType.IsRefLikeType: false, ContainingType.IsRefLikeType: false }
        && method.Parameters.All(static parameter => !parameter.Type.IsRefLikeType);

    /// <summary>
    /// Whether the by-value parameter <paramref name="parameter"/>, read at <paramref name="use"/>, holds a valid path
    /// because its method is private and only ever called, every call in its type passing a valid path (ticket P2-207). Nothing
    /// may assign the parameter, and each call is a step toward <see cref="CalleeDepth"/>. A method the compiler can call
    /// by a name the source does not write (a collection initializer's <c>Add</c>, an interpolated string handler's
    /// <c>Append</c> methods) is not read.
    /// </summary>
    private static bool CallsPassOnlyValidPaths(IParameterSymbol parameter, IOperation use, Compilation compilation, int depth) =>
        depth < CalleeDepth
        && parameter is { RefKind: RefKind.None, ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Ordinary, DeclaredAccessibility: Accessibility.Private } method }
        && !ImplicitlyCalled.Contains(method.Name)
        && LocalValues.Writes(parameter, LocalValues.Root(use)).IsEmpty
        && CallsOf(method, compilation) is [_, ..] calls
        && calls.All(call => IsValid(
            call.Arguments.First(argument => argument.Parameter!.Ordinal == parameter.Ordinal).Value, [], call.SemanticModel!.Compilation, depth + 1));

    /// <summary>
    /// Every call of the private method <paramref name="method"/>, from the declarations of its type, which are all the
    /// source that can name it; empty when a name there refers to it other than by calling it (a method group, a
    /// <c>nameof</c>) or may (a <c>dynamic</c> member name binds to no symbol).
    /// </summary>
    private static ImmutableArray<IInvocationOperation> CallsOf(IMethodSymbol method, Compilation compilation)
    {
        ImmutableArray<IInvocationOperation?> calls =
        [
            .. method.ContainingType.DeclaringSyntaxReferences
                .SelectMany(static declaration => declaration.GetSyntax().DescendantNodes().OfType<SimpleNameSyntax>())
                .Where(name => string.Equals(name.Identifier.ValueText, method.Name, StringComparison.Ordinal))
                .Select(name => (Name: name, Model: compilation.GetSemanticModel(name.SyntaxTree)))
                .Select(static named => (named.Name, named.Model, Symbol: Unreduced(named.Model.GetSymbolInfo(named.Name).Symbol)))
                .Where(named => named.Symbol is null || SymbolEqualityComparer.Default.Equals(named.Symbol.OriginalDefinition, method.OriginalDefinition))
                .Select(static named => named.Symbol is null ? null : Invoked(named.Name, named.Model)),
        ];
        return calls.Contains(null) ? [] : [.. calls.OfType<IInvocationOperation>()];
    }

    /// <summary>The method an extension method called as an instance method is reduced from, or <paramref name="symbol"/> itself.</summary>
    private static ISymbol? Unreduced(ISymbol? symbol) => symbol is IMethodSymbol { ReducedFrom: { } unreduced } ? unreduced : symbol;

    /// <summary>The call <paramref name="name"/> names the method of, alone or after <c>.</c> or <c>?.</c>; null when it is not called.</summary>
    private static IInvocationOperation? Invoked(SimpleNameSyntax name, SemanticModel model) =>
        Callee(name).Parent is InvocationExpressionSyntax invocation ? model.GetOperation(invocation) as IInvocationOperation : null;

    /// <summary>The expression <paramref name="name"/> ends: the member access or member binding it is the name of, or itself.</summary>
    private static SyntaxNode Callee(SimpleNameSyntax name) =>
        name.Parent is MemberAccessExpressionSyntax or MemberBindingExpressionSyntax ? name.Parent : name;

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
    /// Nothing may assign the symbol after its declaration, so the guard and the use read one value; the symbol is declared
    /// in the member that reads it, so every write of it is there to see. A primary constructor's parameter is not: any
    /// member of its type can write it, and a call between the guard and the use can run that member (ticket P2-230).
    /// </summary>
    private static bool IsGuarded(ISymbol symbol, IOperation use)
    {
        IOperation root = LocalValues.Root(use);
        return symbol.DeclaringSyntaxReferences.All(declaration => declaration.GetSyntax().Ancestors().Contains(root.Syntax))
            && LocalValues.Writes(symbol, root).IsEmpty
            && Ancestors(use).Any(step => Guards(step.Outer, step.Inner, argument => SymbolEqualityComparer.Default.Equals(LocalValues.Symbol(argument), symbol)) is not null);
    }

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

    /// <summary>
    /// Whether <paramref name="receiver"/> is one object wherever the method reads it: a class's <c>this</c>, a parameter
    /// passed by value that nothing assigns under <paramref name="root"/>, or none. A struct's <c>this</c> and a <c>ref</c>
    /// or <c>in</c> parameter are not: each can alias a field or array element whose plain assignment replaces it.
    /// </summary>
    private static bool IsStable(IOperation? receiver, IOperation root) => receiver switch
    {
        null or IInstanceReferenceOperation { Type.IsReferenceType: true } => true,
        IParameterReferenceOperation { Parameter.RefKind: RefKind.None } parameter => LocalValues.Writes(parameter.Parameter, root).IsEmpty,
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
