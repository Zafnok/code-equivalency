using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;

using Conjunct = Equiv.Verify.Z3.Ladder.InvariantTemplates.Conjunct;
using Sample = System.Collections.Generic.IReadOnlyDictionary<string, object>;
using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3.Ladder;

/// <summary>
/// A proposer that needs no network (ticket P1-009; ADR 0036 decision 1): data-driven equivalence checking after Sharma,
/// Schkufza, Churchill and Aiken (OOPSLA 2013), with Daikon-style templates (<see cref="InvariantTemplates"/>). It runs
/// both procedures of the request in <see cref="IrInterpreter"/> on up to <see cref="MaxInputs"/> inputs, the inputs of
/// every counterexample Z3 returned first and then random ones, each side cut at its loop headers exactly as rung 4 cuts
/// it (<see cref="IrFragmenter.Segment"/>), so that each run is a sequence of cut points with the relation state at each.
/// It pairs the two sequences as rung 4's product steps them (both sides step when both return to their header or both
/// leave it, else the one that returns steps alone, and an exited side waits), which gives samples of each relation
/// <c>inv.&lt;a&gt;.&lt;b&gt;</c>. Each relation is defined as the conjunction of every template instance that held on all
/// its samples, <c>false</c> when no run reached it. After a rejection, each conjunct the counterexample's conclusion
/// falsifies is dropped. It gives up when nothing survives or when it would repeat a rejected candidate. The random
/// inputs are drawn from <paramref name="seed"/>, so the same request always gets the same candidate.
/// </summary>
internal sealed class TraceInvariantProposer(ulong seed = TraceInvariantProposer.DefaultSeed) : IInvariantProposer
{
    /// <summary>What <see cref="Core.Verdicts.Equivalent.ProposedBy"/> names this proposer.</summary>
    public const string Name = "trace";

    /// <summary>The most inputs the proposer runs both sides on.</summary>
    public const int MaxInputs = 200;

    /// <summary>The most cut points one run reaches before the proposer stops it; the samples up to there stand.</summary>
    public const int MaxCuts = 64;

    /// <summary>The steps one segment may take.</summary>
    private const int SegmentBudget = 10_000;

    /// <summary>The seed of the random inputs the ladder uses.</summary>
    public const ulong DefaultSeed = 0x9E3779B97F4A7C15;

    public Task<string?> ProposeAsync(InvariantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        IrProcedure old = IrText.Parse(request.OldIr);
        IrProcedure @new = IrText.Parse(request.NewIr);
        ImmutableArray<SharedParameter> shared = ProductEncoder.Pair(old, @new);
        Dictionary<string, long> exceptions = new(StringComparer.Ordinal);
        Runner oldRunner = new(old, Side.Old, exceptions);
        Runner newRunner = new(@new, Side.New, exceptions);
        List<(string Relation, IReadOnlyList<Sample> Samples)> traces = [];
        foreach (Dictionary<SharedParameter, IrValue> inputs in Inputs(request, shared, seed))
        {
            Dictionary<string, object> carried = shared.ToDictionary(static s => s.InputName, s => Scalar(inputs[s]), StringComparer.Ordinal);
            List<Point> oldRun = oldRunner.Run([.. old.Parameters.Select(p => inputs[shared.First(s => s.Old == p)])]);
            List<Point> newRun = newRunner.Run([.. @new.Parameters.Select(p => inputs[shared.First(s => s.New == p)])]);
            traces.AddRange(Product(oldRun, newRun, carried).GroupBy(static s => s.Relation, static s => s.Values, StringComparer.Ordinal).Select(static g => (g.Key, (IReadOnlyList<Sample>)[.. g])));
        }

        ILookup<string, IReadOnlyList<Sample>> byRelation = traces.ToLookup(static t => t.Relation, static t => t.Samples, StringComparer.Ordinal);
        Dictionary<string, List<Conjunct>> candidate = request.Relations.ToDictionary(static r => r.Name, r => InvariantTemplates.Mine(r.Parameters, [.. byRelation[r.Name]]).ToList(), StringComparer.Ordinal);
        foreach (InvariantRequest.Fact conclusion in request.Rejected.Select(static r => r.Conclusion).OfType<InvariantRequest.Fact>())
        {
            Dictionary<string, string> values = conclusion.Values.ToDictionary(static b => b.Name, static b => b.Value, StringComparer.Ordinal);
            candidate[conclusion.Relation].RemoveAll(c => c.Falsified(values));
        }

        string text = string.Join('\n', request.Relations.Select(r => Define(r, candidate[r.Name])));
        bool spent = candidate.Values.All(static c => c.Count == 0) || request.Rejected.Any(r => string.Equals(r.Candidate, text, StringComparison.Ordinal));
        return Task.FromResult(spent ? null : text);
    }

