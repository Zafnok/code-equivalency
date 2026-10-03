using System.Linq;

using Equiv.Core.Configuration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Which methods are forwarders, and what each forwards to (ADR 0043; ticket P2-068). A forwarder is an ordinary static
/// method declared in source whose body is one call of a static method with its own parameters, each at its own position
/// and unchanged, whose result it returns. A call to one is the same call to its target, so both lowerings and the bound
/// fingerprint ask this one rule from the callee's symbol. The other conditions each rule out a call that would do more,
/// or less, than its target: a static constructor that runs first, an <c>async</c> method that puts the exception into a
/// task, a <c>[Conditional]</c> call that may not be compiled, and a security attribute that may throw first. A forwarder
/// both sides of a run have is resolved only where the sides agree on its target (<see cref="Agree"/>); a call to any
/// other one stays a call to it, which the caller's verdict assumes equivalent (ADR 0019).
/// </summary>
internal static class Forwarders
{
    /// <summary>
    /// Whether a call to a method both sides have is the same call on both: neither side's is a forwarder, or both are and
    /// their targets have the same identity under <paramref name="renames"/>.
    /// </summary>
    public static bool Agree((IMethodSymbol Method, Compilation Compilation) legacy, (IMethodSymbol Method, Compilation Compilation) modern, RenameMap renames) =>
        string.Equals(TargetName(legacy, renames), TargetName(modern, renames), StringComparison.Ordinal);

    private static string? TargetName((IMethodSymbol Method, Compilation Compilation) side, RenameMap renames) =>
        Resolve(side.Method, side.Compilation) is { } resolved ? CallIdentityFactory.Name(resolved.Target, renames) : null;

    /// <summary>
    /// The method a call to <paramref name="method"/> calls, when <paramref name="method"/> is a forwarder: the end of the
    /// chain of forwarders that starts at it, with the compilation that symbol belongs to. Null when
    /// <paramref name="method"/> is not a forwarder, and when the chain comes back to a method already on it.
    /// <paramref name="compilation"/> is the one <paramref name="method"/> was bound in.
    /// </summary>
    public static Resolved? Resolve(IMethodSymbol method, Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(compilation);
        HashSet<IMethodSymbol> followed = new(SymbolEqualityComparer.Default);
        Resolved? last = null;
        for (Resolved? next = TargetOf(method, compilation); next is not null; next = TargetOf(next.Target, next.Compilation))
        {
            if (!followed.Add(next.Target))
            {
                return null;
            }

            last = next;
        }

        return last;
    }

    /// <summary>What <paramref name="method"/> forwards to, read from its source in the compilation that holds it; null when it is not a forwarder.</summary>
    private static Resolved? TargetOf(IMethodSymbol method, Compilation compilation) =>
        method is
        {
            MethodKind: MethodKind.Ordinary,
            IsStatic: true,
            IsVirtual: false,
            IsAsync: false,
            IsGenericMethod: false,
            RefKind: RefKind.None,
            ContainingType: { IsGenericType: false, StaticConstructors.IsEmpty: true },
            DeclaringSyntaxReferences: [SyntaxReference declaration],
        }
        && !ChangesTheCall(method)
        && Forwarded(declaration.GetSyntax()) is { } forwarded
        && Owner(method, declaration.SyntaxTree, compilation) is { } owner
        && owner.GetSemanticModel(declaration.SyntaxTree).GetOperation(forwarded) is IInvocationOperation { Instance: null } call
        && call.Arguments.Length == method.Parameters.Length
        && call.Arguments.All(PassesItsOwnParameter)
        && SymbolEqualityComparer.Default.Equals(call.TargetMethod.ReturnType, method.ReturnType)
            ? new Resolved(call.TargetMethod, owner)
            : null;

    /// <summary>The one expression a method's body evaluates and returns, as its syntax spells it; null for any other body.</summary>
    private static ExpressionSyntax? Forwarded(SyntaxNode declaration) => declaration switch
    {
        MethodDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
        MethodDeclarationSyntax { Body.Statements: [ReturnStatementSyntax { Expression: { } expression }] } => expression,
        MethodDeclarationSyntax { Body.Statements: [ExpressionStatementSyntax statement] } => statement.Expression,
        _ => null,
    };

    /// <summary>
    /// An argument that is the forwarder's parameter at the callee's same position, by value on both ends and of the same
    /// type, with no conversion: anything else between the two is not an <see cref="IParameterReferenceOperation"/>.
    /// </summary>
    private static bool PassesItsOwnParameter(IArgumentOperation argument)
    {
        IParameterSymbol to = argument.Parameter!;
        return argument.ArgumentKind == ArgumentKind.Explicit
            && argument.Value is IParameterReferenceOperation passed
            && to.RefKind == RefKind.None
            && passed.Parameter.RefKind == RefKind.None
            && passed.Parameter.Ordinal == to.Ordinal
            && SymbolEqualityComparer.Default.Equals(passed.Parameter.Type, to.Type);
    }

    /// <summary>
    /// The compilation whose source declares <paramref name="method"/>: <paramref name="compilation"/> itself, or the
    /// project it references as source. Null when neither holds <paramref name="tree"/>.
    /// </summary>
    private static Compilation? Owner(IMethodSymbol method, SyntaxTree tree, Compilation compilation) =>
        compilation.ContainsSyntaxTree(tree) ? compilation : (compilation.GetMetadataReference(method.ContainingAssembly) as CompilationReference)?.Compilation;

    /// <summary>Whether an attribute of <paramref name="method"/> or of its type can keep a call from reaching the body, or the call from being compiled.</summary>
    private static bool ChangesTheCall(IMethodSymbol method) =>
        method.GetAttributes().Concat(method.ContainingType.GetAttributes()).Select(static a => a.AttributeClass).OfType<INamedTypeSymbol>().Select(static c => c.ToDisplayString()).Any(static name =>
            name.StartsWith("System.Security.", StringComparison.Ordinal) || string.Equals(name, "System.Diagnostics.ConditionalAttribute", StringComparison.Ordinal));

    /// <summary>A forwarder's target and the compilation its symbol belongs to, which is the one its identity and its own body are read with.</summary>
    internal sealed record Resolved(IMethodSymbol Target, Compilation Compilation);
}
