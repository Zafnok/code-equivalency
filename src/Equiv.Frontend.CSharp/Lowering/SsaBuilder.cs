using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Ir;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// SSA construction after Braun, Buchwald, Hack, Leißa, Mallon and Zwinkau, "Simple and Efficient
/// Construction of Static Single Assignment Form" (CC 2013). Pass 1 (the lowerer) fills draft
/// blocks with instructions plus <see cref="Load"/>/<see cref="Store"/> steps on mutable
/// <see cref="Variable"/>s. Pass 2 (<see cref="Build"/>) walks the blocks in reverse postorder,
/// numbering values per block, sealing a block once every predecessor is filled, and removing
/// trivial phis, finally rewriting every operand to its surviving value.
/// </summary>
internal sealed class SsaBuilder
{
    private readonly List<Draft> drafts = [];
    private readonly HashSet<IrVar> renameable = [];
    private readonly Dictionary<IrVar, IrVar> alias = [];
    private readonly Dictionary<(Variable, IrBlockId), IrVar> definitions = [];
    private readonly Dictionary<IrBlockId, List<IrBlockId>> predecessors = [];
    private readonly HashSet<IrBlockId> filled = [];
    private readonly HashSet<IrBlockId> sealedBlocks = [];
    private readonly Dictionary<IrBlockId, List<(Variable Variable, Phi Phi)>> incomplete = [];
    private readonly List<Phi> phis = [];
    private readonly List<IrOpaque> undefined = [];
    private SourceSpan? span;
    private int counter;

    public IrBlockId NewBlock()
    {
        Draft draft = new(new IrBlockId(drafts.Count));
        drafts.Add(draft);
        return draft.Id;
    }

    /// <summary>A fresh temporary. If it is later stored to a variable, it takes the variable's name.</summary>
    public IrVar Temp(IrType type)
    {
        IrVar temp = new($"${(counter++).ToString(CultureInfo.InvariantCulture)}", type);
        renameable.Add(temp);
        return temp;
    }

    public void Emit(IrBlockId block, IrInstruction instruction) => drafts[block.Value].Steps.Add(new Instruction(instruction));

    /// <summary>Reads <paramref name="variable"/> at this point of <paramref name="block"/>.</summary>
    public IrVar Load(IrBlockId block, Variable variable)
    {
        IrVar temp = new($"${(counter++).ToString(CultureInfo.InvariantCulture)}", variable.Template.Type);
        drafts[block.Value].Steps.Add(new LoadStep(variable, temp));
        return temp;
    }

    public void Store(IrBlockId block, Variable variable, IrVar value) => drafts[block.Value].Steps.Add(new StoreStep(variable, value));

    public void Terminate(IrBlockId block, IrTerminator terminator) => drafts[block.Value].Terminator = terminator;

    /// <summary>
    /// Pass 2. Blocks unreachable from <paramref name="entry"/> are dropped. Every exit's outs name the
    /// value of each <paramref name="outs"/> variable live there. A read with no reaching definition
    /// becomes an <see cref="IrOpaque"/> with reason <c>undefined</c> at <paramref name="bodySpan"/>.
    /// </summary>
    public ImmutableArray<IrBlock> Build(IrBlockId entry, ImmutableArray<(Variable Variable, IrVar Param)> outs, SourceSpan bodySpan)
    {
        span = bodySpan;
        List<IrBlockId> order = [];
        Walk(entry, [], order);
        order.Reverse();
        foreach (IrBlockId block in order)
        {
            predecessors[block] = [.. order.Where(p => Successors(drafts[p.Value].Terminator!).Contains(block))];
            incomplete[block] = [];
        }

        foreach (IrBlockId block in order)
        {
            TrySeal(block);
            Fill(drafts[block.Value], outs);
            filled.Add(block);
            foreach (IrBlockId successor in Successors(drafts[block.Value].Terminator!))
            {
                TrySeal(successor);
            }
        }

        // Instead of Braun's per-user recursion: a phi becomes trivial only after a phi it uses was removed, so repeat until none is.
        bool changed = true;
        while (changed)
        {
            changed = phis.Where(static p => !p.Removed).ToList().Exists(p => TryRemoveTrivial(p) != p.Target);
        }

        return [.. drafts.Where(d => filled.Contains(d.Id)).Select(d => Assemble(d, d.Id == entry))];
    }

