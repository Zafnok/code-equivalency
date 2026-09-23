using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// Calls and the call trace (VERIFICATION-MODEL.md section 5; ADR 0018; ticket M3-001). A call's result and
/// <c>threw</c> flag are uninterpreted functions of its arguments and its position, one pair per callee
/// identity and signature, shared by both sides, except that a <see cref="CallIdentity.RuntimeChanged"/>
/// callee gets one pair per side. A legacy identity in the config's call-identity map is renamed to its
/// modern counterpart first. A trace is a <c>Seq</c> of <c>event(callee, args)</c>, the arguments boxed in
/// a <c>Value</c> datatype with one injective constructor per IR type in use.
/// </summary>
internal sealed class TraceEncoder
{
    private readonly Context context;
    private readonly SortMapper sorts;
    private readonly ImmutableDictionary<string, string> callIdentityMap;
    private readonly Dictionary<string, FuncDecl> functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> callees = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FuncDecl> boxes = new(StringComparer.Ordinal);
    private readonly SeqSort values;
    private readonly FuncDecl eventConstructor;
    private readonly SeqSort trace;

    public TraceEncoder(SortMapper sorts, IEnumerable<IrType> argumentTypes, ImmutableDictionary<string, string> callIdentityMap)
    {
        this.sorts = sorts;
        this.callIdentityMap = callIdentityMap;
        context = sorts.Context;

        // Bool is always a constructor, so the datatype is never empty when no call has arguments.
        IrType[] boxed = [.. argumentTypes.Append(new IrBool()).DistinctBy(SortMapper.Name, StringComparer.Ordinal).OrderBy(SortMapper.Name, StringComparer.Ordinal)];
        DatatypeSort value = Datatype("Value", [.. boxed.Select(t => ("of:" + SortMapper.Name(t), "un:" + SortMapper.Name(t), sorts.Sort(t)))]);
        for (int i = 0; i < boxed.Length; i++)
        {
            boxes.Add(SortMapper.Name(boxed[i]), value.Constructors[i]);
        }

        values = context.MkSeqSort(value);
        DatatypeSort @event = Datatype("Event", [("event", "callee", context.IntSort), ("event", "args", values)]);
        eventConstructor = @event.Constructors[0];
        trace = context.MkSeqSort(@event);
    }

    /// <summary>The result, <c>threw</c> flag and trace event of <paramref name="call"/> on <paramref name="side"/> at <paramref name="position"/>.</summary>
    public (Expr? Result, BoolExpr Threw, Expr Event) Call(Side side, IrCall call, ImmutableArray<(IrType Type, Expr Term)> args, BitVecExpr position)
    {
        ImmutableArray<IrType> types = [.. args.Select(static a => a.Type)];
        Expr[] applied = [.. args.Select(static a => a.Term), position];
        Expr? result = call.Target is null ? null : context.MkApp(ResultFunction(side, call.Callee, types, call.Target.Type), applied);
        BoolExpr threw = (BoolExpr)context.MkApp(ThrewFunction(side, call.Callee, types), applied);
        SeqExpr boxed = context.MkConcat([context.MkEmptySeq(values), .. args.Select(a => context.MkUnit(context.MkApp(boxes[SortMapper.Name(a.Type)], a.Term)))]);
        Expr @event = context.MkApp(eventConstructor, context.MkInt(Callee(Canonical(side, call.Callee))), boxed);
        return (result, threw, @event);
    }

    /// <summary>The trace of one side: its blocks' events in reverse postorder, each block's only when it is reached.</summary>
    public SeqExpr Trace(IEnumerable<(BoolExpr Reach, IReadOnlyList<Expr> Events)> blocks)
    {
        SeqExpr empty = context.MkEmptySeq(trace);
        return context.MkConcat(
        [
            empty,
            .. blocks
                .Where(static b => b.Events.Count > 0)
                .Select(b => (SeqExpr)context.MkITE(b.Reach, context.MkConcat([.. b.Events.Select(context.MkUnit)]), empty)),
        ]);
    }

    /// <summary>The result function <c>f(args..., position)</c> for a callee and signature, created on first use.</summary>
    public FuncDecl ResultFunction(Side side, CallIdentity callee, ImmutableArray<IrType> argumentTypes, IrType resultType) =>
        Function("f", side, callee, argumentTypes, sorts.Sort(resultType), "->" + SortMapper.Name(resultType));

    /// <summary>The <c>threw</c> function for a callee and argument types, created on first use.</summary>
    public FuncDecl ThrewFunction(Side side, CallIdentity callee, ImmutableArray<IrType> argumentTypes) =>
        Function("threw", side, callee, argumentTypes, context.BoolSort, string.Empty);

    /// <summary>The identity both sides' traces use for <paramref name="callee"/>: a legacy identity is renamed through the call-identity map.</summary>
    public string Canonical(Side side, CallIdentity callee) =>
        side == Side.Old ? callIdentityMap.GetValueOrDefault(callee.Value, callee.Value) : callee.Value;

    /// <summary>
    /// A datatype whose constructors are named by the distinct first items of <paramref name="fields"/>, each
    /// with the fields listed under it. The <see cref="Constructor"/> objects are released before returning:
    /// their finalizers would otherwise run after the <see cref="Context"/> is disposed and crash the process.
    /// </summary>
    private DatatypeSort Datatype(string name, (string Constructor, string Field, Sort Sort)[] fields)
    {
        Constructor[] constructors =
        [
            .. fields.GroupBy(static f => f.Constructor, StringComparer.Ordinal).Select(g =>
                context.MkConstructor(g.Key, "is:" + g.Key, [.. g.Select(static f => f.Field)], [.. g.Select(static f => f.Sort)], sortRefs: null)),
        ];
        try
        {
            return context.MkDatatypeSort(name, constructors);
        }
        finally
        {
            foreach (Constructor constructor in constructors)
            {
                constructor.Dispose();
            }
        }
    }

    private int Callee(string identity)
    {
        if (!callees.TryGetValue(identity, out int index))
        {
            index = callees.Count;
            callees.Add(identity, index);
        }

        return index;
    }

    private FuncDecl Function(string kind, Side side, CallIdentity callee, ImmutableArray<IrType> argumentTypes, Sort range, string suffix)
    {
        string owner = callee.RuntimeChanged ? ":" + ProductEncoder.Prefix(side) : string.Empty;
        string name = $"{kind}:{Canonical(side, callee)}({string.Join(',', argumentTypes.Select(SortMapper.Name))}){suffix}{owner}";
        if (!functions.TryGetValue(name, out FuncDecl? function))
        {
            function = context.MkFuncDecl(name, [.. argumentTypes.Select(sorts.Sort), context.MkBitVecSort(32)], range);
            functions.Add(name, function);
        }

        return function;
    }
}
