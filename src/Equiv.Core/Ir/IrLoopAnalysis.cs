using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// Loop structure of a procedure (ticket M3-002): the blocks reachable from the entry in reverse postorder,
/// the back edges found by a depth-first search, one natural loop per header (back edges that share a header
/// share a loop), and the loop nesting forest. <see cref="Loops"/> lists the forest in pre-order, outer before
/// inner and siblings by the reverse-postorder position of their headers, which is how the ladder pairs loops
/// across a pair. A back edge whose target does not dominate its source makes the CFG irreducible; the Roslyn
/// CFG never is, and the ladder reports such a procedure as an unaligned loop.
/// </summary>
public sealed class IrLoopAnalysis
{
    private readonly Dictionary<IrBlockId, IrBlock> blocks;

    private IrLoopAnalysis(IrProcedure procedure)
    {
        blocks = procedure.Blocks.ToDictionary(static b => b.Id);
        (List<IrBlock> order, List<(IrBlockId From, IrBlockId To)> backEdges) = Search(procedure.Entry);
        ReversePostorder = [.. order];
        Dictionary<IrBlockId, int> position = order.Select(static (b, i) => (b.Id, i)).ToDictionary(static p => p.Id, static p => p.i);
        Dictionary<IrBlockId, HashSet<IrBlockId>> predecessors = order.ToDictionary(static b => b.Id, static _ => new HashSet<IrBlockId>());
        foreach (IrBlock block in order)
        {
            foreach (IrBlockId successor in block.Terminator.Successors())
            {
                predecessors[successor].Add(block.Id);
            }
        }

        List<(IrBlockId Header, HashSet<IrBlockId> Body, List<IrBlockId> Latches)> natural = [];
        foreach (IGrouping<IrBlockId, (IrBlockId From, IrBlockId To)> edges in backEdges.GroupBy(static e => e.To))
        {
            HashSet<IrBlockId> body = [edges.Key];
            Stack<IrBlockId> pending = new(edges.Select(static e => e.From));
            while (pending.TryPop(out IrBlockId? block))
            {
                if (body.Add(block))
                {
                    IsReducible &= block != procedure.Entry;
                    foreach (IrBlockId predecessor in predecessors[block])
                    {
                        pending.Push(predecessor);
                    }
                }
            }

            natural.Add((edges.Key, body, [.. edges.Select(static e => e.From).Distinct()]));
        }

        List<IrLoop> loops = [.. natural.Select(loop => new IrLoop(
            loop.Header,
            [.. loop.Body.OrderBy(b => position[b])],
            [.. loop.Latches.OrderBy(b => position[b])],
            [.. loop.Body.OrderBy(b => position[b]).SelectMany(b => blocks[b].Terminator.Successors().Distinct().Where(s => !loop.Body.Contains(s)).Select(s => (b, s)))],
            natural
                .Where(outer => outer.Header != loop.Header && outer.Body.Contains(loop.Header))
                .OrderBy(static outer => outer.Body.Count)
                .Select(static outer => outer.Header)
                .FirstOrDefault()))];
        Loops = [.. PreOrder(loops, parent: null, position)];
        IsSelfRecursive = order
            .SelectMany(static b => b.Instructions.OfType<IrCall>())
            .Any(c => string.Equals(c.Callee.Value, procedure.Identity.Value, StringComparison.Ordinal));
    }

    /// <summary>The blocks reachable from the entry, in reverse postorder.</summary>
    public ImmutableArray<IrBlock> ReversePostorder { get; }

    /// <summary>The loop nesting forest in pre-order.</summary>
    public ImmutableArray<IrLoop> Loops { get; }

    /// <summary>False when some back edge's target does not dominate its source.</summary>
    public bool IsReducible { get; } = true;

    /// <summary>Whether a reachable block calls the procedure itself (its own identity).</summary>
    public bool IsSelfRecursive { get; }

    public static IrLoopAnalysis Of(IrProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        return new IrLoopAnalysis(procedure);
    }

    private static IEnumerable<IrLoop> PreOrder(List<IrLoop> loops, IrBlockId? parent, Dictionary<IrBlockId, int> position) =>
        loops
            .Where(l => l.Parent == parent)
            .OrderBy(l => position[l.Header])
            .SelectMany(l => PreOrder(loops, l.Header, position).Prepend(l));

    /// <summary>Iterative depth-first search: the reachable blocks in reverse postorder, and every edge into a block still on the stack.</summary>
    private (List<IrBlock> Order, List<(IrBlockId From, IrBlockId To)> BackEdges) Search(IrBlockId entry)
    {
        Dictionary<IrBlockId, bool> onStack = new() { [entry] = true };
        List<IrBlock> postorder = [];
        List<(IrBlockId, IrBlockId)> backEdges = [];
        Stack<(IrBlock Block, int Next)> stack = new([(blocks[entry], 0)]);
        while (stack.TryPop(out (IrBlock Block, int Next) frame))
        {
            ImmutableArray<IrBlockId> successors = frame.Block.Terminator.Successors();
            if (frame.Next == successors.Length)
            {
                onStack[frame.Block.Id] = false;
                postorder.Add(frame.Block);
                continue;
            }

            stack.Push((frame.Block, frame.Next + 1));
            IrBlockId successor = successors[frame.Next];
            if (!onStack.TryGetValue(successor, out bool active))
            {
                onStack[successor] = true;
                stack.Push((blocks[successor], 0));
            }
            else if (active)
            {
                backEdges.Add((frame.Block.Id, successor));
            }
        }

        postorder.Reverse();
        return (postorder, backEdges);
    }
}
