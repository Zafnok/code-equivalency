using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using HeapMap = Equiv.Verify.Z3.TraceEncoder.HeapMap;
using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// Turns a satisfying model of the product query into a <see cref="Counterexample"/> (ticket M3-001). Inputs
/// are read with model completion. An element of an uninterpreted sort becomes <c>sort "S" n</c>: a literal
/// keeps its own id, any other element gets the next free id. Both sides are then replayed in
/// <see cref="IrInterpreter"/>, with a call oracle that answers from the model's call functions at the
/// position the interpreter passes, and the replay must diverge on the observables the encoder compares. The oracle threads
/// each heap map a call does not pair as the encoder does, and each call event is completed with the whole heap the call
/// read (ticket P1-005). The replay
/// taints every call whose identity starts with <see cref="OpaquePrefix"/> (ADR 0026; ticket M3-016): only a difference
/// in an observable that is untainted on both sides is real.
/// </summary>
internal sealed class ModelDecoder
{
    /// <summary>The identity prefix of a shared opaque fragment's call (ADR 0024), an abstraction the replay taints.</summary>
    public const string OpaquePrefix = "opaque:";

    private readonly Context context;
    private readonly Model model;
    private readonly ProductEncoding encoding;
    private readonly Dictionary<string, Dictionary<string, int>> ids = new(StringComparer.Ordinal);
    private readonly Dictionary<IrSortValue, Expr> elements = [];

    public ModelDecoder(Context context, Model model, ProductEncoding encoding)
    {
        this.context = context;
        this.model = model;
        this.encoding = encoding;
        foreach ((IrSortValue literal, Expr constant) in encoding.Sorts.SortLiterals)
        {
            Remember(literal, model.Eval(constant, completion: true));
        }
    }

    /// <summary>How two replayed runs differ on the observables the encoder compares.</summary>
    public enum Difference
    {
        /// <summary>They agree on every compared observable.</summary>
        None,

        /// <summary>They differ, but only in observables tainted on at least one side.</summary>
        Abstract,

        /// <summary>Some compared observable differs and is untainted on both sides.</summary>
        Real,
    }

    /// <summary>
    /// Decodes the model and replays both sides with taint. A real difference is <see cref="Divergent"/>; a difference
    /// only in tainted observables is <see cref="UnknownReason.Abstraction"/>, carrying the replay as its candidate
    /// counterexample; no difference fails loudly (an encoder bug).
    /// </summary>
    public static Verdict Replay(Context context, Model model, ProductEncoding encoding, IrProcedure old, IrProcedure @new)
    {
        ModelDecoder decoder = new(context, model, encoding);
        IrInputs inputs = decoder.Inputs();
        ImmutableArray<SharedParameter> shared = [.. encoding.Inputs.Select(static i => i.Shared)];
        ModelOracle oldOracle = decoder.Oracle(Side.Old);
        ModelOracle newOracle = decoder.Oracle(Side.New);
        IrRun oldRun = oldOracle.Complete(Run(old, Bind(old, shared, inputs, static s => s.Old), oldOracle));
        IrRun newRun = newOracle.Complete(Run(@new, Bind(@new, shared, inputs, static s => s.New), newOracle));
        Counterexample counterexample = new(inputs, oldRun, newRun);
        return EnsureDiverges(old, @new, shared, inputs, oldRun, newRun, encoding.Calls, oldOracle.Threaded, newOracle.Threaded) == Difference.Real
            ? new Divergent(counterexample)
            : Unknown.DependingOn(counterexample, [.. Abstractions(Codebase.Legacy, oldRun), .. Abstractions(Codebase.Modern, newRun)]);
    }

