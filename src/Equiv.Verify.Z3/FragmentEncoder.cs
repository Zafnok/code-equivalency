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
/// P1-005). <see cref="ProductEncoder"/> asserts two of these, one per side, into one query.
/// <para>
/// Rung 4 of the loop ladder (ticket P1-001) conjoins them into constrained Horn clauses instead, reading the fragment's
/// interface from <see cref="Terms"/> and <see cref="Exits"/>. Its fragments make no call and apply no pure function, so it
/// gives no call encoders, and then there are no positions, no heap threading and no trace. When the sorts are
/// integers (<see cref="SortMapper.Integers"/>), an operation <see cref="IntModeTranslator"/> models exactly is defined
/// as such and its overflow condition, where the block holding it is reached, joins <see cref="Overflow"/>; any other
/// result, and every bitvector read from a map, is only assumed within its bounds.
/// </para>
/// </summary>
internal sealed class FragmentEncoder
{
    private readonly Side side;
    private readonly SortMapper sorts;
    private readonly TraceEncoder? calls;
    private readonly PureEncoder? pures;
    private readonly IntModeTranslator? integers;
    private readonly Context context;
    private readonly IReadOnlyDictionary<string, Expr> inputs;
    private readonly Dictionary<string, Expr> constants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Expr> literals = new(StringComparer.Ordinal);
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
    private readonly List<BoolExpr> overflows = [];

    /// <param name="side">Names the constants <c>old.</c> or <c>new.</c> and picks the side's runtime-sensitive functions.</param>
    /// <param name="procedure">An acyclic procedure.</param>
    /// <param name="sorts">The sorts and literals of the context the terms live in.</param>
    /// <param name="functions">The call and pure-function encoders both sides share, or null for a fragment without either.</param>
    /// <param name="inputs">The term each parameter of <paramref name="procedure"/> is bound to, by name.</param>
    /// <param name="heapInputs">The shared input of each map <see cref="TraceEncoder.Heap"/> ranges over, in its order.</param>
    /// <param name="exceptionTypes">Exception type names to the ids both sides use for them; new names are added.</param>
    /// <exception cref="InvalidOperationException">The fragment calls or applies a pure function and <paramref name="functions"/> is null.</exception>
    public FragmentEncoder(
        Side side,
        IrProcedure procedure,
        SortMapper sorts,
        (TraceEncoder Calls, PureEncoder Pures)? functions,
        IReadOnlyDictionary<string, Expr> inputs,
        ImmutableArray<Expr> heapInputs,
        Dictionary<string, int> exceptionTypes)
    {
        this.side = side;
        this.sorts = sorts;
        calls = functions?.Calls;
        pures = functions?.Pures;
        integers = sorts.Integers;
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
        Trace = calls?.Trace(events);
        BoolExpr[] opaqueDisjuncts = [context.MkFalse(), .. opaques.Select(static o => o.Reach).Distinct()];
        Opaque = context.MkOr(opaqueDisjuncts);
        BoolExpr[] unreachableDisjuncts = [context.MkFalse(), .. unreachable];
        BoolExpr[] overflowDisjuncts = [context.MkFalse(), .. overflows];
        Overflow = context.MkOr(overflowDisjuncts);
        Terms = new SideTerms(
            inputs.Concat(constants).ToDictionary(static t => t.Key, static t => t.Value, StringComparer.Ordinal),
            reach,
            context.MkOr(unreachableDisjuncts));
    }

    public IrProcedure Procedure { get; }

    public IReadOnlyList<BoolExpr> Assertions => assertions;

    public IReadOnlyList<(Side Side, IrOpaque Node, BoolExpr Reach)> Opaques => opaques;

    /// <summary>Every block that returns or throws, with its terminator, in reverse postorder.</summary>
    public IReadOnlyList<(IrBlockId Block, IrTerminator Exit)> Exits => exits;

    public BoolExpr Returned { get; }

    public BoolExpr Threw { get; }

    public IntExpr ExceptionType { get; }

    /// <summary>The call trace, or null for a fragment encoded without call encoders.</summary>
    public SeqExpr? Trace { get; }

    public BoolExpr Opaque { get; }

    /// <summary>Some exactly modelled integer operation in a reached block overflows; false unless the sorts are integers.</summary>
    public BoolExpr Overflow { get; }

    public SideTerms Terms { get; }

