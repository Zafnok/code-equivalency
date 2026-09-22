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
/// position the interpreter passes, and the replay must diverge on the observables the encoder compares.
/// </summary>
internal sealed class ModelDecoder
{
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

    /// <summary>Decodes the model, replays both sides, and fails loudly if the replay does not diverge.</summary>
    public static Counterexample Replay(Context context, Model model, ProductEncoding encoding, IrProcedure old, IrProcedure @new)
    {
        ModelDecoder decoder = new(context, model, encoding);
        IrInputs inputs = decoder.Inputs();
        ImmutableArray<SharedParameter> shared = [.. encoding.Inputs.Select(static i => i.Shared)];
        IrRun oldRun = Run(old, Bind(old, shared, inputs, static s => s.Old), decoder.Oracle(Side.Old));
        IrRun newRun = Run(@new, Bind(@new, shared, inputs, static s => s.New), decoder.Oracle(Side.New));
        EnsureDiverges(old, @new, shared, inputs, oldRun, newRun, encoding.Calls);
        return new Counterexample(inputs, oldRun, newRun);
    }

    /// <summary>A model whose replay does not diverge is an encoder bug: fail loudly, never report it as Divergent.</summary>
    public static void EnsureDiverges(IrProcedure old, IrProcedure @new, ImmutableArray<SharedParameter> shared, IrInputs inputs, IrRun oldRun, IrRun newRun, TraceEncoder calls)
    {
        if (!Diverges(old, @new, shared, inputs, oldRun, newRun, calls))
        {
            throw new InvalidOperationException(
                $"Encoder bug: the solver found a divergence between {old.Identity.Value} and {@new.Identity.Value}, but the replay does not diverge. "
                + $"Inputs: {string.Join(", ", shared.Select((s, i) => $"{s.Var.Name}={inputs.Arguments[i]}"))}. Old: {Describe(oldRun)}. New: {Describe(newRun)}.");
        }
    }

    /// <summary>
    /// True when the two runs differ on an observable the encoder compares: outcome, call trace (legacy
    /// identities renamed through the call-identity map), or the final value of a by-ref shared input
    /// (<paramref name="shared"/>, valued by <paramref name="inputs"/>), a side without that parameter
    /// standing for its unchanged input.
    /// </summary>
    public static bool Diverges(IrProcedure old, IrProcedure @new, ImmutableArray<SharedParameter> shared, IrInputs inputs, IrRun oldRun, IrRun newRun, TraceEncoder calls)
    {
        if (oldRun.Outcome != newRun.Outcome)
        {
            return true;
        }

        IEnumerable<IrCallRecord> oldTrace = oldRun.Trace.Select(r => r with { Callee = new CallIdentity(calls.Canonical(Side.Old, r.Callee)) });
        IEnumerable<IrCallRecord> newTrace = newRun.Trace.Select(r => r with { Callee = new CallIdentity(r.Callee.Value) });
        return !oldTrace.SequenceEqual(newTrace)
            ? true
            : shared
            .Select((s, i) => (Shared: s, Input: inputs.Arguments[i]))
            .Where(static s => s.Shared.ByRef)
            .Any(s => Final(old, oldRun, s.Shared.Old, s.Input) != Final(@new, newRun, s.Shared.New, s.Input));
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

    public ICallOracle Oracle(Side side) => new ModelOracle(this, side);

    private static IrRun Run(IrProcedure procedure, IrInputs inputs, ICallOracle oracle) =>
        IrInterpreter.Run(procedure, inputs, oracle, procedure.Blocks.Sum(static b => b.Instructions.Length + 1));

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
        int index = procedure.Parameters
            .Where(static p => p.Kind != IrParameterKind.In)
            .ToList()
            .FindIndex(p => p == parameter);
        return index < 0 ? input : run.Outs[index];
    }

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

    /// <summary>Answers a call from the model: the side's result and <c>threw</c> functions applied to the arguments and position.</summary>
    private sealed class ModelOracle(ModelDecoder decoder, Side side) : ICallOracle
    {
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
