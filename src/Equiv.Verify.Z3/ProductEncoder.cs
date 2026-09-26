using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Ir;

using Microsoft.Z3;

namespace Equiv.Verify.Z3;

/// <summary>
/// The product program of an acyclic pair (VERIFICATION-MODEL.md sections 1, 2 and 5; ADR 0014; ADR 0018;
/// ADR 0021; ticket M3-001). Both sides are encoded over one set of inputs, the <see cref="SharedParameter"/>s
/// of <see cref="Pair"/>. Every other SSA variable is a constant <c>old.&lt;name&gt;</c> or
/// <c>new.&lt;name&gt;</c> fixed by a definitional equality; SSA makes that sound even for blocks no input
/// reaches. Control flow is a Bool <c>reach</c> per block, and the call position a bv32 <c>cnt</c> per block. Calls go through
/// <see cref="TraceEncoder"/>, pure functions through <see cref="PureEncoder"/>. When a heap pair names a map, the version of
/// every such map a call reads when it does not pair it is a term per block too, <c>heap.&lt;i&gt;</c>: the shared input,
/// replaced by each call's new version (ticket P1-005).
/// The ladder of ticket M3-002 encodes unrolled procedures and loop fragments with it; each is acyclic.
/// </summary>
internal static class ProductEncoder
{
    private static readonly FrozenDictionary<IrBinaryOp, Func<Context, BitVecExpr, BitVecExpr, Expr>> BitVecOps =
        new Dictionary<IrBinaryOp, Func<Context, BitVecExpr, BitVecExpr, Expr>>
        {
            [IrBinaryOp.Add] = static (c, a, b) => c.MkBVAdd(a, b),
            [IrBinaryOp.Sub] = static (c, a, b) => c.MkBVSub(a, b),
            [IrBinaryOp.Mul] = static (c, a, b) => c.MkBVMul(a, b),
            [IrBinaryOp.SDiv] = static (c, a, b) => c.MkBVSDiv(a, b),
            [IrBinaryOp.SRem] = static (c, a, b) => c.MkBVSRem(a, b),
            [IrBinaryOp.UDiv] = static (c, a, b) => c.MkBVUDiv(a, b),
            [IrBinaryOp.URem] = static (c, a, b) => c.MkBVURem(a, b),
            [IrBinaryOp.And] = static (c, a, b) => c.MkBVAND(a, b),
            [IrBinaryOp.Or] = static (c, a, b) => c.MkBVOR(a, b),
            [IrBinaryOp.Xor] = static (c, a, b) => c.MkBVXOR(a, b),
            [IrBinaryOp.Shl] = static (c, a, b) => c.MkBVSHL(a, b),
            [IrBinaryOp.AShr] = static (c, a, b) => c.MkBVASHR(a, b),
            [IrBinaryOp.LShr] = static (c, a, b) => c.MkBVLSHR(a, b),
            [IrBinaryOp.Slt] = static (c, a, b) => c.MkBVSLT(a, b),
            [IrBinaryOp.Sle] = static (c, a, b) => c.MkBVSLE(a, b),
            [IrBinaryOp.Sgt] = static (c, a, b) => c.MkBVSGT(a, b),
            [IrBinaryOp.Sge] = static (c, a, b) => c.MkBVSGE(a, b),
            [IrBinaryOp.Ult] = static (c, a, b) => c.MkBVULT(a, b),
            [IrBinaryOp.Ule] = static (c, a, b) => c.MkBVULE(a, b),
            [IrBinaryOp.Ugt] = static (c, a, b) => c.MkBVUGT(a, b),
            [IrBinaryOp.Uge] = static (c, a, b) => c.MkBVUGE(a, b),
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<IrBinaryOp, Func<Context, BoolExpr, BoolExpr, Expr>> BoolOps =
        new Dictionary<IrBinaryOp, Func<Context, BoolExpr, BoolExpr, Expr>>
        {
            [IrBinaryOp.And] = static (c, a, b) => c.MkAnd(a, b),
            [IrBinaryOp.Or] = static (c, a, b) => c.MkOr(a, b),
            [IrBinaryOp.Xor] = static (c, a, b) => c.MkXor(a, b),
        }.ToFrozenDictionary();

    /// <summary>"Does not overflow" per checked operation; <see cref="IrOverflows"/> is its negation.</summary>
    private static readonly FrozenDictionary<IrOverflowOp, Func<Context, BitVecExpr, BitVecExpr, BoolExpr>> NoOverflow =
        new Dictionary<IrOverflowOp, Func<Context, BitVecExpr, BitVecExpr, BoolExpr>>
        {
            [IrOverflowOp.SAdd] = static (c, a, b) => c.MkAnd(c.MkBVAddNoOverflow(a, b, isSigned: true), c.MkBVAddNoUnderflow(a, b)),
            [IrOverflowOp.UAdd] = static (c, a, b) => c.MkBVAddNoOverflow(a, b, isSigned: false),
            [IrOverflowOp.SSub] = static (c, a, b) => c.MkAnd(c.MkBVSubNoOverflow(a, b), c.MkBVSubNoUnderflow(a, b, isSigned: true)),
            [IrOverflowOp.USub] = static (c, a, b) => c.MkBVSubNoUnderflow(a, b, isSigned: false),
            [IrOverflowOp.SMul] = static (c, a, b) => c.MkAnd(c.MkBVMulNoOverflow(a, b, isSigned: true), c.MkBVMulNoUnderflow(a, b)),
            [IrOverflowOp.UMul] = static (c, a, b) => c.MkBVMulNoOverflow(a, b, isSigned: false),
            [IrOverflowOp.SDiv] = static (c, a, b) => c.MkBVSDivNoOverflow(a, b),
        }.ToFrozenDictionary();

    /// <summary>The name prefix of the synthesised input that maps an array reference to its length (VERIFICATION-MODEL.md section 2).</summary>
    public const string LengthPrefix = "length.";

    public static string Prefix(Side side) => side == Side.Old ? "old" : "new";

    /// <summary>
    /// Pairs the parameters of both sides into shared inputs (ADR 0021). A caller binds the source-language
    /// parameters by position, so those pair by position, whatever their names; the synthesised inputs
    /// (<see cref="IrParameterNames.IsSynthesised"/>) pair by name. Two parameters pair only when their types are equal:
    /// any other parameter is an input of its own side alone, which can cost precision (a false Divergent)
    /// but never shares a value that a caller does not. The old side's parameters come first, in order.
    /// </summary>
    public static ImmutableArray<SharedParameter> Pair(IrProcedure old, IrProcedure @new)
    {
        IrParameter[] positional = [.. @new.Parameters.Where(static p => !IrParameterNames.IsSynthesised(p.Var.Name))];
        Dictionary<string, IrParameter> synthesised = @new.Parameters
            .Where(static p => IrParameterNames.IsSynthesised(p.Var.Name))
            .ToDictionary(static p => p.Var.Name, StringComparer.Ordinal);
        HashSet<string> paired = new(StringComparer.Ordinal);
        List<SharedParameter> shared = [];
        int position = 0;
        foreach (IrParameter parameter in old.Parameters)
        {
            IrParameter? counterpart = IrParameterNames.IsSynthesised(parameter.Var.Name)
                ? synthesised.GetValueOrDefault(parameter.Var.Name)
                : positional.ElementAtOrDefault(position++);
            if (counterpart is not null && counterpart.Var.Type == parameter.Var.Type)
            {
                paired.Add(counterpart.Var.Name);
                shared.Add(new SharedParameter(parameter, counterpart));
            }
            else
            {
                shared.Add(new SharedParameter(parameter, New: null));
            }
        }

        shared.AddRange(@new.Parameters.Where(p => !paired.Contains(p.Var.Name)).Select(static p => new SharedParameter(Old: null, p)));
        return [.. shared];
    }

    public static ProductEncoding Encode(Context context, IrProcedure old, IrProcedure @new, ImmutableDictionary<string, string> callIdentityMap)
    {
        SortMapper sorts = new(context);
        ImmutableArray<(SharedParameter Shared, Expr Term)> inputs =
        [
            .. Pair(old, @new).Select(s => (s, context.MkConst(s.InputName, sorts.Sort(s.Type)))),
        ];

        IrCall[] allCalls = [.. old.Blocks.Concat(@new.Blocks).SelectMany(static b => b.Instructions.OfType<IrCall>())];
        IEnumerable<IrType> argumentTypes = allCalls.SelectMany(static c => c.Args.Select(static a => a.Type));
        TraceEncoder calls = new(sorts, argumentTypes, callIdentityMap, HeapMaps(allCalls));
        ImmutableArray<Expr> heapInputs = [.. calls.Heap.Select(m => inputs.First(i => string.Equals(i.Shared.Var.Name, m.Name, StringComparison.Ordinal) && i.Shared.Type == m.Type).Term)];
        PureEncoder pures = new(sorts, old.Blocks.Concat(@new.Blocks).SelectMany(static b => b.Instructions.OfType<IrPure>()));
        Dictionary<string, int> exceptionTypes = new(StringComparer.Ordinal);
        SideEncoder oldSide = new(Side.Old, old, sorts, (calls, pures), Bound(inputs, static s => s.Old), heapInputs, exceptionTypes);
        SideEncoder newSide = new(Side.New, @new, sorts, (calls, pures), Bound(inputs, static s => s.New), heapInputs, exceptionTypes);

        List<BoolExpr> equal =
        [
            context.MkEq(oldSide.Returned, newSide.Returned),
            ReturnsEqual(context, sorts, oldSide, newSide),
            context.MkEq(oldSide.Threw, newSide.Threw),
            context.MkEq(oldSide.ExceptionType, newSide.ExceptionType),
        ];
        equal.AddRange(inputs
            .Where(static i => i.Shared.ByRef)
            .Select(i => context.MkEq(oldSide.Final(i.Shared.Old, i.Shared.Var, i.Term), newSide.Final(i.Shared.New, i.Shared.Var, i.Term))));
        equal.Add(context.MkEq(oldSide.Trace, newSide.Trace));

        return new ProductEncoding(
            [.. oldSide.Assertions, .. newSide.Assertions, .. sorts.Distinctness()],
            context.MkNot(context.MkAnd(equal)),
            oldSide.Opaque,
            newSide.Opaque,
            inputs,
            [.. oldSide.Opaques, .. newSide.Opaques],
            sorts,
            calls,
            pures,
            oldSide.Terms,
            newSide.Terms);
    }

    /// <summary>
    /// The maps the heap at a call ranges over (ticket P1-005): each map a heap pair names on either side, with its type,
    /// ordered by name and then type. Each is a by-ref parameter of the side that pairs it, so it has a shared input.
    /// </summary>
    public static ImmutableArray<TraceEncoder.HeapMap> HeapMaps(IEnumerable<IrCall> calls) =>
    [
        .. calls
            .SelectMany(static c => c.Heap)
            .Select(static h => new TraceEncoder.HeapMap(h.Map, h.Before.Type))
            .Distinct()
            .OrderBy(static m => m.Name, StringComparer.Ordinal)
            .ThenBy(static m => SortMapper.Name(m.Type), StringComparer.Ordinal),
    ];

    /// <summary>One side's parameter names and the input term each is bound to.</summary>
    private static Dictionary<string, Expr> Bound(ImmutableArray<(SharedParameter Shared, Expr Term)> inputs, Func<SharedParameter, IrParameter?> side) =>
        inputs
            .Where(i => side(i.Shared) is not null)
            .ToDictionary(i => side(i.Shared)!.Var.Name, static i => i.Term, StringComparer.Ordinal);

    /// <summary>
    /// Return values agree. A return type that differs between the sides (the matcher's identity does not
    /// include it) can only agree on inputs where neither side returns.
    /// </summary>
    private static BoolExpr ReturnsEqual(Context context, SortMapper sorts, SideEncoder old, SideEncoder @new)
    {
        IrType? oldType = old.Procedure.ReturnType;
        IrType? newType = @new.Procedure.ReturnType;
        if (oldType != newType)
        {
            return context.MkNot(context.MkOr(old.Returned, @new.Returned));
        }

        if (oldType is null)
        {
            return context.MkTrue();
        }

        Expr none = context.MkConst("ret.none", sorts.Sort(oldType));
        return context.MkEq(old.ReturnValue(none), @new.ReturnValue(none));
    }

    /// <summary>Encodes one side's blocks into assertions and exposes its observables.</summary>
    private sealed class SideEncoder
    {
        private readonly Side side;
        private readonly SortMapper sorts;
        private readonly TraceEncoder calls;
        private readonly PureEncoder pures;
        private readonly Context context;
        private readonly Dictionary<string, Expr> inputs;
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

        public SideEncoder(
            Side side,
            IrProcedure procedure,
            SortMapper sorts,
            (TraceEncoder Calls, PureEncoder Pures) functions,
            Dictionary<string, Expr> inputs,
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

        private BoolExpr Any(IEnumerable<(IrBlockId Block, IrTerminator Exit)> blocks)
        {
            BoolExpr[] disjuncts = [context.MkFalse(), .. blocks.Select(e => reach[e.Block])];
            return context.MkOr(disjuncts);
        }

        private string Name(string suffix) => Prefix(side) + "." + suffix;

        private static string Label(IrBlockId block) => "B" + block.Value.ToString(CultureInfo.InvariantCulture);

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
        /// it; a call replaces every entry with its new version of that map (ticket P1-005).
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
                    Define(overflows.Target, context.MkNot(NoOverflow[overflows.Op](context, (BitVecExpr)Var(overflows.A), (BitVecExpr)Var(overflows.B))));
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
        /// Defines a call's result, <c>threw</c> flag, ref outputs (ticket M4-003) and the <c>after</c> of each heap pair, replaces every entry of
        /// <paramref name="heap"/> with the call's new version of that map (ticket P1-005), and returns its trace event.
        /// </summary>
        private Expr EncodeCall(IrCall call, BitVecExpr position, Expr[] heap)
        {
            IrHeapPair?[] pairs = [.. calls.Heap.Select(m => call.Heap.FirstOrDefault(h => string.Equals(h.Map, m.Name, StringComparison.Ordinal) && h.Before.Type == m.Type))];
            ImmutableArray<Expr> read = [.. pairs.Select((p, i) => p is null ? heap[i] : Var(p.Before))];
            (Expr? result, BoolExpr threw, Expr @event, ImmutableArray<Expr> written, ImmutableArray<Expr> refOuts) =
                calls.Call(side, call, [.. call.Args.Select(a => (a.Type, Var(a)))], position, read);
            if (call.Target is not null)
            {
                Define(call.Target, result!);
            }

            for (int i = 0; i < refOuts.Length; i++)
            {
                Define(call.RefOuts[i], refOuts[i]);
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
            if (read.Map.Name.StartsWith(LengthPrefix, StringComparison.Ordinal))
            {
                Assert(context.MkBVSGE((BitVecExpr)Var(read.Target), context.MkBV(0, 32)));
            }
        }

        private Expr Binary(IrBinaryOp op, Expr a, Expr b) => op switch
        {
            IrBinaryOp.Eq => context.MkEq(a, b),
            IrBinaryOp.Ne => context.MkNot(context.MkEq(a, b)),
            _ when a is BoolExpr left => BoolOps[op](context, left, (BoolExpr)b),
            _ => BitVecOps[op](context, (BitVecExpr)a, (BitVecExpr)b),
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

    /// <summary>Which procedure of a pair a term belongs to.</summary>
    public enum Side
    {
        Old,
        New,
    }

    /// <summary>
    /// One input of the product (ADR 0021): the parameter each side binds to it, or <c>null</c> on a side
    /// without one. Its Z3 constant is named <see cref="InputName"/>: <c>in.&lt;old name&gt;</c>, or
    /// <c>in.new.&lt;new name&gt;</c> for an input only the new side has, so no two inputs share a name.
    /// </summary>
    public sealed record SharedParameter(IrParameter? Old, IrParameter? New)
    {
        public IrVar Var => (Old ?? New)!.Var;

        public IrType Type => Var.Type;

        public string InputName => (Old is null ? "in.new." : "in.") + Var.Name;

        /// <summary>Whether its final value is an observable: it is <c>ref</c> or <c>out</c> on either side.</summary>
        public bool ByRef => new[] { Old, New }.Any(static p => p is not null && p.Kind != IrParameterKind.In);
    }

    /// <summary>
    /// One side's terms, for queries beyond the product's own (ticket M3-002): every variable's Z3 term by name,
    /// every block's <c>reach</c>, and whether the side reaches an <see cref="IrUnreachable"/>. An unreachable block
    /// is an assumption, not an assertion, so that rung 1 can ask whether any input reaches its bound.
    /// </summary>
    public sealed record SideTerms(IReadOnlyDictionary<string, Expr> Vars, IReadOnlyDictionary<IrBlockId, BoolExpr> Reach, BoolExpr Unreachable);

    /// <summary>
    /// The product query for one pair: <see cref="Assertions"/> hold on every input, <see cref="Differs"/>
    /// says some observable differs, and <see cref="OpaqueOld"/>/<see cref="OpaqueNew"/> say that side
    /// reaches an <see cref="IrOpaque"/> (ADR 0014). <see cref="Inputs"/> lists the shared inputs in
    /// <see cref="Pair"/> order with their Z3 constants. A query assumes <c>not Old.Unreachable</c> and
    /// <c>not New.Unreachable</c> unless it asks about them.
    /// </summary>
    public sealed record ProductEncoding(
        ImmutableArray<BoolExpr> Assertions,
        BoolExpr Differs,
        BoolExpr OpaqueOld,
        BoolExpr OpaqueNew,
        ImmutableArray<(SharedParameter Shared, Expr Term)> Inputs,
        ImmutableArray<(Side Side, IrOpaque Node, BoolExpr Reach)> Opaques,
        SortMapper Sorts,
        TraceEncoder Calls,
        PureEncoder Pures,
        SideTerms Old,
        SideTerms New);
}
