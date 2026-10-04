using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;

namespace LoopAlignmentSpike;

/// <summary>
/// One run of one side, cut at its header visits: <see cref="Visits"/> is the loop (its pre-order index) of each header
/// visit in order, and <see cref="Stretches"/> the call events before the first visit, between each two, and after the
/// last, so it is one longer. <see cref="End"/> is null when the run returned or threw, else why it is not usable.
/// </summary>
internal sealed record Trace(string? End, List<int> Visits, List<List<IrCallRecord>> Stretches, IrRun Run);

/// <summary>
/// One side with a marker call at the top of every loop header, so that the interpreter's own call trace says where each
/// header visit falls among the call events. <see cref="Parents"/> is the loop forest: each loop's parent index, or -1.
/// </summary>
internal sealed class Side
{
    public const string Marker = "spike:header:";

    /// <summary>The steps one run may take.</summary>
    private const int Budget = 20_000;

    private readonly IrProcedure instrumented;

    public Side(IrProcedure procedure, IrLoopAnalysis shape)
    {
        List<IrBlockId> headers = [.. shape.Loops.Select(static l => l.Header)];
        Parents = [.. shape.Loops.Select(l => l.Parent is null ? -1 : headers.IndexOf(l.Parent))];
        instrumented = procedure with
        {
            Blocks = [.. procedure.Blocks.Select(b => headers.IndexOf(b.Id) is int k and >= 0 ? b with { Instructions = Mark(b.Instructions, k) } : b)],
        };
    }

    public ImmutableArray<int> Parents { get; }

    public Trace Run(ImmutableArray<IrValue> inputs, Oracle oracle)
    {
        IrRun run = IrInterpreter.Run(instrumented, new IrInputs(inputs), oracle, Budget, taint: null, pure: oracle);
        List<int> visits = [];
        List<List<IrCallRecord>> stretches = [[]];
        foreach (IrCallRecord record in run.Trace)
        {
            if (record.Callee.Value.StartsWith(Marker, StringComparison.Ordinal))
            {
                visits.Add(int.Parse(record.Callee.Value.AsSpan(Marker.Length), CultureInfo.InvariantCulture));
                stretches.Add([]);
            }
            else
            {
                stretches[^1].Add(record);
            }
        }

        string? end = run.Outcome switch
        {
            IrReturned or IrThrew => null,
            IrOpaqueReached => "opaque",
            IrBudgetExhausted => "budget",
            _ => "infeasible",
        };
        return new Trace(end, visits, stretches, run);
    }

    /// <summary>The marker goes after the phis, which must stay first in their block.</summary>
    private static ImmutableArray<IrInstruction> Mark(ImmutableArray<IrInstruction> instructions, int loop)
    {
        int phis = instructions.TakeWhile(static i => i is IrPhi).Count();
        return instructions.Insert(phis, new IrCall(Target: null, Threw: null, new CallIdentity(Marker + loop.ToString(CultureInfo.InvariantCulture)), []));
    }
}

/// <summary>
/// Answers calls and pure functions for both sides of one run. A call's answer depends on the callee and on how many real
/// calls the side made before it (the markers do not count), so two sides that make the same calls get the same answers.
/// A pure function's answer depends on the function and its arguments alone. One instance per side per run.
/// </summary>
internal sealed class Oracle(ulong seed, int trueInFour) : ICallOracle, IPureOracle
{
    private ulong calls;

    public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
    {
        if (callee.Value.StartsWith(Side.Marker, StringComparison.Ordinal))
        {
            return new IrCallResult(Value: null, Threw: false);
        }

        Rng rng = new(seed ^ Rng.Hash(callee.Value) ^ (++calls * 0x9E3779B97F4A7C15), trueInFour);
        IrValue? value = resultType is null ? null : rng.Value(resultType);
        return new IrCallResult(value, Threw: rng.Next() % 16 == 0) { RefOuts = [.. refOuts.Select(rng.Value)] };
    }

    public IrPureResult Answer(IrPure pure, ImmutableArray<IrValue> arguments)
    {
        Rng rng = new(arguments.Aggregate(seed ^ Rng.Hash(pure.Function), static (h, a) => (h * 31) + Rng.Hash(a)), trueInFour: 2);
        IrValue value = rng.Value(pure.Target.Type);
        return new IrPureResult(value, [.. pure.Throws.Select(_ => rng.Next() % 16 == 0)]);
    }
}

/// <summary>SplitMix64 and the proposer's value ranges (<c>TraceInvariantProposer</c>): a bitvector in [-4, 20], one of three elements of a sort, a map with a few entries.</summary>
internal sealed class Rng(ulong seed, int trueInFour)
{
    private ulong state = seed;

    public IrValue Value(IrType type) => type switch
    {
        IrBitVec bits => IrBitVecValue.FromSigned(bits.Width, (long)(Next() % 25) - 4),
        IrBool => new IrBoolValue(Next() % 4 < (ulong)trueInFour),
        IrSort sort => new IrSortValue(sort.Name, (int)(Next() % 3)),
        _ => Map((IrMap)type),
    };

    public ulong Next()
    {
        state += 0x9E3779B97F4A7C15;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    /// <summary>FNV-1a: the same in every process, which <c>string.GetHashCode</c> is not.</summary>
    public static ulong Hash(string text) => text.Aggregate(14695981039346656037UL, static (h, c) => (h ^ c) * 1099511628211UL);

    /// <summary>A hash that two equal values share in every process; a map entry that holds the default does not count.</summary>
    public static ulong Hash(IrValue value) => value switch
    {
        IrBitVecValue bits => (bits.Bits * 1099511628211UL) + (ulong)bits.Width,
        IrBoolValue truth => truth.Value ? 3UL : 5UL,
        IrSortValue element => Hash(element.Sort) + (ulong)element.Id,
        IrMapValue map => map.Entries.Where(e => !e.Value.Equals(map.Default)).Aggregate(Hash(map.Default), static (h, e) => h + (((Hash(e.Key) * 31) + Hash(e.Value)) * 0xBF58476D1CE4E5B9)),
        _ => 0,
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
}
