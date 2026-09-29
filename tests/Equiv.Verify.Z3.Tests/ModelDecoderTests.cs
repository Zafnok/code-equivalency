using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

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

        Assert.False(ModelDecoder.Diverges(new(WithHeap, Returned([Heap])), new(WithoutHeap, Returned([])), Shared(WithHeap, WithoutHeap), Inputs, Calls(context)));
    }

    [Fact]
    public void AOneSidedHeapThatChangedDiverges()
    {
        using Context context = new();

        Assert.True(ModelDecoder.Diverges(new(WithHeap, Returned([Heap.Write(Bv(1), Bv(2))])), new(WithoutHeap, Returned([])), Shared(WithHeap, WithoutHeap), Inputs, Calls(context)));
        Assert.True(ModelDecoder.Diverges(new(WithoutHeap, Returned([])), new(WithHeap, Returned([Heap.Write(Bv(1), Bv(2))])), Shared(WithoutHeap, WithHeap), Inputs, Calls(context)));
    }

    [Fact]
    public void ADifferentOutcomeDiverges()
    {
        using Context context = new();
        IrRun threw = new(new IrThrew("System.Exception"), [], []);

        Assert.True(ModelDecoder.Diverges(new(WithoutHeap, Returned([])), new(WithoutHeap, threw), Shared(WithoutHeap, WithoutHeap), Inputs, Calls(context)));
    }

    [Fact]
    public void TracesAreComparedAfterRenamingLegacyCallees()
    {
        using Context context = new();
        TraceEncoder calls = Calls(context, ImmutableDictionary<string, string>.Empty.Add("Old::F", "New::F"));

        Assert.False(ModelDecoder.Diverges(new(WithoutHeap, Traced("Old::F")), new(WithoutHeap, Traced("New::F")), Shared(WithoutHeap, WithoutHeap), Inputs, calls));
        Assert.True(ModelDecoder.Diverges(new(WithoutHeap, Traced("New::F")), new(WithoutHeap, Traced("Old::F")), Shared(WithoutHeap, WithoutHeap), Inputs, calls));
        Assert.True(ModelDecoder.Diverges(new(WithoutHeap, Traced("Old::F")), new(WithoutHeap, Traced("Other::F")), Shared(WithoutHeap, WithoutHeap), Inputs, calls));
    }

    [Fact]
    public void AReplayThatDoesNotDivergeIsAnEncoderBug()
    {
        using Context context = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.EnsureDiverges(new(WithoutHeap, Traced("F")), new(WithoutHeap, Traced("F")), Shared(WithoutHeap, WithoutHeap), Inputs, Calls(context)));

        Assert.StartsWith("Encoder bug: the solver found a divergence between T::M(int) and T::M(int), but the replay does not diverge.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("a=IrBitVecValue", exception.Message, StringComparison.Ordinal);
        Assert.Contains("trace [F(IrBitVecValue", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADivergingReplayPasses()
    {
        using Context context = new();

        Exception? exception = Record.Exception(() =>
            ModelDecoder.EnsureDiverges(new(WithoutHeap, Traced("F")), new(WithoutHeap, Traced("G")), Shared(WithoutHeap, WithoutHeap), Inputs, Calls(context)));

        Assert.Null(exception);
    }

    [Fact]
    public void TheEncoderBugMessageListsEachInputSeparatedByAComma()
    {
        using Context context = new();
        ImmutableArray<ProductEncoder.SharedParameter> shared = Shared(WithHeap, WithHeap);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.EnsureDiverges(new(WithHeap, Returned([Heap])), new(WithHeap, Returned([Heap])), shared, Inputs, Calls(context)));

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
            ModelDecoder.EnsureDiverges(new(WithoutHeap, run), new(WithoutHeap, run), Shared(WithoutHeap, WithoutHeap), Inputs, Calls(context)));

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

        IrCallResult result = decoder.Oracle(ProductEncoder.Side.Old).Answer(new CallIdentity("F"), [], resultType: null, 0, [new IrHeapSlice("field.C.x", Heap)], []);

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

    /// <summary>
    /// Ticket P2-041: a model may give a map as <c>as-array</c> over a function it interprets; the map's entries are the
    /// function's entries and its default the else value, nested maps included.
    /// </summary>
    [Fact]
    public void AnAsArrayMapDecodesFromTheModelsInterpretationOfItsFunction()
    {
        using Context context = new();
        using BitVecSort bv32 = context.MkBitVecSort(32);
        using ArraySort inner = context.MkArraySort(bv32, bv32);
        FuncDecl f = context.MkFuncDecl("f", bv32, bv32);
        FuncDecl g = context.MkFuncDecl("g", bv32, inner);
        (Expr asF, Expr asG) = AsArrays(context, f, g);
        using Solver solver = context.MkSolver();
        solver.Add(
            context.MkEq(context.MkApp(f, context.MkBV(1, 32)), context.MkBV(2, 32)),
            context.MkEq(context.MkApp(f, context.MkBV(3, 32)), context.MkBV(4, 32)),
            context.MkEq(context.MkApp(g, context.MkBV(5, 32)), context.MkStore(context.MkConstArray(bv32, context.MkBV(0, 32)), context.MkBV(1, 32), context.MkBV(2, 32))));
        Assert.Equal(Status.SATISFIABLE, solver.Check());
        ModelDecoder decoder = new(context, solver.Model, ProductEncoder.Encode(context, WithoutHeap, WithoutHeap, []));
        IrMap nested = new(new IrBitVec(32), Heap.MapType);

        IrMapValue map = Assert.IsType<IrMapValue>(decoder.Decode(asF, Heap.MapType));
        IrMapValue outer = Assert.IsType<IrMapValue>(decoder.Decode(asG, nested));

        Assert.True(asF.IsAsArray);
        Assert.Equal(Bv(2), map.Read(Bv(1)));
        Assert.Equal(Bv(4), map.Read(Bv(3)));
        Assert.Equal(Bv(2), ((IrMapValue)outer.Read(Bv(5))).Read(Bv(1)));
    }

    /// <summary>Ticket P2-041: an <c>as-array</c> with no model, or whose function the model does not interpret, is an encoder bug.</summary>
    [Fact]
    public void AnAsArrayMapWithoutAnInterpretationIsAnEncoderBug()
    {
        using Context context = new();
        using BitVecSort bv32 = context.MkBitVecSort(32);
        FuncDecl f = context.MkFuncDecl("f", bv32, bv32);
        (Expr asF, _) = AsArrays(context, f, context.MkFuncDecl("g", bv32, bv32));
        using Solver solver = context.MkSolver();
        Assert.Equal(Status.SATISFIABLE, solver.Check());
        ModelDecoder decoder = new(context, solver.Model, ProductEncoder.Encode(context, WithoutHeap, WithoutHeap, []));

        InvalidOperationException uninterpreted = Assert.Throws<InvalidOperationException>(() => decoder.Decode(asF, Heap.MapType));
        InvalidOperationException modelless = Assert.Throws<InvalidOperationException>(() => new ModelDecoder.Values().Decode(asF, Heap.MapType));

        Assert.StartsWith("Encoder bug: the model gives a map in a shape the decoder does not read", uninterpreted.Message, StringComparison.Ordinal);
        Assert.StartsWith("Encoder bug: the model gives a map in a shape the decoder does not read", modelless.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-019 criterion 1: the encoder only assumes the lengths it reads, so a model can give a reference nothing
    /// reads a negative length; the decoded input gives it 0 and keeps the length it read.
    /// </summary>
    [Fact]
    public void ANegativeLengthAtAnUnreadReferenceDecodesAsZero()
    {
        IrProcedure reads = IrText.Parse("""
            proc "T::M(int[])" (%u "u": sort "int[]", %length.int__: map<sort "int[]", bv32>) -> bv32 entry B0
            B0:
              %l: bv32 = mapread %length.int__, %u
              ret %l
            """);
        using Context context = new();
        ProductEncoder.ProductEncoding encoding = ProductEncoder.Encode(context, reads, reads, []);
        ArrayExpr lengths = (ArrayExpr)encoding.Inputs[1].Term;
        Expr u = encoding.Inputs[0].Term;
        Expr other = context.MkConst("other", encoding.Sorts.Sort(new IrSort("int[]")));
        using Solver solver = context.MkSolver();
        solver.Add(encoding.Assertions);
        solver.Add(context.MkNot(context.MkEq(other, u)), context.MkEq(context.MkSelect(lengths, u), context.MkBV(3, 32)), context.MkEq(context.MkSelect(lengths, other), context.MkBV(-1, 32)));
        Assert.Equal(Status.SATISFIABLE, solver.Check());
        ModelDecoder decoder = new(context, solver.Model, encoding);
        IrValue raw = decoder.Decode(solver.Model.Eval(lengths, completion: true), encoding.Inputs[1].Shared.Type);

        IrInputs inputs = decoder.Inputs();

        IrMapValue decoded = Assert.IsType<IrMapValue>(inputs.Arguments[1]);
        Assert.Contains(IrBitVecValue.FromSigned(32, -1), ((IrMapValue)raw).Entries.Values.Append(((IrMapValue)raw).Default));
        Assert.Equal(IrBitVecValue.FromSigned(32, 3), decoded.Read(inputs.Arguments[0]));
        Assert.All(decoded.Entries.Values.Prepend(decoded.Default), static v => Assert.True(((IrBitVecValue)v).TwosComplement >= 0, $"negative length {v}"));
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
            ModelDecoder.EnsureDiverges(new(TwoParams, Traced("F")), new(TwoParams, Traced("F")), Shared(TwoParams, TwoParams), Inputs, Calls(context)));

        Assert.Contains(", b=", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEncoderBugMessageSeparatesMultipleOutsTraceEntriesAndArguments()
    {
        using Context context = new();
        IrRun run = new(new IrReturned(Value: null), [Bv(7), Bv(9)], [new IrCallRecord(new CallIdentity("F"), [Bv(1), Bv(2)]), new IrCallRecord(new CallIdentity("G"), [Bv(3)])]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.EnsureDiverges(new(WithoutHeap, run), new(WithoutHeap, run), Shared(WithoutHeap, WithoutHeap), Inputs, Calls(context)));

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

    /// <summary>
    /// Ticket P2-033: a loop rung replays the original procedures from a fragment's model, so the replay can pass a
    /// string literal the fragment never mentions to a call. The oracle must still encode it, not fail the lookup
    /// with <see cref="KeyNotFoundException"/>.
    /// </summary>
    [Fact]
    public void AReplayPassesALiteralTheFragmentNeverMentionedToACall()
    {
        IrProcedure oldOriginal = CallsWithLiteral("F");
        IrProcedure newOriginal = CallsWithLiteral("G");
        using Context context = new();
        ProductEncoder.ProductEncoding encoding = ProductEncoder.Encode(context, WithoutHeap, WithoutHeap, []);
        using Solver solver = context.MkSolver();
        solver.Add(encoding.Assertions);
        Assert.Equal(Status.SATISFIABLE, solver.Check());

        Counterexample? counterexample = ModelDecoder.TryReplay(context, solver.Model, encoding, oldOriginal, newOriginal, stepBudget: 10);

        Assert.NotNull(counterexample);
        IrValue passed = Assert.Single(counterexample.Old.Trace).Arguments[1];
        Assert.Equal(new IrSortValue("System.String", 1174359459), passed);
    }

    [Fact]
    public void AnUnseenLiteralKeepsItsIdWhenItsTermDecodes()
    {
        using Context context = new();
        Expr element = context.MkConst("e", context.MkUninterpretedSort("S"));
        ModelDecoder.Values values = new();

        Expr term = values.Term(new IrSortValue("S", 7), () => element);

        Assert.Same(element, term);
        Assert.Same(element, values.Term(new IrSortValue("S", 7), static () => throw new InvalidOperationException("asked twice")));
        Assert.Equal(new IrSortValue("S", 7), values.Decode(element, new IrSort("S")));
    }

    [Fact]
    public void AnUnseenLiteralDoesNotTakeOverAnElementAlreadyKnown()
    {
        using Context context = new();
        Expr element = context.MkConst("e", context.MkUninterpretedSort("S"));
        ModelDecoder.Values values = new();
        values.Remember(new IrSortValue("S", 5), element);

        values.Term(new IrSortValue("S", 7), () => element);

        Assert.Equal(new IrSortValue("S", 5), values.Decode(element, new IrSort("S")));
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

    /// <summary>The terms <c>(_ as-array f)</c> and <c>(_ as-array g)</c>, which the .NET API only builds through the parser.</summary>
    private static (Expr F, Expr G) AsArrays(Context context, FuncDecl f, FuncDecl g)
    {
        BoolExpr[] parsed = context.ParseSMTLIB2String(
            "(assert (= (_ as-array f) (_ as-array f))) (assert (= (_ as-array g) (_ as-array g)))",
            [],
            [],
            [f.Name, g.Name],
            [f, g]);
        return (parsed[0].Args[0], parsed[1].Args[0]);
    }

    private static ImmutableArray<ProductEncoder.SharedParameter> Shared(IrProcedure old, IrProcedure @new) => ProductEncoder.Pair(old, @new);

    private static IrProcedure CallsWithLiteral(string callee) => IrText.Parse($$"""
        proc "T::M(int)" (%a: bv32) entry B0
        B0:
          %s: sort "System.String" = const sort "System.String" 1174359459
          call "T::{{callee}}(int,string)"(%a, %s)
          ret
        """);

    private static IrRun Returned(ImmutableArray<IrValue> outs) => new(new IrReturned(Value: null), outs, []);

    private static IrRun Traced(string callee) => new(new IrReturned(Value: null), [], [new IrCallRecord(new CallIdentity(callee), [Bv(1)])]);

    private static TraceEncoder Calls(Context context, ImmutableDictionary<string, string>? map = null) =>
        new(new SortMapper(context), [], map ?? [], []);

    /// <summary>Mirrors the private <c>ModelDecoder.Describe</c> format, so a comma-separator mutation there fails this assertion.</summary>
    private static string ExpectedDescribe(IrRun run) =>
        $"{run.Outcome} outs [{string.Join(", ", run.Outs)}] trace [{string.Join(", ", run.Trace.Select(static c => $"{c.Callee.Value}({string.Join(", ", c.Arguments)})"))}]";
}
