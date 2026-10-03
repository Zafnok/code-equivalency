using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// Calls and the call trace (VERIFICATION-MODEL.md section 5; ADR 0018; ticket M3-001). A call's result and
/// <c>threw</c> flag are uninterpreted functions of its arguments, its position and the heap at the call, one pair per
/// callee identity and signature, shared by both sides, except that a <see cref="CallIdentity.RuntimeChanged"/>
/// callee gets one pair per side, and so does a callee in <c>freshPerSide</c>, whose calls a callee contract relates
/// instead (ADR 0036 decision 2; ticket P1-010). So is the new version of each heap map, one <c>heap:</c> function per callee, signature
/// and map (ticket P1-005), and the new value of each <c>ref</c> or <c>out</c> argument, one <c>refout:</c> function per
/// callee, signature and output index (ticket M4-003). The heap at a call is one value per <see cref="Heap"/> map, in that order. A callee
/// identity every call to which is closed takes no heap, leaves every map as it was and has no heap in its event (ADR 0041;
/// ticket P2-060). A legacy identity
/// in the config's call-identity map is renamed to its modern counterpart first. A trace is a <c>Seq</c> of
/// <c>event(callee, args)</c>, whose <c>args</c> are the arguments followed by the heap at the call (as long on both
/// sides, so the concatenation is injective), boxed in a <c>Value</c> datatype with one injective constructor per IR
/// type in use.
/// </summary>
internal sealed class TraceEncoder
{
    /// <summary>
    /// The most operands one concatenation is given (ticket P2-121). Z3 makes a concatenation of n operands a chain n
    /// deep and recurses natively on that depth: <c>Z3_mk_seq_concat</c> overflowed a 1 MB stack between 8,000 and 10,000
    /// operands, and a stack overflow ends the process where no <c>catch</c> sees it (ADR 0023, clarified 2026-10-03).
    /// </summary>
    internal const int MaxOperands = 256;

    private readonly Context context;
    private readonly SortMapper sorts;
    private readonly ImmutableDictionary<string, string> callIdentityMap;
    private readonly IReadOnlySet<string> freshPerSide;
    private readonly HashSet<string> closed;
    private readonly Dictionary<string, FuncDecl> functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> callees = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FuncDecl> boxes = new(StringComparer.Ordinal);
    private readonly SeqSort values;
    private readonly FuncDecl eventConstructor;
    private readonly SeqSort trace;

