using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Ir;

using Microsoft.Z3;

using Side = Equiv.Verify.Z3.ProductEncoder.Side;
using SideTerms = Equiv.Verify.Z3.ProductEncoder.SideTerms;

namespace Equiv.Verify.Z3;

/// <summary>
/// One side of an acyclic procedure (a whole procedure, an unrolled one, or a loop segment) encoded as definitional
/// equalities over the input terms it is given (VERIFICATION-MODEL.md sections 2 and 5; tickets M3-001 and P1-001).
/// Every SSA variable that is not an input is a constant <c>old.&lt;name&gt;</c> or <c>new.&lt;name&gt;</c> fixed by an
/// equality; SSA makes that sound even for blocks no input reaches. Control flow is a Bool <c>reach</c> per block, and
/// the call position a bv32 <c>cnt</c> per block. Calls go through <see cref="TraceEncoder"/>, pure functions through
/// <see cref="PureEncoder"/>. When a heap pair names a map, the version of every such map a call reads when it does not
/// pair it is a term per block too, <c>heap.&lt;i&gt;</c>: the shared input, replaced by each call's new version (ticket
/// P1-005). <see cref="ProductEncoder"/> asserts two of these, one per side, into one query; the fragment's
/// <see cref="Assertions"/> and its interface (<see cref="Terms"/>, <see cref="Exits"/>) are also what a constrained Horn
/// clause needs (ticket P1-001).
/// </summary>
internal sealed class FragmentEncoder
{
    private readonly Side side;
    private readonly SortMapper sorts;
    private readonly TraceEncoder calls;
    private readonly PureEncoder pures;
    private readonly Context context;
    private readonly IReadOnlyDictionary<string, Expr> inputs;
    private readonly Dictionary<string, Expr> constants = new(StringComparer.Ordinal);
    private readonly Dictionary<IrBlockId, BoolExpr> reach = [];
    private readonly Dictionary<IrBlockId, BitVecExpr> countOut = [];
    private readonly ImmutableArray<Expr> heapInputs;
    private readonly Dictionary<IrBlockId, Expr[]> heapOut = [];
    private readonly Dictionary<IrBlockId, List<(IrBlockId From, BoolExpr Taken)>> incoming = [];
    private readonly List<(IrBlockId Block, IrTerminator Exit)> exits = [];
    private readonly List<(BoolExpr Reach, IReadOnlyList<Expr> Events)> events = [];
    private readonly List<BoolExpr> assertions = [];
    private readonly List<(Side Side, IrOpaque Node, BoolExpr Reach)> opaques = [];
    private readonly List<BoolExpr> unreachable = [];

    /// <param name="side">Names the constants <c>old.</c> or <c>new.</c> and picks the side's runtime-sensitive functions.</param>
    /// <param name="procedure">An acyclic procedure.</param>
    /// <param name="sorts">The sorts and literals of the context the terms live in.</param>
    /// <param name="functions">The call and pure-function encoders both sides share.</param>
    /// <param name="inputs">The term each parameter of <paramref name="procedure"/> is bound to, by name.</param>
    /// <param name="heapInputs">The shared input of each map <see cref="TraceEncoder.Heap"/> ranges over, in its order.</param>
    /// <param name="exceptionTypes">Exception type names to the ids both sides use for them; new names are added.</param>
    public FragmentEncoder(
        Side side,
        IrProcedure procedure,
        SortMapper sorts,
        (TraceEncoder Calls, PureEncoder Pures) functions,
        IReadOnlyDictionary<string, Expr> inputs,
        ImmutableArray<Expr> heapInputs,
        Dictionary<string, int> exceptionTypes)
    {
        this.side = side;
        this.sorts = sorts;
        (calls, pures) = functions;
        this.inputs = inputs;
        this.heapInputs = heapInputs;
        Procedure = procedure;
        context = sorts.Context;

        foreach (IrBlock block in IrLoopAnalysis.Of(procedure).ReversePostorder)
        {
            EncodeBlock(block, block.Id == procedure.Entry);
        }

        Returned = Any(exits.Where(static e => e.Exit is IrReturn));
        Threw = Any(exits.Where(static e => e.Exit is IrThrow));
        ExceptionType = exits
            .Where(static e => e.Exit is IrThrow)
            .Reverse()
            .Aggregate((IntExpr)context.MkInt(0), (rest, e) => (IntExpr)context.MkITE(reach[e.Block], context.MkInt(Intern(exceptionTypes, ((IrThrow)e.Exit).ExceptionType)), rest));
        Trace = calls.Trace(events);
        BoolExpr[] opaqueDisjuncts = [context.MkFalse(), .. opaques.Select(static o => o.Reach).Distinct()];
        Opaque = context.MkOr(opaqueDisjuncts);
        BoolExpr[] unreachableDisjuncts = [context.MkFalse(), .. unreachable];
        Terms = new SideTerms(
            inputs.Concat(constants).ToDictionary(static t => t.Key, static t => t.Value, StringComparer.Ordinal),
            reach,
            context.MkOr(unreachableDisjuncts));
    }

