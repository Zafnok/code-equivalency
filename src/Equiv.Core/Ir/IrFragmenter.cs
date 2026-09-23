using System.Collections.Immutable;
using System.Globalization;

namespace Equiv.Core.Ir;

/// <summary>
/// Acyclic fragments of a looping procedure for lockstep induction (VERIFICATION-MODEL.md section 5.1; ticket
/// M3-002). A procedure is cut at its loop headers. A <see cref="Segment"/> runs from the entry, or from one
/// header, up to the next header it reaches or to an exit of the procedure; every cycle passes a header, so a
/// segment is acyclic. Reaching header <c>i</c> of the cut list is an exit of the segment: a call event
/// <c>equiv:cut:i</c> whose arguments are that header's <see cref="State"/> on the edge taken, then a throw of
/// <c>equiv:cut</c>. Comparing two segments' traces therefore compares which header each reaches and the state it
/// carries there, and their ordinary exits compare as the procedure's do.
/// </summary>
public static class IrFragmenter
{
    /// <summary>The callee identity prefix of a cut event.</summary>
    public const string CutEvent = "equiv:cut:";

    /// <summary>The exception type a segment throws after a cut event.</summary>
    public const string CutException = "equiv:cut";

    /// <summary>
    /// The loop state at <paramref name="header"/>: its phis in order, then every other variable live on entry to it,
    /// parameters first in declaration order and the rest in definition order. Heap maps, the values a loop reads but
    /// never changes (loop-invariant live-ins) and the values used after the loop are all in it.
    /// </summary>
    public static ImmutableArray<IrVar> State(IrProcedure procedure, IrBlockId header)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(header);
        ImmutableArray<IrBlock> order = IrLoopAnalysis.Of(procedure).ReversePostorder;
        Dictionary<string, (IrVar Var, int Position)> definitions = procedure.Parameters
            .Select((p, i) => (p.Var, Position: i - procedure.Parameters.Length))
            .Concat(order.SelectMany(static b => b.Instructions).SelectMany(static i => i.Definitions()).Select(static (v, i) => (Var: v, Position: i)))
            .ToDictionary(static d => d.Var.Name, StringComparer.Ordinal);
        IrBlock block = order.First(b => b.Id == header);
        return
        [
            .. block.Instructions.OfType<IrPhi>().Select(static p => p.Target),
            .. LiveIn(order)[header].Select(name => definitions[name]).OrderBy(static d => d.Position).Select(static d => d.Var),
        ];
    }

    /// <summary>
    /// The segment of <paramref name="procedure"/> that starts at the entry (<paramref name="start"/> null) or at the
    /// header <paramref name="start"/>, cut at every header of <paramref name="cuts"/> (each with the state its cut
    /// event carries, from <see cref="State"/> or a reordering of it). A segment from a header takes that header's
    /// state as its first parameters, <c>$s0</c>, <c>$s1</c>, ..., followed by the procedure's own parameters, which
    /// its ordinary exits need for their <c>outs</c>.
    /// </summary>
    public static IrProcedure Segment(IrProcedure procedure, IrBlockId? start, IReadOnlyList<(IrBlockId Header, ImmutableArray<IrVar> State)> cuts)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(cuts);
        IrProcedure pruned = IrUnroller.Prune(procedure);
        Dictionary<IrBlockId, IrBlock> blocks = pruned.Blocks.ToDictionary(static b => b.Id);
        Dictionary<IrBlockId, int> cutIndex = cuts.Select(static (c, i) => (c.Header, i)).ToDictionary(static c => c.Header, static c => c.i);
        IrBlockId root = start ?? pruned.Entry;
        HashSet<IrBlockId> region = Region(blocks, root, cutIndex);
        Dictionary<string, IrVar> slots = start is null ? [] : Slots(pruned, cuts[cutIndex[start]].State);
        IrVar Var(IrVar var) => slots.GetValueOrDefault(var.Name, var);

        int next = pruned.Blocks.Max(static b => b.Id.Value) + 1;
        Dictionary<(IrBlockId From, IrBlockId To), IrBlockId> exits = [];
        IrBlockId Target(IrBlockId from, IrBlockId to)
        {
            if (!cutIndex.ContainsKey(to))
            {
                return to;
            }

            if (!exits.TryGetValue((from, to), out IrBlockId? exit))
            {
                exit = new IrBlockId(next++);
                exits.Add((from, to), exit);
            }

            return exit;
        }

        List<IrBlock> kept = [.. pruned.Blocks.Where(b => region.Contains(b.Id)).Select(b => b with
        {
            Instructions = [.. b.Instructions.Where(i => !(i is IrPhi && b.Id == start)).Select(i => IrUnroller.Rewrite(i, Var))],
            Terminator = IrUnroller.Rewrite(b.Terminator, Var, t => Target(b.Id, t)),
        })];
        ImmutableArray<IrOut> unchanged = [.. pruned.Parameters.Where(static p => p.Kind != IrParameterKind.In).Select(static p => new IrOut(p.Var, p.Var))];
        kept.AddRange(exits.Select(e => new IrBlock(
            e.Value,
            [new IrCall(Target: null, Threw: null, new CallIdentity(CutEvent + cutIndex[e.Key.To].ToString(CultureInfo.InvariantCulture)), [.. Carried(blocks[e.Key.To], e.Key.From, cuts[cutIndex[e.Key.To]].State).Select(Var)])],
            new IrThrow(CutException, unchanged))));

        IrProcedure segment = pruned with
        {
            Parameters = [.. slots.Values.Select(static v => new IrParameter(v, IrParameterKind.In)), .. pruned.Parameters],
            Blocks = [.. kept],
            Entry = root,
        };
        IrProcedure result = IrUnroller.Prune(segment);
        IrUnroller.Validate(procedure, result);
        return result;
    }

    /// <summary>The blocks reachable from <paramref name="root"/> without entering a cut header.</summary>
    private static HashSet<IrBlockId> Region(Dictionary<IrBlockId, IrBlock> blocks, IrBlockId root, Dictionary<IrBlockId, int> cuts)
    {
        HashSet<IrBlockId> region = [root];
        Stack<IrBlockId> pending = new([root]);
        while (pending.TryPop(out IrBlockId? block))
        {
            foreach (IrBlockId successor in blocks[block].Terminator.Successors().Where(s => !cuts.ContainsKey(s) && region.Add(s)))
            {
                pending.Push(successor);
            }
        }

        return region;
    }

    /// <summary>One fresh parameter name <c>$s&lt;i&gt;</c> per state variable, never a name the procedure already uses.</summary>
    private static Dictionary<string, IrVar> Slots(IrProcedure procedure, ImmutableArray<IrVar> state)
    {
        HashSet<string> names = new(
            procedure.Parameters.Select(static p => p.Var.Name).Concat(procedure.Blocks.SelectMany(static b => b.Instructions).SelectMany(static i => i.Definitions()).Select(static v => v.Name)),
            StringComparer.Ordinal);
        Dictionary<string, IrVar> slots = new(StringComparer.Ordinal);
        foreach ((IrVar var, int index) in state.Select(static (v, i) => (v, i)))
        {
            string name = "$s" + index.ToString(CultureInfo.InvariantCulture);
            while (!names.Add(name))
            {
                name += "$";
            }

            slots.Add(var.Name, var with { Name = name });
        }

        return slots;
    }

    /// <summary>The value each state variable of <paramref name="header"/> has on the edge from <paramref name="from"/>: a phi's operand, else the variable itself.</summary>
    private static IEnumerable<IrVar> Carried(IrBlock header, IrBlockId from, ImmutableArray<IrVar> state)
    {
        Dictionary<string, IrVar> phis = header.Instructions
            .OfType<IrPhi>()
            .ToDictionary(static p => p.Target.Name, p => p.Incoming.First(e => e.From == from).Value, StringComparer.Ordinal);
        return state.Select(v => phis.GetValueOrDefault(v.Name, v));
    }

    /// <summary>
    /// Variables live on entry to each block (by name), SSA-style: a phi operand is live at the end of its
    /// predecessor, not at the phi's block, and a phi's target is defined at the top of its block.
    /// </summary>
    private static Dictionary<IrBlockId, HashSet<string>> LiveIn(ImmutableArray<IrBlock> order)
    {
        Dictionary<IrBlockId, HashSet<string>> live = order.ToDictionary(static b => b.Id, static _ => new HashSet<string>(StringComparer.Ordinal));
        Dictionary<IrBlockId, IrBlock> byId = order.ToDictionary(static b => b.Id);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (IrBlock block in order.Reverse())
            {
                HashSet<string> needed = [.. block.Terminator.Successors().SelectMany(s => live[s].Concat(Operands(byId[s], block.Id))), .. block.Terminator.Uses().Select(static v => v.Name)];
                foreach (IrInstruction instruction in block.Instructions.Reverse())
                {
                    needed.ExceptWith(instruction.Definitions().Select(static v => v.Name));
                    needed.UnionWith(instruction.Uses().Select(static v => v.Name));
                }

                changed |= !needed.SetEquals(live[block.Id]);
                live[block.Id] = needed;
            }
        }

        return live;
    }

    private static IEnumerable<string> Operands(IrBlock block, IrBlockId from) =>
        block.Instructions.OfType<IrPhi>().SelectMany(p => p.Incoming.Where(e => e.From == from)).Select(static e => e.Value.Name);
}
