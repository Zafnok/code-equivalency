using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// What a <c>switch</c> expression does when no arm matches (ADR 0024 as clarified by ticket P2-144). The compiler ends it
/// with a throw the source does not write: of <c>System.Runtime.CompilerServices.SwitchExpressionException</c> where the
/// reference assemblies have the type (.NET Core 3.0 and later) and of <c>System.InvalidOperationException</c> where they
/// do not (.NET Framework). So the same text throws two types across that boundary, and the bound fingerprint and the
/// lowering both have to say which. An expression whose arms cover every value
/// (<see cref="ISwitchExpressionOperation.IsExhaustive"/>: a discard or <c>var</c> arm, <c>true</c> and <c>false</c>) has
/// no such throw: the compiler emits no code for it, and neither the fingerprint nor the lowered body holds one.
/// </summary>
internal static class SwitchExpressions
{
    private const string NoMatchException = "System.Runtime.CompilerServices.SwitchExpressionException";

    private const string FallbackException = "System.InvalidOperationException";

    /// <summary>
    /// The constructor the compiler calls where no arm matches, as it looks it up in <paramref name="compilation"/>: the
    /// parameterless one of <c>SwitchExpressionException</c>, else of <c>InvalidOperationException</c>. Null when the
    /// compilation has neither, and the body does not bind.
    /// </summary>
    public static IMethodSymbol? NoMatchConstructor(Compilation compilation) =>
        Parameterless(compilation.GetTypeByMetadataName(NoMatchException)) ?? Parameterless(compilation.GetTypeByMetadataName(FallbackException));

    /// <summary>
    /// The blocks of <paramref name="graph"/>, by ordinal, that no input reaches although Roslyn marks them reachable: the
    /// no-match throw of each <c>switch</c> expression whose arms cover every value. Roslyn's graph ends every
    /// <c>switch</c> expression with that throw, behind the failing edge of the last arm's test, and gives the exception's
    /// creation the expression's own syntax, which no creation the source writes has.
    /// </summary>
    public static ImmutableHashSet<int> Unreached(ControlFlowGraph graph, Compilation compilation) =>
    [
        .. graph.Blocks
            .Where(static block => block.BranchValue is IObjectCreationOperation { Syntax: SwitchExpressionSyntax })
            .Where(block => Written(block.BranchValue!.Syntax, compilation).OfType<ISwitchExpressionOperation>().Any(static expression => expression.IsExhaustive))
            .Select(static block => block.Ordinal),
    ];

    /// <summary>The operation the source writes at <paramref name="syntax"/>, which for a <c>switch</c> expression's syntax is the expression.</summary>
    private static IOperation?[] Written(SyntaxNode syntax, Compilation compilation) =>
        [compilation.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax)];

    private static IMethodSymbol? Parameterless(INamedTypeSymbol? type) =>
        type?.InstanceConstructors.FirstOrDefault(static constructor => constructor.Parameters.IsEmpty);
}