    /// <summary>A relation's <c>define-fun</c>: the conjunction of its instances, <c>true</c> for none.</summary>
    private static string Define(InvariantRequest.Relation relation, List<Conjunct> conjuncts)
    {
        string body = conjuncts switch
        {
            [] => "true",
            [Conjunct one] => one.Smt,
            _ => $"(and {string.Join(' ', conjuncts.Select(static c => c.Smt))})",
        };
        return $"(define-fun {relation.Name} ({string.Join(' ', relation.Parameters.Select(static p => $"({p.Name} {p.Sort})"))}) Bool {body})";
    }

    /// <summary>
    /// The inputs to run: those of each rejection's counterexample (its facts' <c>in.*</c> values), then enough random
    /// ones to reach <see cref="MaxInputs"/>. A value a fact does not give as an integer or Boolean is random.
    /// </summary>
    private static List<Dictionary<SharedParameter, IrValue>> Inputs(InvariantRequest request, ImmutableArray<SharedParameter> shared, ulong seed)
    {
        SplitMix random = new(seed);
        List<Dictionary<SharedParameter, IrValue>> inputs =
        [
            .. request.Rejected
                .SelectMany(static r => (InvariantRequest.Fact?[])[r.Premise, r.Conclusion])
                .OfType<InvariantRequest.Fact>()
                .Select(static f => f.Values.ToDictionary(static b => b.Name, static b => b.Value, StringComparer.Ordinal))
                .Select(values => shared.ToDictionary(static s => s, s => Given(s, values) ?? random.Value(s.Type)))
                .Take(MaxInputs),
        ];
        inputs.AddRange(Enumerable.Range(0, MaxInputs - inputs.Count).Select(_ => shared.ToDictionary(static s => s, s => random.Value(s.Type))));
        return inputs;
    }

    /// <summary>The value a counterexample gives the input <paramref name="input"/>, when it is an integer or a Boolean.</summary>
    private static IrValue? Given(SharedParameter input, Dictionary<string, string> values) =>
        (input.Type, values.TryGetValue(input.InputName, out string? text) ? InvariantTemplates.Read(text) : null) switch
        {
            (IrBitVec bits, long number) => IrBitVecValue.FromSigned(bits.Width, number),
            (IrBool, bool truth) => new IrBoolValue(truth),
            _ => null,
        };

    /// <summary>A value as a sample holds it: a bitvector as the integer it denotes read signed, a Boolean as itself, anything else as is.</summary>
    private static object Scalar(IrValue value) => value switch
    {
        IrBitVecValue bits => bits.TwosComplement,
        IrBoolValue truth => truth.Value,
        _ => value,
    };