    /// <summary>The term of <paramref name="var"/>: its input, or its constant (created on first use).</summary>
    public Expr Term(IrVar var) => Var(var);

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
        int threaded = Heap.IndexOf(new TraceEncoder.HeapMap(shared.Name, shared.Type));
        return exits
            .AsEnumerable()
            .Reverse()
            .Aggregate(input, (rest, e) =>
            {
                IrOut? @out = Outs(e.Exit).FirstOrDefault(o => string.Equals(o.Param.Name, parameter?.Var.Name, StringComparison.Ordinal));
                return @out is not null ? context.MkITE(reach[e.Block], Var(@out.Final), rest) : Threaded(e.Block, threaded, rest);
            });
    }

    /// <summary>
    /// At exit <paramref name="block"/>, the version of heap map <paramref name="index"/> this side threads through its
    /// calls, else <paramref name="rest"/>; a map it does not thread (<paramref name="index"/> below 0) is always
    /// <paramref name="rest"/>.
    /// </summary>
    private Expr Threaded(IrBlockId block, int index, Expr rest) => index < 0 ? rest : context.MkITE(reach[block], heapOut[block][index], rest);

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

    /// <summary>The maps the heap at a call ranges over; none without call encoders.</summary>
    private ImmutableArray<TraceEncoder.HeapMap> Heap => calls?.Heap ?? [];

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

    /// <summary>An operand as <see cref="IntModeTranslator"/> needs it: the literal a constant holds, so a product by it is linear.</summary>
    private IntExpr Operand(IrVar var) => (IntExpr)literals.GetValueOrDefault(var.Name, Var(var));

    private void Assert(BoolExpr assertion) => assertions.Add(assertion);

    private void EncodeBlock(IrBlock block, bool entry)
    {
        BoolExpr reached = context.MkBoolConst(Name("reach." + Label(block.Id)));
        BitVecExpr? count = calls is null ? null : context.MkBVConst(Name("cnt." + Label(block.Id)), 32);
        reach.Add(block.Id, reached);
        List<(IrBlockId From, BoolExpr Taken)> predecessors = incoming.GetValueOrDefault(block.Id, []);
        Expr[] heap = [.. Heap.Select((m, i) => context.MkConst(Name($"heap.{i.ToString(CultureInfo.InvariantCulture)}.{Label(block.Id)}"), sorts.Sort(m.Type)))];
        if (entry)
        {
            Assert(reached);
            AssertAll(count is null ? [] : [context.MkEq(count, context.MkBV(0, 32))]);
            AssertAll(heap.Select((h, i) => context.MkEq(h, heapInputs[i])));
        }
        else
        {
            Assert(context.MkEq(reached, context.MkOr(predecessors.Select(static p => p.Taken))));
            AssertAll(count is null ? [] : [context.MkEq(count, Merge(predecessors, p => countOut[p.From]))]);
            AssertAll(heap.Select((h, i) => context.MkEq(h, Merge(predecessors, p => heapOut[p.From][i]))));
        }

        List<Expr> blockEvents = [];
        foreach (IrInstruction instruction in block.Instructions)
        {
            EncodeInstruction(instruction, predecessors, reached, count, blockEvents, heap);
        }

        events.Add((reached, blockEvents));
        if (count is not null)
        {
            countOut.Add(block.Id, context.MkBVAdd(count, context.MkBV(blockEvents.Count, 32)));
        }

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
    /// Encodes one instruction of a block whose calls so far are <paramref name="blockEvents"/>, whose first call has
    /// position <paramref name="count"/>, and which <paramref name="reached"/> says is reached. <paramref name="heap"/> is
    /// the version of each heap map a call reads when it does not pair it; a call replaces every entry with its new version
    /// (ticket P1-005).
    /// </summary>
    private void EncodeInstruction(IrInstruction instruction, List<(IrBlockId From, BoolExpr Taken)> predecessors, BoolExpr reached, BitVecExpr? count, List<Expr> blockEvents, Expr[] heap)
    {
        switch (instruction)
        {
            case IrConst constant:
                EncodeConstant(constant);
                break;
            case IrBinary binary:
                EncodeBinary(binary, reached);
                break;
            case IrOverflows check:
                EncodeOverflows(check);
                break;
            case IrUnary unary:
                EncodeUnary(unary, reached);
                break;
            case IrPhi phi:
                Define(phi.Target, Merge([.. predecessors.Where(p => phi.Incoming.Any(i => i.From == p.From))], p => Var(phi.Incoming.First(i => i.From == p.From).Value)));
                break;
            case IrCall call:
                blockEvents.Add(EncodeCall(call, Functions(call.Callee.Value).Calls, context.MkBVAdd(count!, context.MkBV(blockEvents.Count, 32)), heap));
                break;
            case IrPure pure:
                {
                    (Expr result, ImmutableArray<BoolExpr> threw) = Functions(pure.Function).Pures.Apply(side, pure, [.. pure.Args.Select(Var)]);
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
                Bounded(read.Target);
                break;
            case IrMapWrite write:
                Define(write.Target, context.MkStore((ArrayExpr)Var(write.Map), Var(write.Key), Var(write.Value)));
                break;
            default:
                opaques.Add((side, (IrOpaque)instruction, reached));
                break;
        }
    }

    /// <summary>A constant, remembered so that an integer-mode product by it stays linear.</summary>
    private void EncodeConstant(IrConst constant)
    {
        Expr literal = sorts.Literal(constant.Value);
        literals[constant.Target.Name] = literal;
        Define(constant.Target, literal);
    }

    private void EncodeBinary(IrBinary binary, BoolExpr reached)
    {
        if (integers is not null && binary.A.Type is IrBitVec { Width: var width } && binary.Op is not (IrBinaryOp.Eq or IrBinaryOp.Ne))
        {
            Integer(binary.Target, integers.Binary(binary.Op, Operand(binary.A), Operand(binary.B), width), reached);
        }
        else
        {
            Define(binary.Target, Binary(binary.Op, Var(binary.A), Var(binary.B)));
        }
    }

    /// <summary>A checked operation's overflow flag; in integer mode a product of two unknowns leaves the flag free.</summary>
    private void EncodeOverflows(IrOverflows check)
    {
        if (integers is null)
        {
            Define(check.Target, context.MkNot(ProductEncoder.NoOverflow[check.Op](context, (BitVecExpr)Var(check.A), (BitVecExpr)Var(check.B))));
        }
        else if (integers.Overflows(check.Op, Operand(check.A), Operand(check.B), ((IrBitVec)check.A.Type).Width) is { } overflowed)
        {
            Define(check.Target, overflowed);
        }
    }

    private void EncodeUnary(IrUnary unary, BoolExpr reached)
    {
        if (integers is not null && unary.Op != IrUnaryOp.BoolNot)
        {
            (Expr value, BoolExpr? overflow) = integers.Unary(unary.Op, Operand(unary.A), ((IrBitVec)unary.A.Type).Width, ((IrBitVec)unary.Target.Type).Width);
            Integer(unary.Target, (value, overflow), reached);
        }
        else
        {
            Define(unary.Target, Unary(unary));
        }
    }

    /// <summary>The call encoders, which a fragment that calls <paramref name="what"/> needs.</summary>
    private (TraceEncoder Calls, PureEncoder Pures) Functions(string what) =>
        calls is null
            ? throw new InvalidOperationException($"{Procedure.Identity.Value} reaches {what}, but its fragment is encoded without calls or pure functions.")
            : (calls, pures!);

    /// <summary>
    /// An integer-mode result: defined when <see cref="IntModeTranslator"/> models it, else only bounded; its overflow
    /// condition, if any, counts where <paramref name="reached"/> holds.
    /// </summary>
    private void Integer(IrVar target, (Expr? Value, BoolExpr? Overflow) result, BoolExpr reached)
    {
        if (result.Value is null)
        {
            Bounded(target);
        }
        else
        {
            Define(target, result.Value);
        }

        if (result.Overflow is { } overflow)
        {
            overflows.Add(context.MkAnd(reached, overflow));
        }
    }

    /// <summary>In integer mode, a bitvector <paramref name="target"/> nothing defines exactly is assumed within its bounds.</summary>
    private void Bounded(IrVar target)
    {
        if (integers is not null && target.Type is IrBitVec { Width: var width })
        {
            Assert(integers.InRange(Var(target), width));
        }
    }

    /// <summary>
    /// Defines a call's result, <c>threw</c> flag and the <c>after</c> of each heap pair, replaces every entry of
    /// <paramref name="heap"/> with the call's new version of that map (ticket P1-005), and returns its trace event.
    /// </summary>
    private Expr EncodeCall(IrCall call, TraceEncoder trace, BitVecExpr position, Expr[] heap)
    {
        IrHeapPair?[] pairs = [.. trace.Heap.Select(m => call.Heap.FirstOrDefault(h => string.Equals(h.Map, m.Name, StringComparison.Ordinal) && h.Before.Type == m.Type))];
        ImmutableArray<Expr> read = [.. pairs.Select((p, i) => p is null ? heap[i] : Var(p.Before))];
        (Expr? result, BoolExpr threw, Expr @event, ImmutableArray<Expr> written) = trace.Call(side, call, [.. call.Args.Select(a => (a.Type, Var(a)))], position, read);
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
            Assert(integers is null
                ? context.MkBVSGE((BitVecExpr)Var(read.Target), context.MkBV(0, 32))
                : context.MkGe((IntExpr)Var(read.Target), context.MkInt(0)));
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
