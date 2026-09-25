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

    private static readonly IrProcedure TwoParams = IrText.Parse("""
        proc "T::N(int,int)" (%a: bv32, %b: bv32) entry B0
        B0:
          ret
        """);

    private static readonly IrProcedure WithSortLiteral = IrText.Parse("""
        proc "T::S()" () -> sort "S" entry B0
        B0:
          %x: sort "S" = const sort "S" 5
          ret %x
        """);

    private static readonly IrMapValue Heap = new(new IrMap(new IrBitVec(32), new IrBitVec(32)), Bv(0), []);

    private static readonly IrInputs Inputs = new([Bv(1), Heap]);

    [Fact]
    public void RunsWithTheSameOutcomeTraceAndFinalHeapDoNotDiverge()
    {
        using Context context = new();

        Assert.False(ModelDecoder.Diverges(WithHeap, WithoutHeap, Shared(WithHeap, WithoutHeap), Inputs, Returned([Heap]), Returned([]), Calls(context)));
    }

    [Fact]
    public void AOneSidedHeapThatChangedDiverges()
    {
        using Context context = new();

        Assert.True(ModelDecoder.Diverges(WithHeap, WithoutHeap, Shared(WithHeap, WithoutHeap), Inputs, Returned([Heap.Write(Bv(1), Bv(2))]), Returned([]), Calls(context)));
        Assert.True(ModelDecoder.Diverges(WithoutHeap, WithHeap, Shared(WithoutHeap, WithHeap), Inputs, Returned([]), Returned([Heap.Write(Bv(1), Bv(2))]), Calls(context)));
    }

    [Fact]
    public void ADifferentOutcomeDiverges()
    {
        using Context context = new();
        IrRun threw = new(new IrThrew("System.Exception"), [], []);

        Assert.True(ModelDecoder.Diverges(WithoutHeap, WithoutHeap, Shared(WithoutHeap, WithoutHeap), Inputs, Returned([]), threw, Calls(context)));
    }

    [Fact]
    public void TracesAreComparedAfterRenamingLegacyCallees()
    {
        using Context context = new();
        TraceEncoder calls = Calls(context, ImmutableDictionary<string, string>.Empty.Add("Old::F", "New::F"));

        Assert.False(ModelDecoder.Diverges(WithoutHeap, WithoutHeap, Shared(WithoutHeap, WithoutHeap), Inputs, Traced("Old::F"), Traced("New::F"), calls));
        Assert.True(ModelDecoder.Diverges(WithoutHeap, WithoutHeap, Shared(WithoutHeap, WithoutHeap), Inputs, Traced("New::F"), Traced("Old::F"), calls));
        Assert.True(ModelDecoder.Diverges(WithoutHeap, WithoutHeap, Shared(WithoutHeap, WithoutHeap), Inputs, Traced("Old::F"), Traced("Other::F"), calls));
    }

    [Fact]
    public void AReplayThatDoesNotDivergeIsAnEncoderBug()
    {
        using Context context = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.EnsureDiverges(WithoutHeap, WithoutHeap, Shared(WithoutHeap, WithoutHeap), Inputs, Traced("F"), Traced("F"), Calls(context)));

        Assert.StartsWith("Encoder bug: the solver found a divergence between T::M(int) and T::M(int), but the replay does not diverge.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("a=IrBitVecValue", exception.Message, StringComparison.Ordinal);
        Assert.Contains("trace [F(IrBitVecValue", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADivergingReplayPasses()
    {
        using Context context = new();

        Exception? exception = Record.Exception(() =>
            ModelDecoder.EnsureDiverges(WithoutHeap, WithoutHeap, Shared(WithoutHeap, WithoutHeap), Inputs, Traced("F"), Traced("G"), Calls(context)));

        Assert.Null(exception);
    }

    [Fact]
    public void TheEncoderBugMessageListsEachInputSeparatedByAComma()
    {
        using Context context = new();
        ImmutableArray<ProductEncoder.SharedParameter> shared = Shared(WithHeap, WithHeap);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.EnsureDiverges(WithHeap, WithHeap, shared, Inputs, Returned([Heap]), Returned([Heap]), Calls(context)));

        string joined = string.Join(", ", shared.Select((s, i) => $"{s.Var.Name}={Inputs.Arguments[i]}"));
        Assert.Contains($"Inputs: {joined}.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEncoderBugMessageDescribesMultipleOutsAndTracedCallsWithCommaSeparators()
    {
        using Context context = new();
        IrRun run = new(
            new IrReturned(Value: null),
            [Bv(1), Bv(2)],
            [
                new IrCallRecord(new CallIdentity("F"), [Bv(1), Bv(2)]),
                new IrCallRecord(new CallIdentity("G"), [Bv(3)]),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.EnsureDiverges(WithoutHeap, WithoutHeap, Shared(WithoutHeap, WithoutHeap), Inputs, run, run, Calls(context)));

        Assert.Contains($"Old: {ExpectedDescribe(run)}.", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"New: {ExpectedDescribe(run)}.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADecodedValueEqualToAKnownSortLiteralReusesItsId()
    {
        using Context context = new();
        ProductEncoder.ProductEncoding encoding = ProductEncoder.Encode(context, WithoutHeap, WithoutHeap, []);
        encoding.Sorts.Literal(new IrSortValue("S", 0));
        Expr lit5 = encoding.Sorts.Literal(new IrSortValue("S", 5));
        Expr fresh = context.MkConst("fresh", encoding.Sorts.Sort(new IrSort("S")));

        using Solver solver = context.MkSolver();
        solver.Assert(context.MkEq(fresh, lit5));
        Assert.Equal(Status.SATISFIABLE, solver.Check());
        ModelDecoder decoder = new(context, solver.Model, encoding);

        IrValue decoded = decoder.Decode(solver.Model.Eval(fresh, completion: true), new IrSort("S"));

        Assert.Equal(new IrSortValue("S", 5), decoded);
    }

    [Fact]
    public void ADecodedValueDistinctFromKnownLiteralsGetsTheNextFreeId()
    {
        using Context context = new();
        ProductEncoder.ProductEncoding encoding = ProductEncoder.Encode(context, WithoutHeap, WithoutHeap, []);
        Expr lit0 = encoding.Sorts.Literal(new IrSortValue("S", 0));
        Expr lit5 = encoding.Sorts.Literal(new IrSortValue("S", 5));
        Expr fresh = context.MkConst("fresh", encoding.Sorts.Sort(new IrSort("S")));

        using Solver solver = context.MkSolver();
        solver.Assert(context.MkDistinct(fresh, lit0, lit5));
        Assert.Equal(Status.SATISFIABLE, solver.Check());
        ModelDecoder decoder = new(context, solver.Model, encoding);

        IrValue decoded = decoder.Decode(solver.Model.Eval(fresh, completion: true), new IrSort("S"));

        Assert.Equal(new IrSortValue("S", 6), decoded);
    }

    /// <summary>
    /// Ticket P1-005: replaying the original procedures from a fragment's model can reach a call that pairs a map the
    /// fragment's encoding has no heap function for; the oracle then leaves that map as it is.
    /// </summary>
    [Fact]
    public void AnOracleLeavesAMapTheEncodingDoesNotRangeOverUnchanged()
    {
        using Context context = new();
        ProductEncoder.ProductEncoding encoding = ProductEncoder.Encode(context, WithoutHeap, WithoutHeap, []);
        using Solver solver = context.MkSolver();
        Assert.Equal(Status.SATISFIABLE, solver.Check());
        ModelDecoder decoder = new(context, solver.Model, encoding);

        IrCallResult result = decoder.Oracle(ProductEncoder.Side.Old).Answer(new CallIdentity("F"), [], resultType: null, 0, [new IrHeapSlice("field.C.x", Heap)]);

        Assert.Equal([Heap], result.Heap);
    }

    [Fact]
    public void AMapInAnUnreadShapeIsAnEncoderBug()
    {
        using Context context = new();
        using Solver solver = context.MkSolver();
        Assert.Equal(Status.SATISFIABLE, solver.Check());
        ModelDecoder decoder = new(context, solver.Model, ProductEncoder.Encode(context, WithoutHeap, WithoutHeap, []));
        using BitVecSort bv32 = context.MkBitVecSort(32);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            decoder.Decode(context.MkArrayConst("m", bv32, bv32), Heap.MapType));

        Assert.StartsWith("Encoder bug: the model gives a map in a shape the decoder does not read", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayThrowsWhenTheModelDoesNotActuallyDiverge()
    {
        using Context context = new();
        ProductEncoder.ProductEncoding encoding = ProductEncoder.Encode(context, WithoutHeap, WithoutHeap, []);
        using Solver solver = context.MkSolver();
        solver.Add(encoding.Assertions);
        Assert.Equal(Status.SATISFIABLE, solver.Check());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.Replay(context, solver.Model, encoding, WithoutHeap, WithoutHeap));

        Assert.StartsWith("Encoder bug:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEncoderBugMessageSeparatesEachSharedInput()
    {
        using Context context = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.EnsureDiverges(TwoParams, TwoParams, Shared(TwoParams, TwoParams), Inputs, Traced("F"), Traced("F"), Calls(context)));

        Assert.Contains(", b=", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEncoderBugMessageSeparatesMultipleOutsTraceEntriesAndArguments()
    {
        using Context context = new();
        IrRun run = new(new IrReturned(Value: null), [Bv(7), Bv(9)], [new IrCallRecord(new CallIdentity("F"), [Bv(1), Bv(2)]), new IrCallRecord(new CallIdentity("G"), [Bv(3)])]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.EnsureDiverges(WithoutHeap, WithoutHeap, Shared(WithoutHeap, WithoutHeap), Inputs, run, run, Calls(context)));

        Assert.Matches(@"outs \[[^\]]*, [^\]]*\]", exception.Message);
        Assert.Matches(@"trace \[[^\]]*\), [A-Za-z]+\(", exception.Message);
        Assert.Matches(@"F\([^)]*, [^)]*\)", exception.Message);
    }

    [Fact]
    public void SortLiteralsAreRememberedAtConstruction()
    {
        using Context context = new();
        ProductEncoder.ProductEncoding encoding = ProductEncoder.Encode(context, WithSortLiteral, WithSortLiteral, []);
        using Solver solver = context.MkSolver();
        solver.Add(encoding.Assertions);
        Assert.Equal(Status.SATISFIABLE, solver.Check());

        ModelDecoder decoder = new(context, solver.Model, encoding);
        Expr literal = encoding.Sorts.Literal(new IrSortValue("S", 5));
        IrValue decoded = decoder.Decode(solver.Model.Eval(literal, completion: true), new IrSort("S"));

        Assert.Equal(new IrSortValue("S", 5), decoded);
    }

    [Fact]
    public void TheSameSortElementReusesItsId()
    {
        using Context context = new();
        ProductEncoder.ProductEncoding encoding = ProductEncoder.Encode(context, WithoutHeap, WithoutHeap, []);
        using Solver solver = context.MkSolver();
        Assert.Equal(Status.SATISFIABLE, solver.Check());
        ModelDecoder decoder = new(context, solver.Model, encoding);

        Sort sort = context.MkUninterpretedSort("S");
        Expr value = context.MkConst("v0", sort);

        IrValue first = decoder.Decode(value, new IrSort("S"));
        IrValue again = decoder.Decode(value, new IrSort("S"));

        Assert.Equal(first, again);
    }

    [Fact]
    public void UnknownSortElementsGetTheNextFreeId()
    {
        using Context context = new();
        ProductEncoder.ProductEncoding encoding = ProductEncoder.Encode(context, WithoutHeap, WithoutHeap, []);
        using Solver solver = context.MkSolver();
        Assert.Equal(Status.SATISFIABLE, solver.Check());
        ModelDecoder decoder = new(context, solver.Model, encoding);

        Sort sort = context.MkUninterpretedSort("S");
        Expr v0 = context.MkConst("v0", sort);
        Expr v1 = context.MkConst("v1", sort);
        Expr v2 = context.MkConst("v2", sort);

        IrSortValue first = Assert.IsType<IrSortValue>(decoder.Decode(v0, new IrSort("S")));
        IrSortValue second = Assert.IsType<IrSortValue>(decoder.Decode(v1, new IrSort("S")));
        IrSortValue third = Assert.IsType<IrSortValue>(decoder.Decode(v2, new IrSort("S")));

        Assert.Equal(0, first.Id);
        Assert.Equal(1, second.Id);
        Assert.Equal(2, third.Id);
    }

    private static IrBitVecValue Bv(ulong bits) => new(32, bits);

    private static ImmutableArray<ProductEncoder.SharedParameter> Shared(IrProcedure old, IrProcedure @new) => ProductEncoder.Pair(old, @new);

    private static IrRun Returned(ImmutableArray<IrValue> outs) => new(new IrReturned(Value: null), outs, []);

    private static IrRun Traced(string callee) => new(new IrReturned(Value: null), [], [new IrCallRecord(new CallIdentity(callee), [Bv(1)])]);

    private static TraceEncoder Calls(Context context, ImmutableDictionary<string, string>? map = null) =>
        new(new SortMapper(context), [], map ?? [], []);

    /// <summary>Mirrors the private <c>ModelDecoder.Describe</c> format, so a comma-separator mutation there fails this assertion.</summary>
    private static string ExpectedDescribe(IrRun run) =>
        $"{run.Outcome} outs [{string.Join(", ", run.Outs)}] trace [{string.Join(", ", run.Trace.Select(static c => $"{c.Callee.Value}({string.Join(", ", c.Arguments)})"))}]";
}