    /// <param name="sorts">The sort mapper of the encoding.</param>
    /// <param name="argumentTypes">The type of every call argument, each of which the trace boxes.</param>
    /// <param name="callIdentityMap">The call-identity unifications legacy calls are renamed with.</param>
    /// <param name="heap">The maps the heap at a call ranges over.</param>
    /// <param name="freshPerSide">The callees whose calls get one set of functions per side.</param>
    /// <param name="sites">Every call of the product with its side, from which the closed identities are read.</param>
    public TraceEncoder(
        SortMapper sorts,
        IEnumerable<IrType> argumentTypes,
        ImmutableDictionary<string, string> callIdentityMap,
        ImmutableArray<HeapMap> heap,
        IReadOnlySet<string>? freshPerSide = null,
        IEnumerable<(Side Side, IrCall Call)>? sites = null)
    {
        this.sorts = sorts;
        this.callIdentityMap = callIdentityMap;
        this.freshPerSide = freshPerSide ?? new HashSet<string>(StringComparer.Ordinal);
        closed = new(
            (sites ?? []).GroupBy(s => Canonical(s.Side, s.Call.Callee), StringComparer.Ordinal).Where(static g => g.All(static s => s.Call.Closed)).Select(static g => g.Key),
            StringComparer.Ordinal);
        Heap = heap;
        context = sorts.Context;

        // Bool is always a constructor, so the datatype is never empty when no call has arguments.
        IrType[] boxed = [.. argumentTypes.Concat(heap.Select(static h => h.Type)).Append(new IrBool()).DistinctBy(SortMapper.Name, StringComparer.Ordinal).OrderBy(SortMapper.Name, StringComparer.Ordinal)];
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

    /// <summary>The maps the heap at a call ranges over: every map a heap pair names on either side, in name order (ticket P1-005).</summary>
    public ImmutableArray<HeapMap> Heap { get; }

    /// <summary>
    /// Whether <paramref name="callee"/>, called on <paramref name="side"/>, is encoded as closed: every call to its canonical
    /// identity in the product is closed (ADR 0041).
    /// </summary>
    public bool IsClosed(Side side, CallIdentity callee) => closed.Contains(Canonical(side, callee));

    /// <summary>
    /// The result, <c>threw</c> flag, trace event, new heap (one term per <see cref="Heap"/> map) and ref outputs (one term per
    /// <see cref="IrCall.RefOuts"/> entry, ticket M4-003) of <paramref name="call"/> on <paramref name="side"/> at
    /// <paramref name="position"/>, given the heap <paramref name="heap"/> at the call. A closed callee neither reads
    /// <paramref name="heap"/> nor changes it, and its event has no heap.
    /// </summary>
    public (Expr? Result, BoolExpr Threw, Expr Event, ImmutableArray<Expr> Heap, ImmutableArray<Expr> RefOuts) Call(
        Side side, IrCall call, ImmutableArray<(IrType Type, Expr Term)> args, BitVecExpr position, ImmutableArray<Expr> heap)
    {
        ImmutableArray<IrType> types = [.. args.Select(static a => a.Type)];
        bool closedCall = IsClosed(side, call.Callee);
        ImmutableArray<Expr> reads = closedCall ? [] : heap;
        Expr[] applied = [.. args.Select(static a => a.Term), position, .. reads];
        Expr? result = call.Target is null ? null : context.MkApp(ResultFunction(side, call.Callee, types, call.Target.Type), applied);
        BoolExpr threw = (BoolExpr)context.MkApp(ThrewFunction(side, call.Callee, types), applied);
        IEnumerable<(IrType Type, Expr Term)> read = args.Concat(Heap.Zip(reads, static (m, term) => (m.Type, term)));
        SeqExpr boxed = context.MkConcat([context.MkEmptySeq(values), .. read.Select(a => context.MkUnit(context.MkApp(boxes[SortMapper.Name(a.Type)], a.Term)))]);
        Expr @event = context.MkApp(eventConstructor, context.MkInt(Callee(Canonical(side, call.Callee))), boxed);
        return (
            result,
            threw,
            @event,
            closedCall ? heap : [.. Heap.Select((_, i) => context.MkApp(HeapFunction(side, call.Callee, types, i), applied))],
            [.. call.RefOuts.Select((r, i) => context.MkApp(RefOutFunction(side, call.Callee, types, i, r.Type), applied))]);
    }

    /// <summary>
    /// The trace of one side: its blocks' events in reverse postorder, each block's only when it is reached. However many
    /// blocks or events there are, the term is no deeper than <see cref="Concat"/> makes it.
    /// </summary>
    public SeqExpr Trace(IEnumerable<(BoolExpr Reach, IReadOnlyList<Expr> Events)> blocks)
    {
        SeqExpr empty = context.MkEmptySeq(trace);
        return Concat(
        [
            empty,
            .. blocks
                .Where(static b => b.Events.Count > 0)
                .Select(b => (SeqExpr)context.MkITE(b.Reach, Concat([.. b.Events.Select(context.MkUnit)]), empty)),
        ]);
    }

    /// <summary>
    /// The concatenation of <paramref name="operands"/> in order: one <c>seq.++</c> of up to <see cref="MaxOperands"/>
    /// operands, and of the two halves' concatenations beyond that. Concatenation is associative, so it is the same
    /// sequence either way.
    /// </summary>
    private SeqExpr Concat(ReadOnlySpan<SeqExpr> operands)
    {
        if (operands.Length <= MaxOperands)
        {
            return context.MkConcat([.. operands]);
        }

        int half = operands.Length / 2;
        return context.MkConcat(Concat(operands[..half]), Concat(operands[half..]));
    }

    /// <summary>The result function <c>f(args..., position)</c> for a callee and signature, created on first use.</summary>
    public FuncDecl ResultFunction(Side side, CallIdentity callee, ImmutableArray<IrType> argumentTypes, IrType resultType) =>
        Function("f", side, callee, argumentTypes, sorts.Sort(resultType), "->" + SortMapper.Name(resultType));

    /// <summary>The <c>threw</c> function for a callee and argument types, created on first use.</summary>
    public FuncDecl ThrewFunction(Side side, CallIdentity callee, ImmutableArray<IrType> argumentTypes) =>
        Function("threw", side, callee, argumentTypes, context.BoolSort, string.Empty);

    /// <summary>The function giving the new version of <see cref="Heap"/> map <paramref name="map"/> after a call, created on first use.</summary>
    public FuncDecl HeapFunction(Side side, CallIdentity callee, ImmutableArray<IrType> argumentTypes, int map) =>
        Function("heap", side, callee, argumentTypes, sorts.Sort(Heap[map].Type), "$" + Heap[map].Name + ":" + SortMapper.Name(Heap[map].Type));

    /// <summary>
    /// The function giving the new value of a call's <paramref name="index"/>th <c>ref</c> or <c>out</c> argument, of type
    /// <paramref name="type"/> (ticket M4-003), created on first use. It takes what the result function takes.
    /// </summary>
    public FuncDecl RefOutFunction(Side side, CallIdentity callee, ImmutableArray<IrType> argumentTypes, int index, IrType type) =>
        Function("refout", side, callee, argumentTypes, sorts.Sort(type), "$" + index.ToString(CultureInfo.InvariantCulture) + ":" + SortMapper.Name(type));

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
        string owner = callee.RuntimeChanged || freshPerSide.Contains(Canonical(side, callee)) ? ":" + ProductEncoder.Prefix(side) : string.Empty;
        string name = $"{kind}:{Canonical(side, callee)}({string.Join(',', argumentTypes.Select(SortMapper.Name))}){suffix}{owner}";
        if (!functions.TryGetValue(name, out FuncDecl? function))
        {
            IEnumerable<HeapMap> reads = IsClosed(side, callee) ? [] : Heap;
            function = context.MkFuncDecl(name, [.. argumentTypes.Select(sorts.Sort), context.MkBitVecSort(32), .. reads.Select(h => sorts.Sort(h.Type))], range);
            functions.Add(name, function);
        }

        return function;
    }

    /// <summary>One map the heap at a call ranges over: a heap pair's map name and type (ticket P1-005).</summary>
    public sealed record HeapMap(string Name, IrType Type);
}
