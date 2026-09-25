using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// Turns a satisfying model of the product query into a <see cref="Counterexample"/> (ticket M3-001). Inputs
/// are read with model completion. An element of an uninterpreted sort becomes <c>sort "S" n</c>: a literal
/// keeps its own id, any other element gets the next free id. Both sides are then replayed in
/// <see cref="IrInterpreter"/>, with a call oracle that answers from the model's call functions at the
/// position the interpreter passes, and pure functions from the model's pure functions, and the replay must diverge on the
/// observables the encoder compares. The replay taints every call whose identity starts with <see cref="OpaquePrefix"/>
/// and every pure function (ADR 0026; tickets M3-016 and M4-002): only a difference in an observable that is untainted on
/// both sides is real.
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
        IrRun oldRun = decoder.Run(old, Bind(old, shared, inputs, static s => s.Old), Side.Old, Budget(old));
        IrRun newRun = decoder.Run(@new, Bind(@new, shared, inputs, static s => s.New), Side.New, Budget(@new));
        Counterexample counterexample = new(inputs, oldRun, newRun);
        return EnsureDiverges(old, @new, shared, inputs, oldRun, newRun, encoding.Calls) == Difference.Real
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
        IrRun oldRun = decoder.Run(old, Bind(old, shared, inputs, static s => s.Old), Side.Old, stepBudget);
        IrRun newRun = decoder.Run(@new, Bind(@new, shared, inputs, static s => s.New), Side.New, stepBudget);
        bool complete = new[] { oldRun, newRun }.All(static r => r.Outcome is IrReturned or IrThrew);
        return complete && Diverges(old, @new, shared, inputs, oldRun, newRun, encoding.Calls) ? new Counterexample(inputs, oldRun, newRun) : null;
    }

    /// <summary>
    /// A model whose replay agrees on every compared observable is an encoder bug: fail loudly, never report it as
    /// Divergent. Otherwise returns how the runs differ.
    /// </summary>
    public static Difference EnsureDiverges(IrProcedure old, IrProcedure @new, ImmutableArray<SharedParameter> shared, IrInputs inputs, IrRun oldRun, IrRun newRun, TraceEncoder calls)
    {
        Difference difference = Compare(old, @new, shared, inputs, oldRun, newRun, calls);
        return difference != Difference.None
            ? difference
            : throw new InvalidOperationException(
                $"Encoder bug: the solver found a divergence between {old.Identity.Value} and {@new.Identity.Value}, but the replay does not diverge. "
                + $"Inputs: {string.Join(", ", shared.Select((s, i) => $"{s.Var.Name}={inputs.Arguments[i]}"))}. Old: {Describe(oldRun)}. New: {Describe(newRun)}.");
    }

    /// <summary>True when the two runs differ in an observable the encoder compares that is untainted on both sides.</summary>
    public static bool Diverges(IrProcedure old, IrProcedure @new, ImmutableArray<SharedParameter> shared, IrInputs inputs, IrRun oldRun, IrRun newRun, TraceEncoder calls) =>
        Compare(old, @new, shared, inputs, oldRun, newRun, calls) == Difference.Real;

    /// <summary>
    /// How the two runs differ on the observables the encoder compares: outcome, call trace (legacy identities renamed
    /// through the call-identity map), and the final value of each by-ref shared input (<paramref name="shared"/>,
    /// valued by <paramref name="inputs"/>), a side without that parameter standing for its unchanged input. A difference
    /// is real when some differing observable is untainted on both sides (ADR 0026). Two returned values compare by the
    /// values' taint, any other outcome difference by the path's. The trace compares its first differing event only,
    /// since control taint can shift every later position.
    /// </summary>
    public static Difference Compare(IrProcedure old, IrProcedure @new, ImmutableArray<SharedParameter> shared, IrInputs inputs, IrRun oldRun, IrRun newRun, TraceEncoder calls)
    {
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
            .Where(s => s.Shared.ByRef && Final(old, oldRun, s.Shared.Old, s.Input) != Final(@new, newRun, s.Shared.New, s.Input))
            .Select(s => FinalTainted(old, oldRun, s.Shared.Old) || FinalTainted(@new, newRun, s.Shared.New)));

        return tainted switch
        {
            [] => Difference.None,
            _ when tainted.Contains(false) => Difference.Real,
            _ => Difference.Abstract,
        };
    }

    /// <summary>
    /// The shared inputs, in <see cref="ProductEncoding.Inputs"/> order. A <c>length.&lt;Sort&gt;</c> input gives 0 wherever
    /// the model gives a negative length (ticket P2-019): the encoder assumes every length it reads is non-negative, so a
    /// negative one is at a reference nothing reads, and 0 makes the input one a CLR caller can pass.
    /// </summary>
    public IrInputs Inputs() =>
        new([.. encoding.Inputs.Select(i => (i.Shared.Var.Name, Value: Decode(model.Eval(i.Term, completion: true), i.Shared.Type)))
            .Select(static i => i.Name.StartsWith(ProductEncoder.LengthPrefix, StringComparison.Ordinal) ? NonNegative((IrMapValue)i.Value) : i.Value)]);

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

    public ICallOracle Oracle(Side side) => new ModelOracle(this, side);

    /// <summary>Answers <see cref="IrPure"/> applications from the model: the side's result and flag functions applied to the arguments.</summary>
    public IPureOracle Pure(Side side) => new ModelOracle(this, side);

    /// <summary>Replays <paramref name="procedure"/> on <paramref name="side"/> from the model, with taint (ADR 0026).</summary>
    private IrRun Run(IrProcedure procedure, IrInputs inputs, Side side, int stepBudget) =>
        IrInterpreter.Run(procedure, inputs, Oracle(side), stepBudget, IsAbstraction, Pure(side));

    /// <summary><paramref name="lengths"/> with every negative length replaced by 0.</summary>
    private static IrMapValue NonNegative(IrMapValue lengths) =>
        new(lengths.MapType, NonNegative(lengths.Default), lengths.Entries.ToImmutableDictionary(static e => e.Key, static e => NonNegative(e.Value)));

    private static IrValue NonNegative(IrValue length) =>
        ((IrBitVecValue)length).TwosComplement < 0 ? new IrBitVecValue(32, 0) : length;

    /// <summary>Enough steps to run an acyclic procedure once through.</summary>
    private static int Budget(IrProcedure procedure) => procedure.Blocks.Sum(static b => b.Instructions.Length + 1);

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

    /// <summary>The final value of <paramref name="parameter"/> in <paramref name="run"/>, else (not by-ref on this side, or absent) <paramref name="input"/>.</summary>
    private static IrValue Final(IrProcedure procedure, IrRun run, IrParameter? parameter, IrValue input)
    {
        int index = OutIndex(procedure, parameter);
        return index < 0 ? input : run.Outs[index];
    }

    /// <summary>Whether the final value of <paramref name="parameter"/> is tainted; the unchanged input of a side without it never is.</summary>
    private static bool FinalTainted(IrProcedure procedure, IrRun run, IrParameter? parameter)
    {
        int index = OutIndex(procedure, parameter);
        return index >= 0 && run.OutTainted(index);
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
    /// Answers a call from the model, the side's result and <c>threw</c> functions applied to the arguments and position,
    /// and a pure function from its result and flag functions applied to the arguments.
    /// </summary>
    private sealed class ModelOracle(ModelDecoder decoder, Side side) : ICallOracle, IPureOracle
    {
        public IrPureResult Answer(IrPure pure, ImmutableArray<IrValue> arguments)
        {
            Expr[] applied = [.. arguments.Select(decoder.Encode)];
            PureEncoder pures = decoder.encoding.Pures;
            IrValue value = decoder.Decode(decoder.model.Eval(decoder.context.MkApp(pures.ResultFunction(side, pure), applied), completion: true), pure.Target.Type);
            return new IrPureResult(
                value,
                [.. pure.Throws.Select(t => decoder.model.Eval(decoder.context.MkApp(pures.ThrewFunction(side, pure, t.ExceptionType), applied), completion: true).IsTrue)]);
        }

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position)
        {
            ImmutableArray<IrType> types = [.. arguments.Select(static a => a.Type)];
            Expr[] applied = [.. arguments.Select(decoder.Encode), decoder.context.MkBV(position, 32)];
            TraceEncoder calls = decoder.encoding.Calls;
            IrValue? value = resultType is null
                ? null
                : decoder.Decode(decoder.model.Eval(decoder.context.MkApp(calls.ResultFunction(side, callee, types, resultType), applied), completion: true), resultType);
            bool threw = decoder.model.Eval(decoder.context.MkApp(calls.ThrewFunction(side, callee, types), applied), completion: true).IsTrue;
            return new IrCallResult(value, threw);
        }
    }
}
