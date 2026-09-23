using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M3-002 criterion 4 on the real samples: the loops of <c>samples/identical</c> and <c>samples/renamed-locals</c>
/// are Equivalent by lockstep induction (unbounded), and <c>samples/loop-bound-change</c> is Divergent on rung 1.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LoopLadderSampleTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    [Theory]
    [InlineData("identical")]
    [InlineData("renamed-locals")]
    public void SampleLoopsAreEquivalentByLockstepInduction(string sample)
    {
        Verdict verdict = Verify(sample, "::SumTo(");

        Assert.Equal(new Equivalent(ProofMethod.LockstepInduction), verdict with { Ladder = [] });
        Assert.Equal(
            [(ProofMethod.Bounded, RungOutcome.Inconclusive), (ProofMethod.LockstepInduction, RungOutcome.Proved)],
            verdict.Ladder.Select(static s => (s.Rung, s.Outcome)));
    }

    [Fact]
    public void LoopBoundChangeIsDivergentOnRungOne()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify("loop-bound-change", "::SumUpTo("));

        Assert.Equal((ProofMethod.Bounded, RungOutcome.Refuted), (divergent.Ladder[^1].Rung, divergent.Ladder[^1].Outcome));
        Assert.NotEqual(divergent.Counterexample.Old.Outcome, divergent.Counterexample.New.Outcome);
        Assert.IsType<IrReturned>(divergent.Counterexample.Old.Outcome);
    }

    private static Verdict Verify(string sample, string member)
    {
        MatchResult result = new CSharpFrontend().Analyze(Solution(sample, "legacy"), Solution(sample, "modern"), EquivConfig.Default, TestContext.Current.CancellationToken).Match;
        ProcedurePair pair = result.Pairs.Single(p => p.New.Value.Contains(member, StringComparison.Ordinal));
        VerificationOptions options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, EquivConfig.Default.CallIdentityRenames);
        return new Z3Backend().Verify(pair.OldBody!, pair.NewBody!, options);
    }

    private static string Solution(string sample, string side) =>
        Directory.GetFiles(Path.Combine(SamplesRoot, sample, side), string.Equals(side, "legacy", StringComparison.Ordinal) ? "*.sln" : "*.slnx").Single();
}
