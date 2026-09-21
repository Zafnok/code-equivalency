using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// The replay check of <see cref="ModelDecoder"/>: which run pairs count as diverging, and the loud failure
/// when a model's replay does not diverge (an encoder bug, never a Divergent verdict).
/// </summary>
public sealed class ModelDecoderTests
{
    private static readonly IrProcedure WithHeap = IrText.Parse("""
        proc "T::M(int)" (%a: bv32, ref %field.C.x: map<bv32, bv32>) entry B0
        B0:
          ret outs(%field.C.x = %field.C.x)
        """);

    private static readonly IrProcedure WithoutHeap = IrText.Parse("""
        proc "T::M(int)" (%a: bv32) entry B0
        B0:
          ret
        """);

    private static readonly IrMapValue Heap = new(new IrMap(new IrBitVec(32), new IrBitVec(32)), Bv(0), []);

    private static readonly Dictionary<string, IrValue> Inputs = new(StringComparer.Ordinal)
    {
        ["a"] = Bv(1),
        ["field.C.x"] = Heap,
    };

    [Fact]
    public void RunsWithTheSameOutcomeTraceAndFinalHeapDoNotDiverge()
    {
        using Context context = new();

        Assert.False(ModelDecoder.Diverges(WithHeap, WithoutHeap, Inputs, Returned([Heap]), Returned([]), Calls(context)));
    }

    [Fact]
    public void AOneSidedHeapThatChangedDiverges()
    {
        using Context context = new();

        Assert.True(ModelDecoder.Diverges(WithHeap, WithoutHeap, Inputs, Returned([Heap.Write(Bv(1), Bv(2))]), Returned([]), Calls(context)));
        Assert.True(ModelDecoder.Diverges(WithoutHeap, WithHeap, Inputs, Returned([]), Returned([Heap.Write(Bv(1), Bv(2))]), Calls(context)));
    }

    [Fact]
    public void ADifferentOutcomeDiverges()
    {
        using Context context = new();
        IrRun threw = new(new IrThrew("System.Exception"), [], []);

        Assert.True(ModelDecoder.Diverges(WithoutHeap, WithoutHeap, Inputs, Returned([]), threw, Calls(context)));
    }

    [Fact]
    public void TracesAreComparedAfterRenamingLegacyCallees()
    {
        using Context context = new();
        TraceEncoder calls = Calls(context, ImmutableDictionary<string, string>.Empty.Add("Old::F", "New::F"));

        Assert.False(ModelDecoder.Diverges(WithoutHeap, WithoutHeap, Inputs, Traced("Old::F"), Traced("New::F"), calls));
        Assert.True(ModelDecoder.Diverges(WithoutHeap, WithoutHeap, Inputs, Traced("New::F"), Traced("Old::F"), calls));
        Assert.True(ModelDecoder.Diverges(WithoutHeap, WithoutHeap, Inputs, Traced("Old::F"), Traced("Other::F"), calls));
    }

    [Fact]
    public void AReplayThatDoesNotDivergeIsAnEncoderBug()
    {
        using Context context = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.EnsureDiverges(WithoutHeap, WithoutHeap, Inputs, Traced("F"), Traced("F"), Calls(context)));

        Assert.StartsWith("Encoder bug: the solver found a divergence between T::M(int) and T::M(int), but the replay does not diverge.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("a=IrBitVecValue", exception.Message, StringComparison.Ordinal);
        Assert.Contains("trace [F(IrBitVecValue", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADivergingReplayPasses()
    {
        using Context context = new();

        ModelDecoder.EnsureDiverges(WithoutHeap, WithoutHeap, Inputs, Traced("F"), Traced("G"), Calls(context));
    }

    private static IrBitVecValue Bv(ulong bits) => new(32, bits);

    private static IrRun Returned(ImmutableArray<IrValue> outs) => new(new IrReturned(null), outs, []);

    private static IrRun Traced(string callee) => new(new IrReturned(null), [], [new IrCallRecord(new CallIdentity(callee), [Bv(1)])]);

    private static TraceEncoder Calls(Context context, ImmutableDictionary<string, string>? map = null) =>
        new(new SortMapper(context), [], map ?? []);
}
