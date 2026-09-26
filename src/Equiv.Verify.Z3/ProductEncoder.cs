using System.Collections.Frozen;
using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

namespace Equiv.Verify.Z3;

/// <summary>
/// The product program of an acyclic pair (VERIFICATION-MODEL.md sections 1, 2 and 5; ADR 0014; ADR 0018;
/// ADR 0021; ticket M3-001). Both sides are encoded over one set of inputs, the <see cref="SharedParameter"/>s
/// of <see cref="Pair"/>, each side by a <see cref="FragmentEncoder"/>, and the query says some observable differs.
/// The ladder of ticket M3-002 encodes unrolled procedures and loop fragments with it; each is acyclic.
/// </summary>
internal static class ProductEncoder
{
    /// <summary>Each bitvector <see cref="IrBinaryOp"/> as a Z3 term (the comparisons are Bool).</summary>
    public static readonly FrozenDictionary<IrBinaryOp, Func<Context, BitVecExpr, BitVecExpr, Expr>> BitVecOps =
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

    /// <summary>The Bool <see cref="IrBinaryOp"/>s other than equality.</summary>
    public static readonly FrozenDictionary<IrBinaryOp, Func<Context, BoolExpr, BoolExpr, Expr>> BoolOps =
        new Dictionary<IrBinaryOp, Func<Context, BoolExpr, BoolExpr, Expr>>
        {
            [IrBinaryOp.And] = static (c, a, b) => c.MkAnd(a, b),
            [IrBinaryOp.Or] = static (c, a, b) => c.MkOr(a, b),
            [IrBinaryOp.Xor] = static (c, a, b) => c.MkXor(a, b),
        }.ToFrozenDictionary();

    /// <summary>"Does not overflow" per checked operation; <see cref="IrOverflows"/> is its negation.</summary>
    public static readonly FrozenDictionary<IrOverflowOp, Func<Context, BitVecExpr, BitVecExpr, BoolExpr>> NoOverflow =
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
        FragmentEncoder oldSide = new(Side.Old, old, sorts, (calls, pures), Bound(inputs, static s => s.Old), heapInputs, exceptionTypes);
        FragmentEncoder newSide = new(Side.New, @new, sorts, (calls, pures), Bound(inputs, static s => s.New), heapInputs, exceptionTypes);

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
    private static BoolExpr ReturnsEqual(Context context, SortMapper sorts, FragmentEncoder old, FragmentEncoder @new)
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
