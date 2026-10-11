using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// The replay with taint (ADR 0026; ticket M3-016 criteria 3, 5 and 6). The lowerer does not emit <c>opaque:</c> calls
/// yet, so the pairs are built as IR directly: a divergence only through an <c>opaque:</c> call is
/// <see cref="UnknownReason.Abstraction"/>, one on an untainted observable of a procedure that also calls one is
/// <see cref="Divergent"/>, and a branch on an <c>opaque:</c> result taints the rest of the path.
/// </summary>
public sealed class TaintReplayTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    private static readonly CallIdentity Add = new("opaque:add");

    private static readonly CallIdentity Parse = new("System.Int32::Parse(string)");

    [Fact]
    public void TaintOnlyDivergenceIsUnknownAbstraction()
    {
        Verdict verdict = Verify(
            """
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              %t: bv32 = call "opaque:add"(%a, %b)
              ret %t
            """,
            """
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              %t: bv32 = call "opaque:add"(%b, %a)
              ret %t
            """);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Equal("the divergence depends on opaque:add", unknown.Detail);
        Assert.Equal([new Abstraction(Codebase.Legacy, Add, Span: null), new Abstraction(Codebase.Modern, Add, Span: null)], unknown.Abstractions);
        Assert.Empty(unknown.Causes);
        Counterexample candidate = Assert.IsType<Counterexample>(unknown.Candidate);
        Assert.NotEqual(candidate.Old.Outcome, candidate.New.Outcome);
        Assert.True(candidate.Old.Taint.Value);
        LadderStep step = Assert.Single(unknown.Ladder);
        Assert.Equal(RungOutcome.Inconclusive, step.Outcome);
        Assert.Equal("a divergence within 3 iterations depends on an abstraction", step.Detail);
    }

    [Fact]
    public void UntaintedDivergenceIsDivergent()
    {
        Verdict verdict = Verify(
            """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %t: bv32 = call "opaque:add"(%a, %a)
              ret %a
            """,
            """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %t: bv32 = call "opaque:add"(%a, %a)
              %one: bv32 = const bv32 1
              %r: bv32 = add %a, %one
              ret %r
            """);

        Divergent divergent = Assert.IsType<Divergent>(verdict);
        Assert.Equal([Add], divergent.Counterexample.New.Taint.Sources);
        Assert.False(divergent.Counterexample.New.Taint.Value);
        Assert.Equal(RungOutcome.Refuted, Assert.Single(divergent.Ladder).Outcome);
    }

    [Fact]
    public void BranchOnTaintTaintsTheRestOfThePath()
    {
        Verdict verdict = Verify(
            """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %t: bool = call "opaque:test"(%a)
              br %t, B1, B2
            B1:
              %one: bv32 = const bv32 1
              ret %one
            B2:
              %two: bv32 = const bv32 2
              ret %two
            """,
            """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %t: bool = call "opaque:test"(%a)
              br %t, B1, B2
            B1:
              %one: bv32 = const bv32 1
              ret %one
            B2:
              %three: bv32 = const bv32 3
              ret %three
            """);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.True(Assert.IsType<Counterexample>(unknown.Candidate).New.Taint.Outcome);
    }

    /// <summary>
    /// ADR 0056 (ticket P2-081): the sides differ only when <c>Parse</c> answers 0, which the solver chose and the real
    /// member may never give for that argument, so the model is a candidate and not a witness.
    /// </summary>
    [Fact]
    public void ADivergenceOnAClosedCallsAnswerIsUnknownAbstraction()
    {
        Verdict verdict = Verify(BranchesOnParse("call closed", 1), BranchesOnParse("call closed", 3));

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Equal([new Abstraction(Codebase.Legacy, Parse, Span: null), new Abstraction(Codebase.Modern, Parse, Span: null)], unknown.Abstractions);
        Counterexample candidate = Assert.IsType<Counterexample>(unknown.Candidate);
        Assert.True(candidate.Old.Taint.Outcome);
        Assert.Equal([0], candidate.Old.Taint.Trace);
    }

    /// <summary>An open call's answer stays the modular assumption (ADR 0026): the same pair without <c>closed</c> is Divergent.</summary>
    [Fact]
    public void ADivergenceOnAnOpenCallsAnswerIsStillDivergent() =>
        Assert.IsType<Divergent>(Verify(BranchesOnParse("call", 1), BranchesOnParse("call", 3)));

    /// <summary>An identity one side calls open is encoded open on both (ADR 0041), so the replay taints neither call.</summary>
    [Fact]
    public void AClosedCallThatSharesItsIdentityWithAnOpenOneIsNotTainted()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(BranchesOnParse("call closed", 1), BranchesOnParse("call", 3)));

        Assert.Empty(divergent.Counterexample.Old.Taint.Sources);
    }

    /// <summary>A difference that does not use the closed call's answer is real whatever the member does.</summary>
    [Fact]
    public void ADivergenceThatIgnoresAClosedCallsAnswerIsDivergent()
    {
        const string old = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %t: bv32 = call closed "System.Int32::Parse(string)"(%a)
              ret %a
            """;
        const string @new = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %t: bv32 = call closed "System.Int32::Parse(string)"(%a)
              %one: bv32 = const bv32 1
              %r: bv32 = add %a, %one
              ret %r
            """;

        Divergent divergent = Assert.IsType<Divergent>(Verify(old, @new));

        Assert.Equal([Parse], divergent.Counterexample.New.Taint.Sources);
    }

    /// <summary>One more closed call on one side shows in a real run only through its answer, so its event is tainted (ADR 0056).</summary>
    [Fact]
    public void AnExtraClosedCallIsUnknownAbstraction()
    {
        const string old = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              ret %a
            """;
        const string @new = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %t: bv32 = call closed "System.Int32::Parse(string)"(%a)
              ret %a
            """;

        Assert.Equal(UnknownReason.Abstraction, Assert.IsType<Unknown>(Verify(old, @new)).Reason);
    }

    /// <summary>
    /// A closed member the runtime-changes table lists answers differently on the two runtimes by the table's own row, so
    /// its call is not tainted and the pair is still Divergent, which the report shows as EQ006.
    /// </summary>
    [Fact]
    public void AClosedCallToARuntimeChangedMemberIsNotTainted()
    {
        const string body = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %t: bv32 = call closed "System.String::IndexOf(string)"!(%a)
              ret %t
            """;

        Divergent divergent = Assert.IsType<Divergent>(Verify(body, body));

        Assert.Empty(divergent.Counterexample.Old.Taint.Sources);
    }

    /// <summary>
    /// ADR 0056: an open call one side makes is an observable whatever a closed call answers. The modern side makes no open
    /// call on any path, so its lacking the legacy side's is real although its path past <c>Parse</c> is tainted.
    /// </summary>
    [Fact]
    public void AnOpenCallAgainstAClosedOneIsDivergent()
    {
        const string old = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %t: bv32 = call "System.Convert::ToInt32(string)"(%a)
              ret %t
            """;

        Divergent divergent = Assert.IsType<Divergent>(Verify(old, BranchesOnParse("call closed", 1)));

        Assert.True(divergent.Counterexample.New.Taint.Outcome);
    }

    /// <summary>
    /// The modern side can make the legacy side's open call past its tainted branch, so whether it lacks that call rests
    /// on what the closed call answers. Were the legacy call one the modern side never makes, the pair would be Divergent.
    /// </summary>
    [Theory]
    [InlineData("T::G(int)", false)]
    [InlineData("T::F(int)", true)]
    public void AnOpenCallAgainstASideThatMayMakeItAfterAClosedCallIsUnknownAbstraction(string legacyCallee, bool divergent)
    {
        string old = $"""
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              call "{legacyCallee}"(%a)
              ret
            """;
        const string @new = """
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              %t: bool = call closed "System.Boolean::Parse(string)"(%a)
              br %t, B1, B2
            B1:
              call "T::G(int)"(%a)
              ret
            B2:
              ret
            """;

        Verdict verdict = Verify(old, @new);

        Assert.Equal(divergent, verdict is Divergent);
        Assert.Equal(!divergent, verdict is Unknown { Reason: UnknownReason.Abstraction });
    }

    /// <summary>A shared fragment stands for calls the replay does not see, so a side that has one may make any open call.</summary>
    [Fact]
    public void AnOpenCallAgainstASideWithASharedFragmentIsUnknownAbstraction()
    {
        const string old = """
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              call "T::F(int)"(%a)
              ret
            """;
        const string @new = """
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              %t: bool = call closed "System.Boolean::Parse(string)"(%a)
              br %t, B1, B2
            B1:
              call "opaque:1f2e"(%a)
              ret
            B2:
              ret
            """;

        Assert.Equal(UnknownReason.Abstraction, Assert.IsType<Unknown>(Verify(old, @new)).Reason);
    }

    /// <summary>
    /// The open-call comparison only undoes the taint of closed calls: a side whose run reached a pure function keeps the
    /// verdict that taint gives, as before ADR 0056.
    /// </summary>
    [Fact]
    public void AnOpenCallAgainstASideTaintedByAPureFunctionIsUnknownAbstraction()
    {
        const string old = """
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              call "T::F(int)"(%a)
              ret
            """;
        const string @new = """
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              %p: bv32 = pure "op:T::Mix(int)"(%a)
              call "T::G(int)"(%p)
              ret
            """;

        Assert.Equal(UnknownReason.Abstraction, Assert.IsType<Unknown>(Verify(old, @new)).Reason);
    }

    [Fact]
    public void UntaintedAgreementIsStillAnEncoderBug()
    {
        using Context context = new();
        IrProcedure procedure = IrText.Parse("""
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              ret
            """);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ModelDecoder.EnsureDiverges(new(procedure, Returned()), new(procedure, Returned()), ProductEncoder.Pair(procedure, procedure), new IrInputs([Bv(1)]), Calls(context)));

        Assert.StartsWith("Encoder bug:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OutcomesCompareByThePathUnlessBothReturned()
    {
        using Context context = new();
        IrRun returned = new(new IrReturned(Bv(1)), [], []) { Taint = Tainted(outcome: false, value: true) };
        IrRun other = new(new IrReturned(Bv(2)), [], []);
        IrRun threw = new(new IrThrew("E"), [], []);

        Assert.Equal(ModelDecoder.Difference.Abstract, Compare(context, returned, other));
        Assert.Equal(ModelDecoder.Difference.Abstract, Compare(context, other, returned));
        Assert.Equal(ModelDecoder.Difference.Real, Compare(context, returned, threw));
        Assert.Equal(ModelDecoder.Difference.Real, Compare(context, threw, returned));
        Assert.Equal(ModelDecoder.Difference.Abstract, Compare(context, returned with { Taint = Tainted(outcome: true, value: true) }, threw));
        Assert.Equal(ModelDecoder.Difference.Abstract, Compare(context, threw, returned with { Taint = Tainted(outcome: true, value: true) }));
        Assert.Equal(ModelDecoder.Difference.Real, Compare(context, other, new IrRun(new IrReturned(Bv(3)), [], [])));
    }

    [Fact]
    public void TheTraceComparesItsFirstDifferingEventOnly()
    {
        using Context context = new();
        IrRun ab = Traced(["a", "b"], tainted: [0]);
        IrRun ac = Traced(["a", "c"], tainted: [0]);
        IrRun xc = Traced(["x", "c"], tainted: [0]);
        IrRun ay = Traced(["a", "y"], tainted: [1]);

        Assert.Equal(ModelDecoder.Difference.Real, Compare(context, ab, ac));
        Assert.Equal(ModelDecoder.Difference.Abstract, Compare(context, ab, xc));
        Assert.Equal(ModelDecoder.Difference.Abstract, Compare(context, ab, ay));
        Assert.Equal(ModelDecoder.Difference.Abstract, Compare(context, ay, ab));
        Assert.Equal(ModelDecoder.Difference.None, Compare(context, ab, Traced(["a", "b"], tainted: [])));
    }

    [Fact]
    public void AMissingEventIsTaintedExactlyWhenThatSidesPathIs()
    {
        using Context context = new();
        IrRun longer = Traced(["a", "b"], tainted: []);
        IrRun shorter = Traced(["a"], tainted: []);

        Assert.Equal(ModelDecoder.Difference.Real, Compare(context, longer, shorter));
        Assert.Equal(ModelDecoder.Difference.Real, Compare(context, shorter, longer));
        Assert.Equal(ModelDecoder.Difference.Abstract, Compare(context, longer, shorter with { Taint = Tainted(outcome: true, value: true) }));
        Assert.Equal(ModelDecoder.Difference.Abstract, Compare(context, shorter with { Taint = Tainted(outcome: true, value: true) }, longer));
        Assert.Equal(ModelDecoder.Difference.Real, Compare(context, longer, shorter with { Taint = Tainted(outcome: false, value: true) }));
    }

    [Fact]
    public void AFinalHeapComparesByItsTaintAndAMissingParameterIsUntainted()
    {
        using Context context = new();
        IrProcedure withHeap = IrText.Parse("""
            proc "T::M(int)" (%a: bv32, ref %field.C.x: map<bv32, bv32>) entry B0
            B0:
              ret outs(%field.C.x = %field.C.x)
            """);
        IrProcedure withoutHeap = IrText.Parse("""
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              ret
            """);
        IrMapValue heap = new(new IrMap(new IrBitVec(32), new IrBitVec(32)), Bv(0), []);
        IrInputs inputs = new([Bv(1), heap]);
        IrRun changed = new(new IrReturned(Value: null), [heap.Write(Bv(1), Bv(2))], []);
        IrRun changedTainted = changed with { Taint = IrTaint.None with { Outs = [0] } };
        IrRun none = new(new IrReturned(Value: null), [], []);
        ImmutableArray<ProductEncoder.SharedParameter> oldHeap = ProductEncoder.Pair(withHeap, withoutHeap);
        ImmutableArray<ProductEncoder.SharedParameter> newHeap = ProductEncoder.Pair(withoutHeap, withHeap);

        Assert.Equal(ModelDecoder.Difference.Real, ModelDecoder.Compare(new(withHeap, changed), new(withoutHeap, none), oldHeap, inputs, Calls(context)));
        Assert.Equal(ModelDecoder.Difference.Abstract, ModelDecoder.Compare(new(withHeap, changedTainted), new(withoutHeap, none), oldHeap, inputs, Calls(context)));
        Assert.Equal(ModelDecoder.Difference.Abstract, ModelDecoder.Compare(new(withoutHeap, none), new(withHeap, changedTainted), newHeap, inputs, Calls(context)));
    }

    /// <summary>
    /// Ticket P1-005: a side without a heap map reports the version its oracle threaded through its calls, which is tainted
    /// once that side's run reached an abstraction.
    /// </summary>
    [Fact]
    public void AThreadedHeapIsTheFinalValueOfASideWithoutTheMapAndIsTaintedAfterAnAbstraction()
    {
        using Context context = new();
        IrProcedure withHeap = IrText.Parse("""
            proc "T::M(int)" (%a: bv32, ref %field.C.x: map<bv32, bv32>) entry B0
            B0:
              ret outs(%field.C.x = %field.C.x)
            """);
        IrProcedure withoutHeap = IrText.Parse("""
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              ret
            """);
        IrMapValue heap = new(new IrMap(new IrBitVec(32), new IrBitVec(32)), Bv(0), []);
        IrMapValue written = heap.Write(Bv(1), Bv(2));
        IrInputs inputs = new([Bv(1), heap]);
        IrRun changed = new(new IrReturned(Value: null), [written], []);
        IrRun none = new(new IrReturned(Value: null), [], []);
        IrRun afterAbstraction = none with { Taint = IrTaint.None with { Sources = [Add] } };
        ImmutableArray<ProductEncoder.SharedParameter> shared = ProductEncoder.Pair(withHeap, withoutHeap);
        Dictionary<TraceEncoder.HeapMap, IrValue> threadedTo(IrMapValue value) => new() { [new TraceEncoder.HeapMap("field.C.x", heap.Type)] = value };

        Assert.Equal(ModelDecoder.Difference.None, ModelDecoder.Compare(new(withHeap, changed), new(withoutHeap, none, threadedTo(written)), shared, inputs, Calls(context)));
        Assert.Equal(ModelDecoder.Difference.Real, ModelDecoder.Compare(new(withHeap, changed), new(withoutHeap, none, threadedTo(heap)), shared, inputs, Calls(context)));
        Assert.Equal(ModelDecoder.Difference.Abstract, ModelDecoder.Compare(new(withHeap, changed), new(withoutHeap, afterAbstraction, threadedTo(heap)), shared, inputs, Calls(context)));
        Assert.Equal(ModelDecoder.Difference.Real, ModelDecoder.Compare(new(withHeap, changed), new(withoutHeap, afterAbstraction), shared, inputs, Calls(context)));
    }

    /// <summary>
    /// Ticket P1-005: when the side without the map threads it through an <c>opaque:</c> call, a difference in the final heap
    /// depends on the abstraction, and so does every event, which may carry a threaded version.
    /// </summary>
    [Fact]
    public void AHeapThreadedThroughAnAbstractionIsTainted()
    {
        Verdict verdict = Verify(
            """
            proc "T::M(int)" (%a: bv32, ref %field.C.x: map<bv32, bv32>) entry B0
            B0:
              call "opaque:add"(%a) heap("field.C.x" %field.C.x -> %x1: map<bv32, bv32>)
              %one: bv32 = const bv32 1
              %x2: map<bv32, bv32> = mapwrite %x1, %a, %one
              ret outs(%field.C.x = %x2)
            """,
            """
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              call "opaque:add"(%a)
              ret
            """);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Equal([0], Assert.IsType<Counterexample>(unknown.Candidate).New.Taint.Trace);
    }

    [Fact]
    public void OnlyOpaquePrefixedCallsAreAbstractions()
    {
        Assert.True(ModelDecoder.IsAbstraction(new CallIdentity("opaque:1f2e")));
        Assert.False(ModelDecoder.IsAbstraction(new CallIdentity("T::opaque:x")));
        Assert.False(ModelDecoder.IsAbstraction(new CallIdentity("Opaque:x")));
    }

    /// <summary>A procedure that calls <c>Parse</c> by <paramref name="call"/> and returns <paramref name="onZero"/> when it answers 0, else 2.</summary>
    private static string BranchesOnParse(string call, int onZero) =>
        $"""
        proc "T::M(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %t: bv32 = {call} "System.Int32::Parse(string)"(%a)
          %zero: bv32 = const bv32 0
          %z: bool = eq %t, %zero
          br %z, B1, B2
        B1:
          %r: bv32 = const bv32 {onZero.ToString(System.Globalization.CultureInfo.InvariantCulture)}
          ret %r
        B2:
          %two: bv32 = const bv32 2
          ret %two
        """;

    private static Verdict Verify(string old, string @new) => new Z3Backend().Verify(IrText.Parse(old), IrText.Parse(@new), Options);

    private static ModelDecoder.Difference Compare(Context context, IrRun old, IrRun @new)
    {
        IrProcedure procedure = IrText.Parse("""
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              call "a"(%a)
              call "b"(%a)
              call "c"(%a)
              call "x"(%a)
              call "y"(%a)
              ret
            """);
        return ModelDecoder.Compare(new(procedure, old), new(procedure, @new), ProductEncoder.Pair(procedure, procedure), new IrInputs([Bv(1)]), Calls(context));
    }

    private static IrTaint Tainted(bool outcome, bool value) => IrTaint.None with { Outcome = outcome, Value = value };

    private static IrRun Returned() => new(new IrReturned(Value: null), [], []);

    private static IrRun Traced(ImmutableArray<string> callees, ImmutableArray<int> tainted) =>
        new(new IrReturned(Value: null), [], [.. callees.Select(static c => new IrCallRecord(new CallIdentity(c), []))])
        {
            Taint = IrTaint.None with { Trace = tainted },
        };

    private static IrBitVecValue Bv(ulong bits) => new(32, bits);

    private static TraceEncoder Calls(Context context) => new(new SortMapper(context), [], [], []);
}
