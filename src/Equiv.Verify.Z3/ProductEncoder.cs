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
/// reaches. Control flow is a Bool <c>reach</c> per block, and the call position a bv32 <c>cnt</c> per block.
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

    public static string Prefix(Side side) => side == Side.Old ? "old" : "new";

    /// <summary>
    /// Reverse postorder of the blocks reachable from the entry, and whether any edge closes a cycle
    /// (a back edge). Iterative, so a long procedure cannot overflow the stack. M3-002 reuses it.
    /// </summary>
    public static (ImmutableArray<IrBlock> ReversePostorder, bool HasBackEdge) Analyze(IrProcedure procedure)
    {
        Dictionary<IrBlockId, IrBlock> blocks = procedure.Blocks.ToDictionary(static b => b.Id);
        Dictionary<IrBlockId, bool> onStack = [];
        List<IrBlock> postorder = [];
        Stack<(IrBlock Block, int Next)> stack = new();
        bool backEdge = false;
        stack.Push((blocks[procedure.Entry], 0));
        onStack[procedure.Entry] = true;
        while (stack.Count > 0)
        {
            (IrBlock block, int next) = stack.Pop();
            ImmutableArray<IrBlockId> successors = Successors(block.Terminator);
            if (next == successors.Length)
            {
                onStack[block.Id] = false;
                postorder.Add(block);
                continue;
            }

            stack.Push((block, next + 1));
            IrBlockId successor = successors[next];
            if (!onStack.TryGetValue(successor, out bool active))
            {
                onStack[successor] = true;
                stack.Push((blocks[successor], 0));
            }
            else
            {
                backEdge |= active;
            }
        }

        postorder.Reverse();
        return ([.. postorder], backEdge);
    }

    /// <summary>
    /// Pairs the parameters of both sides into shared inputs (ADR 0021). A caller binds the source-language
    /// parameters by position, so those pair by position, whatever their names; the synthesised inputs
    /// (<see cref="IsSynthesised"/>) pair by name. Two parameters pair only when their types are equal:
    /// any other parameter is an input of its own side alone, which can cost precision (a false Divergent)
    /// but never shares a value that a caller does not. The old side's parameters come first, in order.
    /// </summary>
    public static ImmutableArray<SharedParameter> Pair(IrProcedure old, IrProcedure @new)
    {
        IrParameter[] positional = [.. @new.Parameters.Where(static p => !IsSynthesised(p.Var.Name))];
        Dictionary<string, IrParameter> synthesised = @new.Parameters
            .Where(static p => IsSynthesised(p.Var.Name))
            .ToDictionary(static p => p.Var.Name, StringComparer.Ordinal);
        HashSet<string> paired = new(StringComparer.Ordinal);
        List<SharedParameter> shared = [];
        int position = 0;
        foreach (IrParameter parameter in old.Parameters)
        {
            IrParameter? counterpart = IsSynthesised(parameter.Var.Name)
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

    /// <summary>
    /// Whether a parameter is one the frontend synthesised (VERIFICATION-MODEL.md section 2): the receiver
    /// <c>this</c>, or a heap or nullness input, whose name is spelled with dots. No source-language
    /// parameter name contains a dot.
    /// </summary>
    public static bool IsSynthesised(string name) => string.Equals(name, "this", StringComparison.Ordinal) || name.Contains('.', StringComparison.Ordinal);

    public static ProductEncoding Encode(Context context, IrProcedure old, IrProcedure @new, ImmutableDictionary<string, string> callIdentityMap)
    {
        SortMapper sorts = new(context);
        ImmutableArray<(SharedParameter Shared, Expr Term)> inputs =
        [
            .. Pair(old, @new).Select(s => (s, context.MkConst(s.InputName, sorts.Sort(s.Type)))),
        ];

        IEnumerable<IrType> argumentTypes = old.Blocks.Concat(@new.Blocks)
            .SelectMany(static b => b.Instructions.OfType<IrCall>())
            .SelectMany(static c => c.Args.Select(static a => a.Type));
        TraceEncoder calls = new(sorts, argumentTypes, callIdentityMap);
        Dictionary<string, int> exceptionTypes = new(StringComparer.Ordinal);
        SideEncoder oldSide = new(Side.Old, old, sorts, calls, Bound(inputs, static s => s.Old), exceptionTypes);
        SideEncoder newSide = new(Side.New, @new, sorts, calls, Bound(inputs, static s => s.New), exceptionTypes);

        List<BoolExpr> equal =
        [
            context.MkEq(oldSide.Returned, newSide.Returned),
            ReturnsEqual(context, sorts, oldSide, newSide),
            context.MkEq(oldSide.Threw, newSide.Threw),
            context.MkEq(oldSide.ExceptionType, newSide.ExceptionType),
        ];
        equal.AddRange(inputs
            .Where(static i => i.Shared.ByRef)
            .Select(i => context.MkEq(oldSide.Final(i.Shared.Old, i.Term), newSide.Final(i.Shared.New, i.Term))));
        equal.Add(context.MkEq(oldSide.Trace, newSide.Trace));

        return new ProductEncoding(
            [.. oldSide.Assertions, .. newSide.Assertions, .. sorts.Distinctness()],
            context.MkNot(context.MkAnd(equal)),
            oldSide.Opaque,
            newSide.Opaque,
            inputs,
            [.. oldSide.Opaques, .. newSide.Opaques],
            sorts,
            calls);
    }

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

    private static ImmutableArray<IrBlockId> Successors(IrTerminator terminator) => terminator switch
    {
        IrGoto jump => [jump.Target],
        IrBranch branch => [branch.Then, branch.Else],
        IrSwitch choice => [.. choice.Cases.Select(static c => c.Target), choice.Default],
        _ => [],
    };

    /// <summary>Encodes one side's blocks into assertions and exposes its observables.</summary>
    private sealed class SideEncoder
    {
        private readonly Side side;
        private readonly SortMapper sorts;
        private readonly TraceEncoder calls;
        private readonly Context context;
        private readonly Dictionary<string, Expr> inputs;
        private readonly Dictionary<string, Expr> constants = new(StringComparer.Ordinal);
        private readonly Dictionary<IrBlockId, BoolExpr> reach = [];
        private readonly Dictionary<IrBlockId, BitVecExpr> countOut = [];
        private readonly Dictionary<IrBlockId, List<(IrBlockId From, BoolExpr Taken)>> incoming = [];
        private readonly List<(IrBlockId Block, IrTerminator Exit)> exits = [];
        private readonly List<(BoolExpr Reach, IReadOnlyList<Expr> Events)> events = [];
        private readonly List<BoolExpr> assertions = [];
        private readonly List<(Side Side, IrOpaque Node, BoolExpr Reach)> opaques = [];

        public SideEncoder(
            Side side,
            IrProcedure procedure,
            SortMapper sorts,
            TraceEncoder calls,
            Dictionary<string, Expr> inputs,
            Dictionary<string, int> exceptionTypes)
        {
            this.side = side;
            this.sorts = sorts;
            this.calls = calls;
            this.inputs = inputs;
            Procedure = procedure;
            context = sorts.Context;

            foreach (IrBlock block in Analyze(procedure).ReversePostorder)
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
            Opaque = context.MkOr([context.MkFalse(), .. opaques.Select(static o => o.Reach).Distinct()]);
        }

        public IrProcedure Procedure { get; }

        public IReadOnlyList<BoolExpr> Assertions => assertions;

        public IReadOnlyList<(Side Side, IrOpaque Node, BoolExpr Reach)> Opaques => opaques;

        public BoolExpr Returned { get; }

        public BoolExpr Threw { get; }

        public IntExpr ExceptionType { get; }

        public SeqExpr Trace { get; }

        public BoolExpr Opaque { get; }

        /// <summary>The value returned, or <paramref name="none"/> (shared by both sides) when no return is reached.</summary>
        public Expr ReturnValue(Expr none) =>
            exits
                .Where(static e => e.Exit is IrReturn)
                .Reverse()
                .Aggregate(none, (rest, e) => context.MkITE(reach[e.Block], Var(((IrReturn)e.Exit).Value!), rest));

        /// <summary>
        /// The final value of <paramref name="parameter"/>: its version at the exit taken, else
        /// <paramref name="input"/>, which is also the value on a side without the parameter.
        /// </summary>
        public Expr Final(IrParameter? parameter, Expr input) =>
            exits
                .AsEnumerable()
                .Reverse()
                .Aggregate(input, (rest, e) =>
                {
                    IrOut? @out = Outs(e.Exit).FirstOrDefault(o => string.Equals(o.Param.Name, parameter?.Var.Name, StringComparison.Ordinal));
                    return @out is null ? rest : context.MkITE(reach[e.Block], Var(@out.Final), rest);
                });

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

        private BoolExpr Any(IEnumerable<(IrBlockId Block, IrTerminator Exit)> blocks) =>
            context.MkOr([context.MkFalse(), .. blocks.Select(e => reach[e.Block])]);

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
            if (entry)
            {
                Assert(reached);
                Assert(context.MkEq(count, context.MkBV(0, 32)));
            }
            else
            {
                Assert(context.MkEq(reached, context.MkOr(predecessors.Select(static p => p.Taken))));
                Assert(context.MkEq(count, Merge(predecessors, p => countOut[p.From])));
            }

            List<Expr> blockEvents = [];
            foreach (IrInstruction instruction in block.Instructions)
            {
                Encode(instruction, predecessors, reached, context.MkBVAdd(count, context.MkBV(blockEvents.Count, 32)), blockEvents);
            }

            events.Add((reached, blockEvents));
            countOut.Add(block.Id, context.MkBVAdd(count, context.MkBV(blockEvents.Count, 32)));
            EncodeTerminator(block, reached);
        }

        /// <summary>The value of the incoming edge taken: an <c>ite</c> over all but the last predecessor.</summary>
        private Expr Merge(List<(IrBlockId From, BoolExpr Taken)> predecessors, Func<(IrBlockId From, BoolExpr Taken), Expr> value) =>
            predecessors
                .Take(predecessors.Count - 1)
                .Reverse()
                .Aggregate(value(predecessors[^1]), (rest, p) => context.MkITE(p.Taken, value(p), rest));

        private void Encode(IrInstruction instruction, List<(IrBlockId From, BoolExpr Taken)> predecessors, BoolExpr reached, BitVecExpr position, List<Expr> blockEvents)
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
                    {
                        (Expr? result, BoolExpr threw, Expr @event) = calls.Call(side, call, [.. call.Args.Select(a => (a.Type, Var(a)))], position);
                        if (call.Target is not null)
                        {
                            Define(call.Target, result!);
                        }

                        if (call.Threw is not null)
                        {
                            Define(call.Threw, threw);
                        }

                        blockEvents.Add(@event);
                        break;
                    }

                case IrMapRead read:
                    Define(read.Target, context.MkSelect((ArrayExpr)Var(read.Map), Var(read.Key)));
                    break;
                case IrMapWrite write:
                    Define(write.Target, context.MkStore((ArrayExpr)Var(write.Map), Var(write.Key), Var(write.Value)));
                    break;
                default:
                    opaques.Add((side, (IrOpaque)instruction, reached));
                    break;
            }
        }

        private void Define(IrVar target, Expr value) => Assert(context.MkEq(Var(target), value));

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
                            edges.Add((target, context.MkAnd([matches, .. earlier.Select(context.MkNot)])));
                            earlier.Add(matches);
                        }

                        edges.Add((choice.Default, context.MkAnd([context.MkTrue(), .. earlier.Select(context.MkNot)])));
                        break;
                    }

                case IrUnreachable:
                    Assert(context.MkNot(reached));
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
    /// The product query for one pair: <see cref="Assertions"/> hold on every input, <see cref="Differs"/>
    /// says some observable differs, and <see cref="OpaqueOld"/>/<see cref="OpaqueNew"/> say that side
    /// reaches an <see cref="IrOpaque"/> (ADR 0014). <see cref="Inputs"/> lists the shared inputs in
    /// <see cref="Pair"/> order with their Z3 constants.
    /// </summary>
    public sealed record ProductEncoding(
        ImmutableArray<BoolExpr> Assertions,
        BoolExpr Differs,
        BoolExpr OpaqueOld,
        BoolExpr OpaqueNew,
        ImmutableArray<(SharedParameter Shared, Expr Term)> Inputs,
        ImmutableArray<(Side Side, IrOpaque Node, BoolExpr Reach)> Opaques,
        SortMapper Sorts,
        TraceEncoder Calls);
}