    /// <summary>
    /// The samples of rung 4's relations along the product of two runs, each a sequence of cut points, as rung 4 steps it:
    /// from the first pair, the side that has exited waits; else both step when both return to their header or both leave
    /// it, and otherwise the side that returns steps alone. A side whose run was cut short at a header ends the product.
    /// </summary>
    private static IEnumerable<(string Relation, Sample Values)> Product(List<Point> old, List<Point> @new, Dictionary<string, object> carried)
    {
        int i = 0;
        int j = 0;
        while (i < old.Count && j < @new.Count)
        {
            Point a = old[i];
            Point b = @new[j];
            Dictionary<string, object> values = new(carried, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> value in a.Values.Concat(b.Values))
            {
                values[value.Key] = value.Value;
            }

            yield return ($"inv.{Label(a.Header)}.{Label(b.Header)}", values);
            if ((a.Header is not null && i + 1 == old.Count) || (b.Header is not null && j + 1 == @new.Count))
            {
                yield break;
            }

            (bool oldSteps, bool newSteps) = Steps(a.Header, b.Header, old.ElementAtOrDefault(i + 1), @new.ElementAtOrDefault(j + 1));
            i += oldSteps ? 1 : 0;
            j += newSteps ? 1 : 0;
        }
    }

    /// <summary>
    /// Which sides the product steps from cut points <paramref name="a"/> and <paramref name="b"/>, given each side's next
    /// cut point: a side that has exited waits; else both step when both return to their header or both leave it, and
    /// otherwise the one that returns steps alone.
    /// </summary>
    private static (bool Old, bool New) Steps(IrBlockId? a, IrBlockId? b, Point? oldNext, Point? newNext)
    {
        bool oldReturns = oldNext?.Header == a;
        bool newReturns = newNext?.Header == b;
        return (a, b) switch
        {
            (null, null) => (true, true),
            (null, _) => (false, true),
            (_, null) => (true, false),
            _ when oldReturns == newReturns => (true, true),
            _ => (oldReturns, newReturns),
        };
    }

    /// <summary>A cut point as rung 4's relations name it: <c>B&lt;n&gt;</c> for a header, <c>exit</c> for the exit.</summary>
    private static string Label(IrBlockId? point) => point is null ? "exit" : "B" + point.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A cut point one run reached: a header (null for the exit), and the relation state there by argument name.</summary>
    private sealed record Point(IrBlockId? Header, Dictionary<string, object> Values);

    /// <summary>
    /// One side, cut as rung 4 cuts it: its headers in pre-order, the state each carries without the parameters and the
    /// variables a constant defines, and a segment from the entry and from each header.
    /// </summary>
    private sealed class Runner
    {
        private readonly string prefix;
        private readonly Dictionary<string, long> exceptions;
        private readonly ImmutableArray<IrBlockId> headers;
        private readonly Dictionary<IrBlockId, ImmutableArray<IrVar>> states;
        private readonly HashSet<string> carried;
        private readonly IrProcedure entry;
        private readonly Dictionary<IrBlockId, IrProcedure> loops;
        private readonly ImmutableArray<IrParameter> byRef;

        public Runner(IrProcedure procedure, Side side, Dictionary<string, long> exceptions)
        {
            prefix = ProductEncoder.Prefix(side);
            this.exceptions = exceptions;
            foreach (IrThrow thrown in procedure.Blocks.Select(static b => b.Terminator).OfType<IrThrow>())
            {
                exceptions.TryAdd(thrown.ExceptionType, exceptions.Count + 1);
            }

            headers = [.. IrLoopAnalysis.Of(procedure).Loops.Select(static l => l.Header)];
            states = headers.ToDictionary(static h => h, h => IrFragmenter.State(procedure, h));
            carried = [.. procedure.Parameters.Select(static p => p.Var.Name), .. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>().Select(static c => c.Target.Name)];
            (IrBlockId, ImmutableArray<IrVar>)[] cuts = [.. headers.Select(h => (h, states[h]))];
            entry = IrFragmenter.Segment(procedure, start: null, cuts);
            loops = headers.ToDictionary(static h => h, h => IrFragmenter.Segment(procedure, h, cuts));
            byRef = [.. procedure.Parameters.Where(static p => p.Kind != IrParameterKind.In)];
        }