    /// <summary>Whether <paramref name="callee"/> is an abstraction the replay taints.</summary>
    public static bool IsAbstraction(CallIdentity callee) => callee.Value.StartsWith(OpaquePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Replays <paramref name="old"/> and <paramref name="new"/>, whose parameters are those of the fragments the model
    /// satisfies, from the decoded inputs (ticket M3-002). A model of a loop obligation is a real counterexample only
    /// when this replay completes on both sides within <paramref name="stepBudget"/> steps without reaching an
    /// <see cref="IrOpaque"/> and diverges in an untainted observable; otherwise it returns null.
    /// </summary>
    public static Counterexample? TryReplay(Context context, Model model, ProductEncoding encoding, IrProcedure old, IrProcedure @new, int stepBudget)
    {
        ModelDecoder decoder = new(context, model, encoding);
        IrInputs inputs = decoder.Inputs();
        ImmutableArray<SharedParameter> shared = [.. encoding.Inputs.Select(static i => i.Shared)];
        ModelOracle oldOracle = decoder.Oracle(Side.Old);
        ModelOracle newOracle = decoder.Oracle(Side.New);
        IrRun oldRun = oldOracle.Complete(IrInterpreter.Run(old, Bind(old, shared, inputs, static s => s.Old), oldOracle, stepBudget, IsAbstraction));
        IrRun newRun = newOracle.Complete(IrInterpreter.Run(@new, Bind(@new, shared, inputs, static s => s.New), newOracle, stepBudget, IsAbstraction));
        bool complete = new[] { oldRun, newRun }.All(static r => r.Outcome is IrReturned or IrThrew);
        return complete && Diverges(old, @new, shared, inputs, oldRun, newRun, encoding.Calls, oldOracle.Threaded, newOracle.Threaded) ? new Counterexample(inputs, oldRun, newRun) : null;
    }

    /// <summary>
    /// A model whose replay agrees on every compared observable is an encoder bug: fail loudly, never report it as
    /// Divergent. Otherwise returns how the runs differ.
    /// </summary>
    public static Difference EnsureDiverges(
        IrProcedure old,
        IrProcedure @new,
        ImmutableArray<SharedParameter> shared,
        IrInputs inputs,
        IrRun oldRun,
        IrRun newRun,
        TraceEncoder calls,
        IReadOnlyDictionary<HeapMap, IrValue>? oldThreaded = null,
        IReadOnlyDictionary<HeapMap, IrValue>? newThreaded = null)
    {
        Difference difference = Compare(old, @new, shared, inputs, oldRun, newRun, calls, oldThreaded, newThreaded);
        return difference != Difference.None
            ? difference
            : throw new InvalidOperationException(
                $"Encoder bug: the solver found a divergence between {old.Identity.Value} and {@new.Identity.Value}, but the replay does not diverge. "
                + $"Inputs: {string.Join(", ", shared.Select((s, i) => $"{s.Var.Name}={inputs.Arguments[i]}"))}. Old: {Describe(oldRun)}. New: {Describe(newRun)}.");
    }

    /// <summary>True when the two runs differ in an observable the encoder compares that is untainted on both sides.</summary>
    public static bool Diverges(
        IrProcedure old,
        IrProcedure @new,
        ImmutableArray<SharedParameter> shared,
        IrInputs inputs,
        IrRun oldRun,
        IrRun newRun,
        TraceEncoder calls,
        IReadOnlyDictionary<HeapMap, IrValue>? oldThreaded = null,
        IReadOnlyDictionary<HeapMap, IrValue>? newThreaded = null) =>
        Compare(old, @new, shared, inputs, oldRun, newRun, calls, oldThreaded, newThreaded) == Difference.Real;

    /// <summary>
    /// How the two runs differ on the observables the encoder compares: outcome, call trace (legacy identities renamed
    /// through the call-identity map), and the final value of each by-ref shared input (<paramref name="shared"/>,
    /// valued by <paramref name="inputs"/>), a side without that parameter standing for the version its oracle threaded
    /// through its calls (<paramref name="oldThreaded"/>, <paramref name="newThreaded"/>; ticket P1-005) or else its unchanged
    /// input. A difference is real when some differing observable is untainted on both sides (ADR 0026). Two returned values
    /// compare by the values' taint, any other outcome difference by the path's. A threaded version is tainted once the run
    /// reached an abstraction. The trace compares its first differing event only,
    /// since control taint can shift every later position.
    /// </summary>
    public static Difference Compare(
        IrProcedure old,
        IrProcedure @new,
        ImmutableArray<SharedParameter> shared,
        IrInputs inputs,
        IrRun oldRun,
        IrRun newRun,
        TraceEncoder calls,
        IReadOnlyDictionary<HeapMap, IrValue>? oldThreaded = null,
        IReadOnlyDictionary<HeapMap, IrValue>? newThreaded = null)
    {
        Replayed oldSide = new(old, oldRun, oldThreaded ?? ImmutableDictionary<HeapMap, IrValue>.Empty);
        Replayed newSide = new(@new, newRun, newThreaded ?? ImmutableDictionary<HeapMap, IrValue>.Empty);
        List<bool> tainted = [];
        if (oldRun.Outcome != newRun.Outcome)
        {
            tainted.Add(oldRun.Outcome is IrReturned && newRun.Outcome is IrReturned
                ? oldRun.Taint.Value || newRun.Taint.Value
                : oldRun.Taint.Outcome || newRun.Taint.Outcome);
        }

        ImmutableArray<IrCallRecord> oldTrace = [.. oldRun.Trace.Select(r => r with { Callee = new CallIdentity(calls.Canonical(Side.Old, r.Callee)) })];
        ImmutableArray<IrCallRecord> newTrace = [.. newRun.Trace.Select(static r => r with { Callee = new CallIdentity(r.Callee.Value) })];
        int common = Math.Min(oldTrace.Length, newTrace.Length);
        int first = Enumerable.Range(0, common).Where(i => oldTrace[i] != newTrace[i]).DefaultIfEmpty(common).First();
        if (first < common || oldTrace.Length != newTrace.Length)
        {
            tainted.Add(oldRun.EventTainted(first) || newRun.EventTainted(first));
        }

        tainted.AddRange(shared
            .Select((s, i) => (Shared: s, Input: inputs.Arguments[i]))
            .Where(s => s.Shared.ByRef && oldSide.Final(s.Shared.Old, s.Shared.Var, s.Input) != newSide.Final(s.Shared.New, s.Shared.Var, s.Input))
            .Select(s => oldSide.FinalTainted(s.Shared.Old, s.Shared.Var) || newSide.FinalTainted(s.Shared.New, s.Shared.Var)));

        return tainted switch
        {
            [] => Difference.None,
            _ when tainted.Contains(false) => Difference.Real,
            _ => Difference.Abstract,
        };
    }

    /// <summary>The shared inputs, in <see cref="ProductEncoding.Inputs"/> order.</summary>
    public IrInputs Inputs() =>
        new([.. encoding.Inputs.Select(i => Decode(model.Eval(i.Term, completion: true), i.Shared.Type))]);

    public IrValue Decode(Expr value, IrType type) => type switch
    {
        IrBool => new IrBoolValue(value.IsTrue),
        IrBitVec bitVec => new IrBitVecValue(bitVec.Width, ((BitVecNum)value).UInt64),
        IrSort sort => Element(sort.Name, value),
        _ => DecodeMap(value, (IrMap)type),
    };

    /// <summary>The Z3 term for a decoded or literal value, so the oracle can apply call functions to it.</summary>
    public Expr Encode(IrValue value) => value switch
    {
        IrSortValue element => elements[element],
        IrMapValue map => map.Entries.Aggregate(
            context.MkConstArray(encoding.Sorts.Sort(map.MapType.Key), Encode(map.Default)),
            (array, e) => context.MkStore(array, Encode(e.Key), Encode(e.Value))),
        _ => encoding.Sorts.Literal(value),
    };

    public ModelOracle Oracle(Side side) => new(this, side);

    private static IrRun Run(IrProcedure procedure, IrInputs inputs, ICallOracle oracle) =>
        IrInterpreter.Run(procedure, inputs, oracle, procedure.Blocks.Sum(static b => b.Instructions.Length + 1), IsAbstraction);

    /// <summary>The tainting identities <paramref name="run"/> reached, as abstractions of <paramref name="side"/>; an <see cref="IrCall"/> has no span.</summary>
    private static IEnumerable<Abstraction> Abstractions(Codebase side, IrRun run) =>
        run.Taint.Sources.Select(s => new Abstraction(side, s, Span: null));

    /// <summary>One side's arguments, in its own parameter order, from the shared inputs it binds.</summary>
    private static IrInputs Bind(IrProcedure procedure, ImmutableArray<SharedParameter> shared, IrInputs inputs, Func<SharedParameter, IrParameter?> side)
    {
        Dictionary<string, IrValue> byName = shared
            .Select((s, i) => (Parameter: side(s), Value: inputs.Arguments[i]))
            .Where(static s => s.Parameter is not null)
            .ToDictionary(static s => s.Parameter!.Var.Name, static s => s.Value, StringComparer.Ordinal);
        return new IrInputs([.. procedure.Parameters.Select(p => byName[p.Var.Name])]);
    }

    private static int OutIndex(IrProcedure procedure, IrParameter? parameter) =>
        procedure.Parameters
            .Where(static p => p.Kind != IrParameterKind.In)
            .ToList()
            .FindIndex(p => p == parameter);

    private static string Describe(IrRun run) =>
        $"{run.Outcome} outs [{string.Join(", ", run.Outs)}] trace [{string.Join(", ", run.Trace.Select(static c => $"{c.Callee.Value}({string.Join(", ", c.Arguments)})"))}]";

    private IrSortValue Element(string sort, Expr value)
    {
        string key = value.ToString();
        if (ids.TryGetValue(sort, out Dictionary<string, int>? known) && known.TryGetValue(key, out int id))
        {
            return new IrSortValue(sort, id);
        }

        IrSortValue element = new(sort, elements.Keys.Where(e => string.Equals(e.Sort, sort, StringComparison.Ordinal)).Select(static e => e.Id + 1).DefaultIfEmpty(0).Max());
        Remember(element, value);
        return element;
    }

    private void Remember(IrSortValue element, Expr value)
    {
        if (!ids.TryGetValue(element.Sort, out Dictionary<string, int>? known))
        {
            known = new(StringComparer.Ordinal);
            ids.Add(element.Sort, known);
        }

        known[value.ToString()] = element.Id;
        elements[element] = value;
    }

    /// <summary>
    /// Z3 4.12 evaluates a model array, with completion, to a store chain over a constant array; any other
    /// shape (an <c>as-array</c>, a lambda) fails loudly, naming the term, rather than decoding to a wrong map.
    /// </summary>
    private IrMapValue DecodeMap(Expr value, IrMap type) => value switch
    {
        { IsStore: true } => DecodeMap(value.Args[0], type).Write(Decode(value.Args[1], type.Key), Decode(value.Args[2], type.Value)),
        { IsConstantArray: true } => new IrMapValue(type, Decode(value.Args[0], type.Value), []),
        _ => throw new InvalidOperationException($"Encoder bug: the model gives a map in a shape the decoder does not read (store chain over a constant array expected): {value}"),
    };

    /// <summary>
    /// Answers a call from the model: the side's result, <c>threw</c> and heap functions applied to the arguments, the
    /// position and the heap at the call (ticket P1-005). The heap at the call is the slice the interpreter passes for a map
    /// the call pairs, else <see cref="Threaded"/>'s version, which starts at the shared input and takes each call's new
    /// version, as the encoder threads it. A map the call pairs that the encoding's heap does not range over (a replay of
    /// the original procedures from a fragment's model) is left as it is.
    /// </summary>
    internal sealed class ModelOracle : ICallOracle
    {
        private readonly ModelDecoder decoder;
        private readonly Side side;
        private readonly IrValue[] threaded;
        private readonly List<ImmutableArray<IrHeapSlice>> reads = [];

        public ModelOracle(ModelDecoder decoder, Side side)
        {
            this.decoder = decoder;
            this.side = side;
            threaded = [.. Heap.Select(m => decoder.Decode(decoder.model.Eval(decoder.encoding.Inputs.First(i => Is(m, i.Shared.Var)).Term, completion: true), m.Type))];
        }

        /// <summary>The version of each heap map threaded through this side's calls so far.</summary>
        public IReadOnlyDictionary<HeapMap, IrValue> Threaded => Heap.Zip(threaded).ToDictionary(static e => e.First, static e => e.Second);

        private ImmutableArray<HeapMap> Heap => decoder.encoding.Calls.Heap;

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap)
        {
            ImmutableArray<IrType> types = [.. arguments.Select(static a => a.Type)];
            IrValue[] read = [.. Heap.Select((m, i) => heap.FirstOrDefault(h => Is(m, h)) is { } slice ? slice.Value : threaded[i])];
            Expr[] applied = [.. arguments.Select(decoder.Encode), decoder.context.MkBV(position, 32), .. read.Select(decoder.Encode)];
            TraceEncoder calls = decoder.encoding.Calls;
            IrValue? value = resultType is null
                ? null
                : decoder.Decode(decoder.model.Eval(decoder.context.MkApp(calls.ResultFunction(side, callee, types, resultType), applied), completion: true), resultType);
            bool threw = decoder.model.Eval(decoder.context.MkApp(calls.ThrewFunction(side, callee, types), applied), completion: true).IsTrue;
            for (int i = 0; i < threaded.Length; i++)
            {
                threaded[i] = decoder.Decode(decoder.model.Eval(decoder.context.MkApp(calls.HeapFunction(side, callee, types, i), applied), completion: true), Heap[i].Type);
            }

            reads.Add([.. Heap.Select((m, i) => new IrHeapSlice(m.Name, read[i]))]);
            return new IrCallResult(value, threw)
            {
                Heap = [.. heap.Select(h => Heap.ToList().FindIndex(m => Is(m, h)) is var i and >= 0 ? threaded[i] : h.Value)],
            };
        }

        /// <summary>
        /// <paramref name="run"/>, which this oracle answered, with each call event's heap completed to every map the encoding's
        /// heap ranges over, as the encoder's events are. A run that reached an abstraction may have threaded a tainted version
        /// into any event, so then every event is tainted.
        /// </summary>
        public IrRun Complete(IrRun run)
        {
            IrRun completed = run with { Trace = [.. run.Trace.Select((r, i) => r with { Heap = reads[i] })] };
            return Heap.IsEmpty || !Replayed.ThreadsTaint(run)
                ? completed
                : completed with { Taint = run.Taint with { Trace = [.. Enumerable.Range(0, run.Trace.Length)] } };
        }

        private static bool Is(HeapMap map, IrVar var) => string.Equals(map.Name, var.Name, StringComparison.Ordinal) && map.Type == var.Type;

        private static bool Is(HeapMap map, IrHeapSlice slice) => string.Equals(map.Name, slice.Map, StringComparison.Ordinal) && map.Type == slice.Value.Type;
    }