    private static IEnumerable<IrBlockId> Successors(IrTerminator terminator) => terminator switch
    {
        IrGoto jump => [jump.Target],
        IrBranch branch => [branch.Then, branch.Else],
        IrSwitch choice => [.. choice.Cases.Select(static c => c.Target), choice.Default],
        _ => [],
    };

    private void Walk(IrBlockId block, HashSet<IrBlockId> visited, List<IrBlockId> postorder)
    {
        visited.Add(block);
        foreach (IrBlockId successor in Successors(drafts[block.Value].Terminator!).Where(s => !visited.Contains(s)))
        {
            Walk(successor, visited, postorder);
        }

        postorder.Add(block);
    }

    private void Fill(Draft draft, ImmutableArray<(Variable Variable, IrVar Param)> outs)
    {
        foreach (IStep step in draft.Steps)
        {
            switch (step)
            {
                case LoadStep load:
                    alias[load.Temp] = ReadVariable(load.Variable, draft.Id);
                    break;
                case StoreStep store:
                    WriteVariable(store.Variable, draft.Id, Name(store.Variable, Resolve(store.Value)));
                    break;
            }
        }

        draft.Terminator = draft.Terminator! switch
        {
            IrReturn exit => exit with { Outs = Outs(outs, draft.Id) },
            IrThrow exit => exit with { Outs = Outs(outs, draft.Id) },
            var other => other,
        };
    }

    private ImmutableArray<IrOut> Outs(ImmutableArray<(Variable Variable, IrVar Param)> outs, IrBlockId block) =>
        [.. outs.Select(o => new IrOut(o.Param, ReadVariable(o.Variable, block)))];

    /// <summary>A stored temporary takes the variable's source name (<c>x.3</c>); anything else is stored as is.</summary>
    private IrVar Name(Variable variable, IrVar value)
    {
        if (!renameable.Remove(value))
        {
            return value;
        }

        IrVar named = new($"{variable.Template.Name}.{(counter++).ToString(CultureInfo.InvariantCulture)}", value.Type, variable.Template.SourceName);
        alias[value] = named;
        return named;
    }

    private void WriteVariable(Variable variable, IrBlockId block, IrVar value) => definitions[(variable, block)] = value;

    private IrVar ReadVariable(Variable variable, IrBlockId block) =>
        definitions.TryGetValue((variable, block), out IrVar? value) ? Resolve(value) : ReadVariableRecursive(variable, block);

    private IrVar ReadVariableRecursive(Variable variable, IrBlockId block)
    {
        IrVar value;
        if (!sealedBlocks.Contains(block))
        {
            Phi phi = NewPhi(variable, block);
            incomplete[block].Add((variable, phi));
            value = phi.Target;
        }
        else if (predecessors[block].Count == 1)
        {
            value = ReadVariable(variable, predecessors[block][0]);
        }
        else
        {
            Phi phi = NewPhi(variable, block);
            WriteVariable(variable, block, phi.Target);
            value = AddPhiOperands(variable, phi);
        }

        WriteVariable(variable, block, value);
        return value;
    }

    private Phi NewPhi(Variable variable, IrBlockId block)
    {
        Phi phi = new(new IrVar($"{variable.Template.Name}.{(counter++).ToString(CultureInfo.InvariantCulture)}", variable.Template.Type, variable.Template.SourceName), block);
        phis.Add(phi);
        return phi;
    }

    private IrVar AddPhiOperands(Variable variable, Phi phi)
    {
        foreach (IrBlockId predecessor in predecessors[phi.Block])
        {
            phi.Operands.Add((predecessor, ReadVariable(variable, predecessor)));
        }

        return TryRemoveTrivial(phi);
    }

    /// <summary>A phi whose operands are itself and one other value is that value; with no other value it is undefined.</summary>
    private IrVar TryRemoveTrivial(Phi phi)
    {
        IrVar? same = null;
        foreach (IrVar operand in phi.Operands.Select(o => Resolve(o.Value)).Where(o => o != phi.Target))
        {
            if (same is not null && operand != same)
            {
                return phi.Target;
            }

            same = operand;
        }

        same ??= Undefined(phi.Target.Type);
        phi.Removed = true;
        alias[phi.Target] = same;
        return same;
    }

