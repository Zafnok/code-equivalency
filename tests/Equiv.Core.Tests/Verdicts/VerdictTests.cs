using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Core.Tests.Verdicts;

/// <summary>One case per verdict kind (ARCHITECTURE.md: Equivalent, Divergent, Unknown, Added, Removed; nothing else).</summary>
public sealed class VerdictTests
{
    private static readonly IrRun SampleRun = new(new IrReturned(new IrBitVecValue(32, 1)), [], []);

    [Fact]
    public void EquivalentIsAVerdict()
    {
        Assert.IsAssignableFrom<Verdict>(new Equivalent());
    }

    [Fact]
    public void DivergentCarriesACounterexample()
    {
        Counterexample counterexample = new(new IrInputs([]), SampleRun, SampleRun);
        Divergent verdict = new(counterexample);
        Assert.Same(counterexample, verdict.Counterexample);
        Assert.IsAssignableFrom<Verdict>(verdict);
    }

    [Fact]
    public void CounterexampleComparesOldAndNewRunsStructurally()
    {
        Counterexample a = new(new IrInputs([new IrBitVecValue(32, 1)]), SampleRun, SampleRun);
        Counterexample b = new(new IrInputs([new IrBitVecValue(32, 1)]), SampleRun, SampleRun);
        Assert.Equal(a, b);
    }

    [Fact]
    public void UnknownCarriesAReasonAndDetail()
    {
        Unknown verdict = new(UnknownReason.Timeout, "solver gave up after 5000ms");
        Assert.Equal(UnknownReason.Timeout, verdict.Reason);
        Assert.Equal("solver gave up after 5000ms", verdict.Detail);
        Assert.IsAssignableFrom<Verdict>(verdict);
    }

    [Theory]
    [InlineData(UnknownReason.Timeout)]
    [InlineData(UnknownReason.Opaque)]
    [InlineData(UnknownReason.UnmatchedOverload)]
    public void EveryUnknownReasonRoundTripsThroughTheRecord(UnknownReason reason)
    {
        Assert.Equal(reason, new Unknown(reason, "detail").Reason);
    }

    [Fact]
    public void AddedIsAVerdict()
    {
        Assert.IsAssignableFrom<Verdict>(new Added());
    }

    [Fact]
    public void RemovedIsAVerdict()
    {
        Assert.IsAssignableFrom<Verdict>(new Removed());
    }
}