    public IrProcedure Procedure { get; }

    public IReadOnlyList<BoolExpr> Assertions => assertions;

    public IReadOnlyList<(Side Side, IrOpaque Node, BoolExpr Reach)> Opaques => opaques;

    public BoolExpr Returned { get; }

    public BoolExpr Threw { get; }

    public IntExpr ExceptionType { get; }

    public SeqExpr Trace { get; }

    public BoolExpr Opaque { get; }

    public SideTerms Terms { get; }

    /// <summary>The value returned, or <paramref name="none"/> (shared by both sides) when no return is reached.</summary>
    public Expr ReturnValue(Expr none) =>
        exits
            .Where(static e => e.Exit is IrReturn)
            .Reverse()
            .Aggregate(none, (rest, e) => context.MkITE(reach[e.Block], Var(((IrReturn)e.Exit).Value!), rest));

    /// <summary>
    /// The final value of <paramref name="parameter"/>, the shared input <paramref name="shared"/>: its version at the exit
    /// taken, else, on a side with no by-ref parameter for it, the version threaded through this side's calls when a
    /// heap pair names it (ticket P1-005), else <paramref name="input"/>.
    /// </summary>
    public Expr Final(IrParameter? parameter, IrVar shared, Expr input)
    {
        int threaded = calls.Heap.IndexOf(new TraceEncoder.HeapMap(shared.Name, shared.Type));
        return exits
            .AsEnumerable()
            .Reverse()
            .Aggregate(input, (rest, e) =>
            {
                IrOut? @out = Outs(e.Exit).FirstOrDefault(o => string.Equals(o.Param.Name, parameter?.Var.Name, StringComparison.Ordinal));
                Expr? value = @out is not null ? Var(@out.Final) : threaded < 0 ? null : heapOut[e.Block][threaded];
                return value is null ? rest : context.MkITE(reach[e.Block], value, rest);
            });
    }

    private static ImmutableArray<IrOut> Outs(IrTerminator exit) => exit is IrReturn ret ? ret.Outs : ((IrThrow)exit).Outs;

    private static int Intern(Dictionary<string, int> table, string name)
    {
        if (!table.TryGetValue(name, out int id))
        {
            id = table.Count + 1;
            table.Add(name, id);
        }

        return id;
    }

    private static string Label(IrBlockId block) => "B" + block.Value.ToString(CultureInfo.InvariantCulture);

    private BoolExpr Any(IEnumerable<(IrBlockId Block, IrTerminator Exit)> blocks)
    {
        BoolExpr[] disjuncts = [context.MkFalse(), .. blocks.Select(e => reach[e.Block])];
        return context.MkOr(disjuncts);
    }

    private string Name(string suffix) => ProductEncoder.Prefix(side) + "." + suffix;

    private Expr Var(IrVar var)
    {
        if (inputs.TryGetValue(var.Name, out Expr? input))
        {
            return input;
        }

        if (!constants.TryGetValue(var.Name, out Expr? constant))
        {
            constant = context.MkConst(Name(var.Name), sorts.Sort(var.Type));
            constants.Add(var.Name, constant);
        }

        return constant;
    }

    private void Assert(BoolExpr assertion) => assertions.Add(assertion);

