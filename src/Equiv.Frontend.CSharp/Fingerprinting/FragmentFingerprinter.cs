using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Fingerprinting;

/// <summary>
/// The bound fingerprint of an expression-level fragment the lowerer cannot lower, and the variables it reads (ADR 0024
/// decision 2; ticket M4-004). A fragment is a function of what it reads, the heap and its position only when nothing else
/// reaches into it, so it gets no fingerprint when:
/// <list type="bullet">
/// <item>it is not an expression, or holds a flow capture, a null test or a caught exception of the graph, or a struct's
/// <c>this</c>: its value then depends on something computed outside it;</item>
/// <item>it writes a local or parameter declared outside it (a call's only output is its result);</item>
/// <item>it refers to a local function declared outside it, whose body and captures its fingerprint does not hold;</item>
/// <item>it reads a <c>ref</c> local;</item>
/// <item>a lambda in it captures a variable that a lambda or local function anywhere in the graph writes, since that write
/// can run after the fragment; the lowerer rejects one whose captured variable is assigned after it in the graph;</item>
/// <item>it is runtime-sensitive, by M3-015's rule.</item>
/// </list>
/// Its reads are the locals and parameters declared outside it that it references, a lambda's captures included, in order of
/// first occurrence. Its text is <see cref="BoundSerialiser.SerialiseFragment"/>'s.
/// </summary>
internal sealed class FragmentFingerprinter(
    IMethodSymbol method,
    Compilation compilation,
    RenameMap renames,
    ImmutableArray<string> suppressedRuntimeChanges,
    ImmutableArray<ApiEquivalence> equivalences,
    bool legacy)
{
    /// <summary>The operations only a control-flow graph holds, whose values the graph computes outside the fragment.</summary>
    private static readonly FrozenSet<OperationKind> GraphOnly =
        new[] { OperationKind.FlowCapture, OperationKind.FlowCaptureReference, OperationKind.IsNull, OperationKind.CaughtException }.ToFrozenSet();

    private readonly Dictionary<ControlFlowGraph, ImmutableHashSet<ISymbol>> writtenByFunctions = [];

    /// <summary><paramref name="operation"/>'s fragment, an operation of <paramref name="graph"/>, or null when it gets no fingerprint.</summary>
    public Fragment? Of(IOperation operation, ControlFlowGraph graph)
    {
        if (operation.Syntax is not ExpressionSyntax syntax)
        {
            return null;
        }

        IOperation Lambda(IFlowAnonymousFunctionOperation flow) => graph.GetAnonymousFunctionControlFlowGraph(flow).OriginalOperation;
        bool Outer(ISymbol symbol) => IsOuter(symbol, syntax);
        ImmutableArray<IOperation> tree = [.. Walk(operation, Lambda)];
        // Roslyn fails a data-flow region only where no bound node spans it, such as a type in a cast; an operation's own
        // syntax always has one.
        DataFlowAnalysis flow = compilation.GetSemanticModel(syntax.SyntaxTree).AnalyzeDataFlow(syntax);
        if (tree.Any(o => !IsSelfContained(o, syntax))
            || flow.WrittenInside.Any(Outer))
        {
            return null;
        }

        ImmutableArray<ISymbol> captured = [.. flow.CapturedInside.Where(Outer)];
        ImmutableArray<ISymbol> reads = [.. tree.Select(Read).OfType<ISymbol>().Where(Outer).Distinct(SymbolEqualityComparer.Default)];
        if (captured.Any(WrittenByFunctions(graph).Contains)
            || reads.Any(static r => r is ILocalSymbol { RefKind: not RefKind.None }))
        {
            return null;
        }

        (string text, bool runtimeSensitive) = BoundSerialiser.SerialiseFragment(method, compilation, operation, Lambda, renames, suppressedRuntimeChanges, equivalences, legacy);
        return runtimeSensitive ? null : new Fragment(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), reads, captured);
    }

    /// <summary>Every operation of the fragment in pre-order, each lambda of the graph replaced by its bound body.</summary>
    private static IEnumerable<IOperation> Walk(IOperation operation, Func<IFlowAnonymousFunctionOperation, IOperation> lambda)
    {
        IOperation node = operation is IFlowAnonymousFunctionOperation flow ? lambda(flow) : operation;
        return [node, .. node.ChildOperations.SelectMany(c => Walk(c, lambda))];
    }

    private static ISymbol? Read(IOperation operation) => operation switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="operation"/> holds nothing computed outside the fragment: no flow capture, null test or caught
    /// exception of the graph, no struct's <c>this</c>, and no local function declared outside <paramref name="syntax"/>.
    /// </summary>
    private static bool IsSelfContained(IOperation operation, SyntaxNode syntax) => operation switch
    {
        _ when GraphOnly.Contains(operation.Kind) => false,
        IInstanceReferenceOperation reference => !reference.Type!.IsValueType,
        _ => LocalFunction(operation) is not { } local || IsInside(local, syntax),
    };

    /// <summary>The local function <paramref name="operation"/> calls or converts to a delegate, if any.</summary>
    private static IMethodSymbol? LocalFunction(IOperation operation) => operation switch
    {
        IInvocationOperation invocation => invocation.TargetMethod,
        IMethodReferenceOperation reference => reference.Method,
        _ => null,
    } is { MethodKind: MethodKind.LocalFunction } local ? local : null;

    private static bool IsInside(ISymbol symbol, SyntaxNode syntax) => symbol.DeclaringSyntaxReferences.All(r => syntax.Contains(r.GetSyntax()));

    /// <summary>
    /// A local or parameter declared outside <paramref name="syntax"/>, which the fragment reads from the method rather than
    /// declares itself. <c>this</c> is never one: it is the same input on both sides and never changes in a class.
    /// </summary>
    private static bool IsOuter(ISymbol symbol, SyntaxNode syntax) =>
        symbol is ILocalSymbol or IParameterSymbol { IsThis: false } && !IsInside(symbol, syntax);

    /// <summary>The variables that a lambda or local function in <paramref name="graph"/> writes.</summary>
    private ImmutableHashSet<ISymbol> WrittenByFunctions(ControlFlowGraph graph)
    {
        if (!writtenByFunctions.TryGetValue(graph, out ImmutableHashSet<ISymbol>? written))
        {
            written = graph.OriginalOperation.DescendantsAndSelf()
                .Where(static o => o is IAnonymousFunctionOperation or ILocalFunctionOperation)
                .SelectMany(o =>
                {
                    SemanticModel model = compilation.GetSemanticModel(o.Syntax.SyntaxTree);
                    return (o.Syntax is ExpressionSyntax lambda ? model.AnalyzeDataFlow(lambda) : model.AnalyzeDataFlow((StatementSyntax)o.Syntax)).WrittenInside;
                })
                .ToImmutableHashSet(SymbolEqualityComparer.Default);
            writtenByFunctions[graph] = written;
        }

        return written;
    }

    /// <summary>A fragment's fingerprint, the variables it reads in order of first occurrence, and those a lambda in it captures.</summary>
    internal sealed record Fragment(string Fingerprint, ImmutableArray<ISymbol> Reads, ImmutableArray<ISymbol> Captured);
}
