using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Ir;

using Microsoft.Z3;

namespace Equiv.Verify.Z3;

/// <summary>
/// The product program of an acyclic pair (VERIFICATION-MODEL.md sections 1, 2 and 5; ADR 0014; ADR 0018;
/// ticket M3-001). Both sides are encoded over one set of inputs, <c>in.&lt;name&gt;</c>, a parameter name
/// present on either side. Every other SSA variable is a constant <c>old.&lt;name&gt;</c> or
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
            [IrOverflowOp.SAdd] = static (c, a, b) => c.MkAnd(c.MkBVAddNoOverflow(a, b, true), c.MkBVAddNoUnderflow(a, b)),
            [IrOverflowOp.UAdd] = static (c, a, b) => c.MkBVAddNoOverflow(a, b, false),
            [IrOverflowOp.SSub] = static (c, a, b) => c.MkAnd(c.MkBVSubNoOverflow(a, b), c.MkBVSubNoUnderflow(a, b, true)),
            [IrOverflowOp.USub] = static (c, a, b) => c.MkBVSubNoUnderflow(a, b, false),
            [IrOverflowOp.SMul] = static (c, a, b) => c.MkAnd(c.MkBVMulNoOverflow(a, b, true), c.MkBVMulNoUnderflow(a, b)),
            [IrOverflowOp.UMul] = static (c, a, b) => c.MkBVMulNoOverflow(a, b, false),
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

    public static ProductEncoding Encode(Context context, IrProcedure old, IrProcedure @new, ImmutableDictionary<string, string> callIdentityMap)
    {
        SortMapper sorts = new(context);
        Dictionary<string, (IrParameter Parameter, Expr Input)> inputs = new(StringComparer.Ordinal);
        foreach (IrParameter parameter in old.Parameters.Concat(@new.Parameters))
        {
            if (inputs.TryGetValue(parameter.Var.Name, out (IrParameter Parameter, Expr Input) shared))
            {
                if (shared.Parameter.Var.Type != parameter.Var.Type)
                {
                    throw new ArgumentException($"Parameter {parameter.Var.Name} has a different type on each side.", nameof(@new));
                }

                continue;
            }

            inputs.Add(parameter.Var.Name, (parameter, context.MkConst("in." + parameter.Var.Name, sorts.Sort(parameter.Var.Type))));
        }

        IEnumerable<IrType> argumentTypes = old.Blocks.Concat(@new.Blocks)
            .SelectMany(static b => b.Instructions.OfType<IrCall>())
            .SelectMany(static c => c.Args.Select(static a => a.Type));
        TraceEncoder calls = new(sorts, argumentTypes, callIdentityMap);
        Dictionary<string, int> exceptionTypes = new(StringComparer.Ordinal);
        SideEncoder oldSide = new(Side.Old, old, sorts, calls, inputs, exceptionTypes);
        SideEncoder newSide = new(Side.New, @new, sorts, calls, inputs, exceptionTypes);

        List<BoolExpr> equal =
        [
            context.MkEq(oldSide.Returned, newSide.Returned),
            ReturnsEqual(context, sorts, oldSide, newSide),
            context.MkEq(oldSide.Threw, newSide.Threw),
            context.MkEq(oldSide.ExceptionType, newSide.ExceptionType),
        ];
        IEnumerable<string> byRef = old.Parameters.Concat(@new.Parameters)
            .Where(static p => p.Kind != IrParameterKind.In)
            .Select(static p => p.Var.Name)
            .Distinct(StringComparer.Ordinal);
        equal.AddRange(byRef.Select(name => context.MkEq(oldSide.Final(name), newSide.Final(name))));
        equal.Add(context.MkEq(oldSide.Trace, newSide.Trace));

        return new ProductEncoding(
            [.. oldSide.Assertions, .. newSide.Assertions, .. sorts.Distinctness()],
            context.MkNot(context.MkAnd(equal)),
            oldSide.Opaque,
            newSide.Opaque,
            [.. inputs.Values],
            [.. oldSide.Opaques, .. newSide.Opaques],
            sorts,
            calls);
    }

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
        private readonly IReadOnlyDictionary<string, (IrParameter Parameter, Expr Input)> inputs;
        private readonly HashSet<string> parameters;
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
            IReadOnlyDictionary<string, (IrParameter Parameter, Expr Input)> inputs,
            Dictionary<string, int> exceptionTypes)
        {
            this.side = side;
            this.sorts = sorts;
            this.calls = calls;
            this.inputs = inputs;
            Procedure = procedure;
            context = sorts.Context;
            parameters = [.. procedure.Parameters.Select(static p => p.Var.Name)];

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

        /// <summary>The final value of by-ref parameter <paramref name="name"/>: its version at the exit taken, else the shared input.</summary>
        public Expr Final(string name) =>
            exits
                .AsEnumerable()
                .Reverse()
                .Aggregate(inputs[name].Input, (rest, e) =>
                {
                    IrOut? @out = Outs(e.Exit).FirstOrDefault(o => string.Equals(o.Param.Name, name, StringComparison.Ordinal));
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
            if (parameters.Contains(var.Name))
            {
                return inputs[var.Name].Input;
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
    /// The product query for one pair: <see cref="Assertions"/> hold on every input, <see cref="Differs"/>
    /// says some observable differs, and <see cref="OpaqueOld"/>/<see cref="OpaqueNew"/> say that side
    /// reaches an <see cref="IrOpaque"/> (ADR 0014). <see cref="Inputs"/> lists the shared inputs, the old
    /// side's parameters first.
    /// </summary>
    public sealed record ProductEncoding(
        ImmutableArray<BoolExpr> Assertions,
        BoolExpr Differs,
        BoolExpr OpaqueOld,
        BoolExpr OpaqueNew,
        ImmutableArray<(IrParameter Parameter, Expr Input)> Inputs,
        ImmutableArray<(Side Side, IrOpaque Node, BoolExpr Reach)> Opaques,
        SortMapper Sorts,
        TraceEncoder Calls);
}