    private void EncodeBlock(IrBlock block, bool entry)
    {
        BoolExpr reached = context.MkBoolConst(Name("reach." + Label(block.Id)));
        BitVecExpr count = context.MkBVConst(Name("cnt." + Label(block.Id)), 32);
        reach.Add(block.Id, reached);
        List<(IrBlockId From, BoolExpr Taken)> predecessors = incoming.GetValueOrDefault(block.Id, []);
        Expr[] heap = [.. calls.Heap.Select((m, i) => context.MkConst(Name($"heap.{i.ToString(CultureInfo.InvariantCulture)}.{Label(block.Id)}"), sorts.Sort(m.Type)))];
        if (entry)
        {
            Assert(reached);
            Assert(context.MkEq(count, context.MkBV(0, 32)));
            AssertAll(heap.Select((h, i) => context.MkEq(h, heapInputs[i])));
        }
        else
        {
            Assert(context.MkEq(reached, context.MkOr(predecessors.Select(static p => p.Taken))));
            Assert(context.MkEq(count, Merge(predecessors, p => countOut[p.From])));
            AssertAll(heap.Select((h, i) => context.MkEq(h, Merge(predecessors, p => heapOut[p.From][i]))));
        }

        List<Expr> blockEvents = [];
        foreach (IrInstruction instruction in block.Instructions)
        {
            EncodeInstruction(instruction, predecessors, reached, context.MkBVAdd(count, context.MkBV(blockEvents.Count, 32)), blockEvents, heap);
        }

        events.Add((reached, blockEvents));
        countOut.Add(block.Id, context.MkBVAdd(count, context.MkBV(blockEvents.Count, 32)));
        heapOut.Add(block.Id, heap);
        EncodeTerminator(block, reached);
    }

    /// <summary>The value of the incoming edge taken: an <c>ite</c> over all but the last predecessor.</summary>
    private Expr Merge(List<(IrBlockId From, BoolExpr Taken)> predecessors, Func<(IrBlockId From, BoolExpr Taken), Expr> value) =>
        predecessors
            .Take(predecessors.Count - 1)
            .Reverse()
            .Aggregate(value(predecessors[^1]), (rest, p) => context.MkITE(p.Taken, value(p), rest));

    private void AssertAll(IEnumerable<BoolExpr> assertions) => assertions.ToList().ForEach(Assert);

    /// <summary>
    /// Encodes one instruction. <paramref name="heap"/> is the version of each heap map a call reads when it does not pair
    /// it; a call replaces every entry with its new version (ticket P1-005).
    /// </summary>
    private void EncodeInstruction(IrInstruction instruction, List<(IrBlockId From, BoolExpr Taken)> predecessors, BoolExpr reached, BitVecExpr position, List<Expr> blockEvents, Expr[] heap)
    {
        switch (instruction)
        {
            case IrConst constant:
                Define(constant.Target, sorts.Literal(constant.Value));
                break;
            case IrBinary binary:
                Define(binary.Target, Binary(binary.Op, Var(binary.A), Var(binary.B)));
                break;
            case IrOverflows overflows:
                Define(overflows.Target, context.MkNot(ProductEncoder.NoOverflow[overflows.Op](context, (BitVecExpr)Var(overflows.A), (BitVecExpr)Var(overflows.B))));
                break;
            case IrUnary unary:
                Define(unary.Target, Unary(unary));
                break;
            case IrPhi phi:
                Define(phi.Target, Merge([.. predecessors.Where(p => phi.Incoming.Any(i => i.From == p.From))], p => Var(phi.Incoming.First(i => i.From == p.From).Value)));
                break;
            case IrCall call:
                blockEvents.Add(EncodeCall(call, position, heap));
                break;
            case IrPure pure:
                {
                    (Expr result, ImmutableArray<BoolExpr> threw) = pures.Apply(side, pure, [.. pure.Args.Select(Var)]);
                    Define(pure.Target, result);
                    for (int i = 0; i < threw.Length; i++)
                    {
                        Define(pure.Throws[i].Flag, threw[i]);
                    }

                    break;
                }

            case IrMapRead read:
                Define(read.Target, context.MkSelect((ArrayExpr)Var(read.Map), Var(read.Key)));
                AssumeLength(read);
                break;
            case IrMapWrite write:
                Define(write.Target, context.MkStore((ArrayExpr)Var(write.Map), Var(write.Key), Var(write.Value)));
                break;
            default:
                opaques.Add((side, (IrOpaque)instruction, reached));
                break;
        }
    }

    /// <summary>
    /// Defines a call's result, <c>threw</c> flag and the <c>after</c> of each heap pair, replaces every entry of
    /// <paramref name="heap"/> with the call's new version of that map (ticket P1-005), and returns its trace event.
    /// </summary>
    private Expr EncodeCall(IrCall call, BitVecExpr position, Expr[] heap)
    {
        IrHeapPair?[] pairs = [.. calls.Heap.Select(m => call.Heap.FirstOrDefault(h => string.Equals(h.Map, m.Name, StringComparison.Ordinal) && h.Before.Type == m.Type))];
        ImmutableArray<Expr> read = [.. pairs.Select((p, i) => p is null ? heap[i] : Var(p.Before))];
        (Expr? result, BoolExpr threw, Expr @event, ImmutableArray<Expr> written) = calls.Call(side, call, [.. call.Args.Select(a => (a.Type, Var(a)))], position, read);
        if (call.Target is not null)
        {
            Define(call.Target, result!);
        }

        if (call.Threw is not null)
        {
            Define(call.Threw, threw);
        }

        for (int i = 0; i < heap.Length; i++)
        {
            heap[i] = written[i];
            if (pairs[i] is { } pair)
            {
                Define(pair.After, written[i]);
            }
        }

        return @event;
    }