        /// <summary>
        /// The cut points a run on <paramref name="inputs"/> reaches, up to <see cref="MaxCuts"/>, ending with the exit when
        /// it returns or throws; a segment that runs out of steps or reaches an opaque node ends it there.
        /// </summary>
        public List<Point> Run(ImmutableArray<IrValue> inputs)
        {
            List<Point> points = [];
            IrProcedure segment = entry;
            ImmutableArray<IrValue> arguments = inputs;
            while (points.Count < MaxCuts)
            {
                IrRun run = IrInterpreter.Run(segment, new IrInputs(arguments), CutOracle.Instance, SegmentBudget);
                switch (run.Outcome)
                {
                    case IrThrew { ExceptionType: IrFragmenter.CutException }:
                        IrCallRecord cut = run.Trace[^1];
                        IrBlockId header = headers[int.Parse(cut.Callee.Value.AsSpan(IrFragmenter.CutEvent.Length), CultureInfo.InvariantCulture)];
                        points.Add(new Point(header, State(header, cut.Arguments)));
                        (segment, arguments) = (loops[header], [.. cut.Arguments, .. inputs]);
                        break;
                    case IrReturned done:
                        points.Add(new Point(Header: null, Exit(returned: true, done.Value, exception: 0, run.Outs)));
                        return points;
                    case IrThrew threw:
                        points.Add(new Point(Header: null, Exit(returned: false, value: null, exceptions[threw.ExceptionType], run.Outs)));
                        return points;
                    default:
                        return points;
                }
            }

            return points;
        }

        /// <summary>The relation state a cut event carries, named <c>old.&lt;variable&gt;</c> or <c>new.&lt;variable&gt;</c>.</summary>
        private Dictionary<string, object> State(IrBlockId header, ImmutableArray<IrValue> values) =>
            states[header]
                .Select((v, k) => (v, k))
                .Where(p => !carried.Contains(p.v.Name))
                .ToDictionary(p => $"{prefix}.{p.v.Name}", p => Scalar(values[p.k]), StringComparer.Ordinal);

        /// <summary>The exit state: whether it returned, the value (none on a throw or without a return type), the exception id and each by-ref final.</summary>
        private Dictionary<string, object> Exit(bool returned, IrValue? value, long exception, ImmutableArray<IrValue> outs)
        {
            Dictionary<string, object> exit = new(StringComparer.Ordinal)
            {
                [$"{prefix}.returned"] = returned,
                [$"{prefix}.exception"] = exception,
            };
            if (value is not null)
            {
                exit[$"{prefix}.value"] = Scalar(value);
            }

            for (int k = 0; k < byRef.Length; k++)
            {
                exit[$"{prefix}.{byRef[k].Var.Name}"] = Scalar(outs[k]);
            }

            return exit;
        }
    }

    /// <summary>Answers a segment's cut event, the only call a fragment rung 4 applies to makes.</summary>
    private sealed class CutOracle : ICallOracle
    {
        public static readonly CutOracle Instance = new();

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts) =>
            new(Value: null, Threw: false);
    }

    /// <summary>SplitMix64: a small deterministic generator for the random inputs.</summary>
    private sealed class SplitMix(ulong seed)
    {
        private ulong state = seed;

        /// <summary>A random value of <paramref name="type"/>: a bitvector in [-4, 20], a Boolean, one of three elements of a sort, a map with a few entries.</summary>
        public IrValue Value(IrType type) => type switch
        {
            IrBitVec bits => IrBitVecValue.FromSigned(bits.Width, (long)(Next() % 25) - 4),
            IrBool => new IrBoolValue(Next() % 2 == 0),
            IrSort sort => new IrSortValue(sort.Name, (int)(Next() % 3)),
            _ => Map((IrMap)type),
        };

        private IrMapValue Map(IrMap type)
        {
            ImmutableDictionary<IrValue, IrValue>.Builder entries = ImmutableDictionary.CreateBuilder<IrValue, IrValue>();
            for (ulong k = Next() % 4; k > 0; k--)
            {
                entries[Value(type.Key)] = Value(type.Value);
            }

            return new IrMapValue(type, Value(type.Value), entries.ToImmutable());
        }

        private ulong Next()
        {
            state += 0x9E3779B97F4A7C15;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }
}
