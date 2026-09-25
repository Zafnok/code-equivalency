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
            ModelDecoder.EnsureDiverges(procedure, procedure, ProductEncoder.Pair(procedure, procedure), new IrInputs([Bv(1)]), Returned(), Returned(), Calls(context)));

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

        Assert.Equal(ModelDecoder.Difference.Real, ModelDecoder.Compare(withHeap, withoutHeap, oldHeap, inputs, changed, none, Calls(context)));
        Assert.Equal(ModelDecoder.Difference.Abstract, ModelDecoder.Compare(withHeap, withoutHeap, oldHeap, inputs, changedTainted, none, Calls(context)));
        Assert.Equal(ModelDecoder.Difference.Abstract, ModelDecoder.Compare(withoutHeap, withHeap, newHeap, inputs, none, changedTainted, Calls(context)));
    }

    [Fact]
    public void OnlyOpaquePrefixedCallsAreAbstractions()
    {
        Assert.True(ModelDecoder.IsAbstraction(new CallIdentity("opaque:1f2e")));
        Assert.False(ModelDecoder.IsAbstraction(new CallIdentity("T::opaque:x")));
        Assert.False(ModelDecoder.IsAbstraction(new CallIdentity("Opaque:x")));
    }

    private static Verdict Verify(string old, string @new) => new Z3Backend().Verify(IrText.Parse(old), IrText.Parse(@new), Options);

    private static ModelDecoder.Difference Compare(Context context, IrRun old, IrRun @new)
    {
        IrProcedure procedure = IrText.Parse("""
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              ret
            """);
        return ModelDecoder.Compare(procedure, procedure, ProductEncoder.Pair(procedure, procedure), new IrInputs([Bv(1)]), old, @new, Calls(context));
    }

    private static IrTaint Tainted(bool outcome, bool value) => IrTaint.None with { Outcome = outcome, Value = value };

    private static IrRun Returned() => new(new IrReturned(Value: null), [], []);

    private static IrRun Traced(ImmutableArray<string> callees, ImmutableArray<int> tainted) =>
        new(new IrReturned(Value: null), [], [.. callees.Select(static c => new IrCallRecord(new CallIdentity(c), []))])
        {
            Taint = IrTaint.None with { Trace = tainted },
        };

    private static IrBitVecValue Bv(ulong bits) => new(32, bits);

    private static TraceEncoder Calls(Context context) => new(new SortMapper(context), [], []);
}