    private void Define(IrVar target, Expr value) => Assert(context.MkEq(Var(target), value));

    /// <summary>
    /// A read of a <c>length.&lt;Sort&gt;</c> input is non-negative, since no CLR array has a negative length
    /// (ticket P2-019). The assumption holds whether or not the read is reached: the CLR never reads a null
    /// reference's length, so no input a caller can pass is lost.
    /// </summary>
    private void AssumeLength(IrMapRead read)
    {
        if (read.Map.Name.StartsWith(ProductEncoder.LengthPrefix, StringComparison.Ordinal))
        {
            Assert(context.MkBVSGE((BitVecExpr)Var(read.Target), context.MkBV(0, 32)));
        }
    }

    private Expr Binary(IrBinaryOp op, Expr a, Expr b) => op switch
    {
        IrBinaryOp.Eq => context.MkEq(a, b),
        IrBinaryOp.Ne => context.MkNot(context.MkEq(a, b)),
        _ when a is BoolExpr left => ProductEncoder.BoolOps[op](context, left, (BoolExpr)b),
        _ => ProductEncoder.BitVecOps[op](context, (BitVecExpr)a, (BitVecExpr)b),
    };

    private Expr Unary(IrUnary unary)
    {
        Expr a = Var(unary.A);
        if (unary.Op == IrUnaryOp.BoolNot)
        {
            return context.MkNot((BoolExpr)a);
        }

        BitVecExpr bits = (BitVecExpr)a;
        uint from = (uint)((IrBitVec)unary.A.Type).Width;
        uint to = (uint)((IrBitVec)unary.Target.Type).Width;
        return unary.Op switch
        {
            IrUnaryOp.Neg => context.MkBVNeg(bits),
            IrUnaryOp.Not => context.MkBVNot(bits),
            IrUnaryOp.ZExt => context.MkZeroExt(to - from, bits),
            IrUnaryOp.SExt => context.MkSignExt(to - from, bits),
            _ => context.MkExtract(to - 1, 0, bits),
        };
    }

    private void EncodeTerminator(IrBlock block, BoolExpr reached)
    {
        List<(IrBlockId Target, BoolExpr Condition)> edges = [];
        switch (block.Terminator)
        {
            case IrGoto jump:
                edges.Add((jump.Target, context.MkTrue()));
                break;
            case IrBranch branch:
                {
                    BoolExpr condition = (BoolExpr)Var(branch.Cond);
                    edges.Add((branch.Then, condition));
                    edges.Add((branch.Else, context.MkNot(condition)));
                    break;
                }

            case IrSwitch choice:
                {
                    Expr scrutinee = Var(choice.Scrutinee);
                    List<BoolExpr> earlier = [];
                    foreach ((IrValue value, IrBlockId target) in choice.Cases)
                    {
                        BoolExpr matches = context.MkEq(scrutinee, sorts.Literal(value));
                        BoolExpr[] caseConjuncts = [matches, .. earlier.Select(context.MkNot)];
                        edges.Add((target, context.MkAnd(caseConjuncts)));
                        earlier.Add(matches);
                    }

                    BoolExpr[] defaultConjuncts = [context.MkTrue(), .. earlier.Select(context.MkNot)];
                    edges.Add((choice.Default, context.MkAnd(defaultConjuncts)));
                    break;
                }

            case IrUnreachable:
                unreachable.Add(reached);
                break;
            default:
                exits.Add((block.Id, block.Terminator));
                break;
        }

        foreach (IGrouping<IrBlockId, (IrBlockId Target, BoolExpr Condition)> edge in edges.GroupBy(static e => e.Target))
        {
            BoolExpr condition = edge.Skip(1).Any() ? context.MkOr(edge.Select(static e => e.Condition)) : edge.First().Condition;
            if (!incoming.TryGetValue(edge.Key, out List<(IrBlockId From, BoolExpr Taken)>? list))
            {
                list = [];
                incoming.Add(edge.Key, list);
            }

            list.Add((block.Id, context.MkAnd(reached, condition)));
        }
    }
}
