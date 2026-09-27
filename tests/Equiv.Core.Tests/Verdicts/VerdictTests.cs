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
        Assert.IsType<Verdict>(new Equivalent(ProofMethod.Bounded), exactMatch: false);
    }

    [Fact]
    public void DivergentCarriesACounterexample()
    {
        Counterexample counterexample = new(new IrInputs([]), SampleRun, SampleRun);
        Divergent verdict = new(counterexample);
        Assert.Same(counterexample, verdict.Counterexample);
        Assert.IsType<Verdict>(verdict, exactMatch: false);
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
        Assert.IsType<Verdict>(verdict, exactMatch: false);
    }

    [Theory]
    [InlineData(UnknownReason.Timeout)]
    [InlineData(UnknownReason.Opaque)]
    [InlineData(UnknownReason.UnmatchedOverload)]
    [InlineData(UnknownReason.UnalignedLoop)]
    [InlineData(UnknownReason.Recursion)]
    [InlineData(UnknownReason.Unbound)]
    [InlineData(UnknownReason.Abstraction)]
    [InlineData(UnknownReason.ChcTimeout)]
    [InlineData(UnknownReason.ChcSpurious)]
    public void EveryUnknownReasonRoundTripsThroughTheRecord(UnknownReason reason)
    {
        Assert.Equal(reason, new Unknown(reason, "detail").Reason);
    }

    /// <summary>Ticket P1-001: a rung 4 proof carries the coupling invariant Spacer found; no other proof has one.</summary>
    [Fact]
    public void AChcEquivalentCarriesItsInvariant()
    {
        Equivalent proved = new(ProofMethod.Chc) { Invariant = "(= old.i new.i)" };

        Assert.Equal("(= old.i new.i)", proved.Invariant);
        Assert.Null(new Equivalent(ProofMethod.Chc).Invariant);
        Assert.Equal(proved, new Equivalent(ProofMethod.Chc) { Invariant = "(= old.i new.i)" });
        Assert.NotEqual(proved, proved with { Invariant = "true" });
    }

    /// <summary>Ticket P1-001: the rung 4 step records whether Spacer ran over integers or bitvectors; no other step does.</summary>
    [Fact]
    public void ALadderStepCarriesTheChcModeItRanIn()
    {
        LadderStep step = new(ProofMethod.Chc, RungOutcome.Proved, "d") { Mode = ChcMode.Integers };

        Assert.Equal(ChcMode.Integers, step.Mode);
        Assert.Null(new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, "d").Mode);
        Assert.Equal(step, step with { });
        Assert.NotEqual(step, step with { Mode = ChcMode.BitVectors });
    }

    [Fact]
    public void EquivalentNamesItsProofMethodAndBound()
    {
        Equivalent bounded = new(ProofMethod.Bounded, BoundedBy: 3);

        Assert.Equal(ProofMethod.Bounded, bounded.Method);
        Assert.Equal(3, bounded.BoundedBy);
        Assert.Null(new Equivalent(ProofMethod.LockstepInduction).BoundedBy);
        Assert.NotEqual(new Equivalent(ProofMethod.LockstepInduction), new Equivalent(ProofMethod.KInduction));
    }

    [Fact]
    public void AVerdictHasNoLadderUnlessABackendRecordsOne()
    {
        Assert.Empty(new Added().Ladder);
    }

    [Fact]
    public void VerdictsCompareTheirLadderStructurally()
    {
        LadderStep step = new(ProofMethod.Bounded, RungOutcome.Inconclusive, "bound reachable");
        Unknown first = new Unknown(UnknownReason.UnalignedLoop, "d") with { Ladder = [step] };
        Unknown second = new Unknown(UnknownReason.UnalignedLoop, "d") with { Ladder = [step with { }] };

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Ladder = [] });
        Assert.NotEqual<Verdict>(new Added(), new Removed());
        Assert.False(first.Equals(Null.Of<Verdict>()));
    }

    [Fact]
    public void DependingOnNamesEachIdentityOnceAndPointsAtEverySpannedAbstraction()
    {
        Counterexample candidate = new(new IrInputs([]), SampleRun, SampleRun);
        SourceSpan span = new("New.cs", 4, 9, 4, 20);
        ImmutableArray<Abstraction> abstractions =
        [
            new(Codebase.Legacy, new CallIdentity("opaque:a"), Span: null),
            new(Codebase.Modern, new CallIdentity("opaque:b"), span),
            new(Codebase.Modern, new CallIdentity("opaque:a"), Span: null),
        ];

        Unknown unknown = Unknown.DependingOn(candidate, abstractions);

        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Equal("the divergence depends on opaque:a, opaque:b", unknown.Detail);
        Assert.Same(candidate, unknown.Candidate);
        Assert.Equal(abstractions, unknown.Abstractions);
        Assert.Equal([new UnknownCause(Codebase.Modern, "abstraction opaque:b", span)], unknown.Causes);
    }

    [Fact]
    public void AnUnknownHasNoCausesCandidateOrAbstractionsUnlessGiven()
    {
        Unknown unknown = new(UnknownReason.Timeout, "d");

        Assert.Empty(unknown.Causes);
        Assert.Null(unknown.Candidate);
        Assert.Empty(unknown.Abstractions);
    }

    [Fact]
    public void UnknownsCompareTheirCausesCandidateAndAbstractionsStructurally()
    {
        SourceSpan span = new("New.cs", 4, 9, 4, 20);
        Unknown first = Full(span);
        Unknown second = Full(span with { });

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Reason = UnknownReason.Opaque });
        Assert.NotEqual(first, second with { Detail = "other" });
        Assert.NotEqual(first, second with { Causes = [] });
        Assert.NotEqual(first, second with { Scope = UnknownScope.Line });
        Assert.NotEqual(first, second with { Candidate = null });
        Assert.NotEqual(first, second with { Abstractions = [] });
        Assert.NotEqual(first, second with { Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, "x")] });
        Assert.False(first.Equals(Null.Of<Unknown>()));
    }

    private static Unknown Full(SourceSpan span) =>
        new(UnknownReason.Abstraction, "d")
        {
            Causes = [new UnknownCause(Codebase.Modern, "lambda", span)],
            Candidate = new Counterexample(new IrInputs([]), SampleRun, SampleRun),
            Abstractions = [new Abstraction(Codebase.Legacy, new CallIdentity("opaque:a"), span)],
        };

    [Fact]
    public void AddedIsAVerdict()
    {
        Assert.IsType<Verdict>(new Added(), exactMatch: false);
    }

    [Fact]
    public void RemovedIsAVerdict()
    {
        Assert.IsType<Verdict>(new Removed(), exactMatch: false);
    }
}
