using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The scopes of one graph that a loop enters afresh and that declare a local a lambda of the graph names (ticket P2-182):
/// a loop body's locals, a <c>foreach</c>'s iteration variable. Each entry of such a scope is a new instance of its locals,
/// for which the compiled code allocates a new closure, so a store the next iteration makes is to another variable than
/// the one a lambda of this iteration captured. A scope counts when its first block is on a cycle and every edge into that
/// block comes from outside the scope. One that is not entered that way, such as one whose first label a <c>goto</c>
/// inside it jumps back to, does not count: a store anywhere after a conversion that captures its locals still makes the
/// conversion opaque, which is always sound.
/// </summary>
internal static class RenewedScopes
{
    public static ImmutableArray<ControlFlowRegion> Find(ControlFlowGraph graph)
    {
        ImmutableHashSet<ILocalSymbol> named = Named(graph);
        return
        [
            .. Regions(graph.Root).Where(r =>
                r.Kind == ControlFlowRegionKind.LocalLifetime
                && r.Locals.Any(named.Contains)
                && graph.Blocks[r.FirstBlockOrdinal].Predecessors.All(p => !Inside(r, p.Source))
                && OnACycle(graph.Blocks[r.FirstBlockOrdinal])),
        ];
    }

    /// <summary>The locals a lambda of <paramref name="graph"/> names, a nested lambda's included: a superset of those it captures.</summary>
    private static ImmutableHashSet<ILocalSymbol> Named(ControlFlowGraph graph) =>
        graph.Blocks
            .SelectMany(static b => b.Operations.Append(b.BranchValue))
            .OfType<IOperation>()
            .SelectMany(static o => o.DescendantsAndSelf())
            .OfType<IFlowAnonymousFunctionOperation>()
            .SelectMany(l => graph.GetAnonymousFunctionControlFlowGraph(l).OriginalOperation.DescendantsAndSelf())
            .OfType<ILocalReferenceOperation>()
            .Select(static r => r.Local)
            .ToImmutableHashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

    private static IEnumerable<ControlFlowRegion> Regions(ControlFlowRegion region) => [region, .. region.NestedRegions.SelectMany(Regions)];

    private static bool Inside(ControlFlowRegion region, BasicBlock block) => block.Ordinal >= region.FirstBlockOrdinal && block.Ordinal <= region.LastBlockOrdinal;

    /// <summary>Whether a path from <paramref name="first"/> leads back to it.</summary>
    private static bool OnACycle(BasicBlock first)
    {
        HashSet<BasicBlock> reached = [];
        Stack<BasicBlock> pending = new(Successors(first));
        while (pending.TryPop(out BasicBlock? block))
        {
            if (block == first)
            {
                return true;
            }

            foreach (BasicBlock successor in Successors(block).Where(reached.Add))
            {
                pending.Push(successor);
            }
        }

        return false;
    }

    private static IEnumerable<BasicBlock> Successors(BasicBlock block) =>
        new[] { block.FallThroughSuccessor?.Destination, block.ConditionalSuccessor?.Destination }.OfType<BasicBlock>();
}