    private IrVar Undefined(IrType type)
    {
        IrVar value = new($"${(counter++).ToString(CultureInfo.InvariantCulture)}", type);
        undefined.Add(new IrOpaque(value, "undefined", span!));
        return value;
    }

    private void TrySeal(IrBlockId block)
    {
        if (sealedBlocks.Contains(block) || !predecessors[block].TrueForAll(filled.Contains))
        {
            return;
        }

        foreach ((Variable variable, Phi phi) in incomplete[block])
        {
            AddPhiOperands(variable, phi);
        }

        sealedBlocks.Add(block);
    }

    private IrVar Resolve(IrVar value)
    {
        while (alias.TryGetValue(value, out IrVar? next))
        {
            value = next;
        }

        return value;
    }

    private IrBlock Assemble(Draft draft, bool isEntry) => new(
        draft.Id,
        [
            .. isEntry ? undefined : [],
            .. phis.Where(p => p.Block == draft.Id && !p.Removed)
                .Select(p => (IrInstruction)new IrPhi(Resolve(p.Target), [.. p.Operands.Select(o => (o.From, Resolve(o.Value)))])),
            .. draft.Steps.OfType<Instruction>().Select(s => Rewrite(s.Value)),
        ],
        Rewrite(draft.Terminator!));

    private IrInstruction Rewrite(IrInstruction instruction) => instruction switch
    {
        IrConst c => c with { Target = Resolve(c.Target) },
        IrBinary b => b with { Target = Resolve(b.Target), A = Resolve(b.A), B = Resolve(b.B) },
        IrUnary u => u with { Target = Resolve(u.Target), A = Resolve(u.A) },
        IrOverflows o => o with { Target = Resolve(o.Target), A = Resolve(o.A), B = Resolve(o.B) },
        IrCall c => c with { Target = c.Target is null ? null : Resolve(c.Target), Threw = Resolve(c.Threw!), Args = [.. c.Args.Select(Resolve)] },
        IrMapRead r => r with { Target = Resolve(r.Target), Map = Resolve(r.Map), Key = Resolve(r.Key) },
        IrMapWrite w => w with { Target = Resolve(w.Target), Map = Resolve(w.Map), Key = Resolve(w.Key), Value = Resolve(w.Value) },
        IrPure p => p with { Target = Resolve(p.Target), Throws = [.. p.Throws.Select(t => t with { Flag = Resolve(t.Flag) })], Args = [.. p.Args.Select(Resolve)] },
        _ => Rewrite((IrOpaque)instruction),
    };

    private IrOpaque Rewrite(IrOpaque opaque) => opaque with { Target = opaque.Target is null ? null : Resolve(opaque.Target) };

    private IrTerminator Rewrite(IrTerminator terminator) => terminator switch
    {
        IrBranch branch => branch with { Cond = Resolve(branch.Cond) },
        IrSwitch choice => choice with { Scrutinee = Resolve(choice.Scrutinee) },
        IrReturn exit => exit with { Value = exit.Value is null ? null : Resolve(exit.Value), Outs = [.. exit.Outs.Select(o => o with { Final = Resolve(o.Final) })] },
        IrThrow exit => exit with { Outs = [.. exit.Outs.Select(o => o with { Final = Resolve(o.Final) })] },
        _ => terminator,
    };

    /// <summary>
    /// A mutable source variable (local, parameter or flow capture). Identity is by reference;
    /// <see cref="Template"/> supplies the name, source name and type of its SSA versions.
    /// </summary>
    internal sealed class Variable(IrVar template)
    {
        public IrVar Template { get; } = template;
    }

    private interface IStep;

    private sealed record Instruction(IrInstruction Value) : IStep;

    private sealed record LoadStep(Variable Variable, IrVar Temp) : IStep;

    private sealed record StoreStep(Variable Variable, IrVar Value) : IStep;

    private sealed class Draft(IrBlockId id)
    {
        public IrBlockId Id { get; } = id;

        public List<IStep> Steps { get; } = [];

        public IrTerminator? Terminator { get; set; }
    }

    private sealed class Phi(IrVar target, IrBlockId block)
    {
        public IrVar Target { get; } = target;

        public IrBlockId Block { get; } = block;

        public List<(IrBlockId From, IrVar Value)> Operands { get; } = [];

        public bool Removed { get; set; }
    }
}
