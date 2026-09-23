using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.TestSupport;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Core.Tests.Ir;

/// <summary>
/// <see cref="IrFragmenter"/> on the <see cref="LoopFixtures"/> (ticket M3-002 deliverable 1 and criterion 6): the loop
/// state of a header, and segments cut at every header, which validate and are pinned by snapshots.
/// </summary>
public sealed class IrFragmenterTests
{
    private static readonly IrBlockId B0 = new(0);
    private static readonly IrBlockId B1 = new(1);
    private static readonly IrBlockId B2 = new(2);

    [Fact]
    public void TheStateIsThePhisThenTheLiveInsParametersFirst()
    {
        IrProcedure single = IrText.Parse(LoopFixtures.Single);

        Assert.Equal(["i", "s", "n", "one"], IrFragmenter.State(single, B1).Select(static v => v.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void AnInnerHeaderCarriesWhatTheOuterLoopStillNeeds()
    {
        IrProcedure nested = IrText.Parse(LoopFixtures.Nested);

        Assert.Equal(["j", "t", "n", "z", "one", "i"], IrFragmenter.State(nested, B2).Select(static v => v.Name), StringComparer.Ordinal);
        Assert.Equal(["i", "s", "n", "z", "one"], IrFragmenter.State(nested, B1).Select(static v => v.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void ARefParameterLiveAtTheHeaderIsState()
    {
        IrProcedure throws = IrText.Parse(LoopFixtures.Throws);

        Assert.Equal(["i", "r1", "n", "k", "one"], IrFragmenter.State(throws, B1).Select(static v => v.Name), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(nameof(LoopFixtures.Single), -1)]
    [InlineData(nameof(LoopFixtures.Single), 1)]
    [InlineData(nameof(LoopFixtures.EarlyReturn), 1)]
    [InlineData(nameof(LoopFixtures.Throws), -1)]
    [InlineData(nameof(LoopFixtures.Throws), 1)]
    [InlineData(nameof(LoopFixtures.Nested), -1)]
    [InlineData(nameof(LoopFixtures.Nested), 1)]
    [InlineData(nameof(LoopFixtures.Nested), 2)]
    public Task SegmentsAreValidAndCutAtEveryHeader(string name, int start)
    {
        IrProcedure procedure = Load(name);

        IrProcedure segment = IrFragmenter.Segment(procedure, start < 0 ? null : new IrBlockId(start), Cuts(procedure));

        Assert.Empty(IrValidator.Validate(segment));
        Assert.Empty(IrLoopAnalysis.Of(segment).Loops);
        return Verify(IrText.Dump(segment)).UseParameters(name, start);
    }

    [Fact]
    public void AHeaderSegmentRunsOneIterationAndReportsTheNextState()
    {
        IrProcedure single = IrText.Parse(LoopFixtures.Single);
        IrProcedure body = IrFragmenter.Segment(single, B1, Cuts(single));

        IrRun next = IrGen.Run(body, new IrInputs([Bits(0), Bits(0), Bits(2), Bits(1), Bits(7)]));
        IrRun done = IrGen.Run(body, new IrInputs([Bits(5), Bits(9), Bits(2), Bits(1), Bits(7)]));

        Assert.Equal(new IrThrew(IrFragmenter.CutException), next.Outcome);
        Assert.Equal([new IrCallRecord(new CallIdentity("equiv:cut:0"), [Bits(1), Bits(0), Bits(2), Bits(1)])], next.Trace);
        Assert.Equal(new IrReturned(Bits(9)), done.Outcome);
        Assert.Empty(done.Trace);
    }

    [Fact]
    public void TheEntrySegmentOfAnEntryThatIsAHeaderRunsItOnce()
    {
        IrProcedure procedure = IrText.Parse("""
            proc "T::M(bool)" (%c: bool) entry B0
            B0:
              br %c, B0, B1
            B1:
              ret
            """);

        IrProcedure segment = IrFragmenter.Segment(procedure, start: null, Cuts(procedure));

        Assert.Empty(IrValidator.Validate(segment));
        Assert.Equal(new IrThrew(IrFragmenter.CutException), IrGen.Run(segment, new IrInputs([new IrBoolValue(Value: true)])).Outcome);
        Assert.Equal(new IrReturned(Value: null), IrGen.Run(segment, new IrInputs([new IrBoolValue(Value: false)])).Outcome);
    }

    [Fact]
    public void ASlotNameAlreadyInUseGetsAnotherName()
    {
        IrProcedure procedure = IrText.Parse("""
            proc "T::M(bool)" (%$s0: bool) entry B0
            B0:
              goto B1
            B1:
              br %$s0, B1, B2
            B2:
              ret
            """);

        IrProcedure segment = IrFragmenter.Segment(procedure, B1, Cuts(procedure));

        Assert.Equal(["$s0$", "$s0"], segment.Parameters.Select(static p => p.Var.Name), StringComparer.Ordinal);
        Assert.Empty(IrValidator.Validate(segment));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        IrProcedure single = IrText.Parse(LoopFixtures.Single);

        Assert.Throws<ArgumentNullException>(static () => IrFragmenter.State(null!, B0));
        Assert.Throws<ArgumentNullException>(() => IrFragmenter.State(single, null!));
        Assert.Throws<ArgumentNullException>(static () => IrFragmenter.Segment(null!, B0, []));
        Assert.Throws<ArgumentNullException>(() => IrFragmenter.Segment(single, B0, null!));
    }

    private static ImmutableArray<(IrBlockId Header, ImmutableArray<IrVar> State)> Cuts(IrProcedure procedure) =>
        [.. IrLoopAnalysis.Of(procedure).Loops.Select(l => (l.Header, IrFragmenter.State(procedure, l.Header)))];

    private static IrProcedure Load(string name) => IrText.Parse(name switch
    {
        nameof(LoopFixtures.Single) => LoopFixtures.Single,
        nameof(LoopFixtures.Nested) => LoopFixtures.Nested,
        nameof(LoopFixtures.EarlyReturn) => LoopFixtures.EarlyReturn,
        _ => LoopFixtures.Throws,
    });

    private static IrBitVecValue Bits(uint value) => new(32, value);
}