    /// <summary>One replayed side: its procedure, its run and the heap versions its oracle threaded (ticket P1-005).</summary>
    private sealed record Replayed(IrProcedure Procedure, IrRun Run, IReadOnlyDictionary<HeapMap, IrValue> Threaded)
    {
        /// <summary>
        /// Whether a version threaded through <paramref name="run"/>'s calls may depend on an abstraction: once the run has
        /// reached one, since every taint, a tainted branch's included, starts at one.
        /// </summary>
        public static bool ThreadsTaint(IrRun run) => !run.Taint.Sources.IsEmpty;

        /// <summary>
        /// The final value of <paramref name="parameter"/> (the shared input <paramref name="shared"/>): its out when it is by-ref
        /// on this side, else its threaded version, else <paramref name="input"/>.
        /// </summary>
        public IrValue Final(IrParameter? parameter, IrVar shared, IrValue input)
        {
            int index = OutIndex(Procedure, parameter);
            return index >= 0 ? Run.Outs[index] : Threaded.GetValueOrDefault(new HeapMap(shared.Name, shared.Type), input);
        }

        /// <summary>Whether that final value is tainted; the unchanged input of a side without it never is.</summary>
        public bool FinalTainted(IrParameter? parameter, IrVar shared)
        {
            int index = OutIndex(Procedure, parameter);
            return index >= 0 ? Run.OutTainted(index) : Threaded.ContainsKey(new HeapMap(shared.Name, shared.Type)) && ThreadsTaint(Run);
        }
    }
}
