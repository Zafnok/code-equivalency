using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Every fixture under <c>Fixtures/loops/</c> takes the verdict path its first comment line names (ticket M3-002
/// criteria 1 and 3): <c>Equivalent(&lt;proofMethod&gt;)</c>, <c>Divergent(&lt;rung that refuted it&gt;)</c> with a
/// counterexample whose replay diverges, or <c>Unknown(&lt;reason&gt;)</c>.
/// </summary>
public sealed class LadderFixtureTests
{
    public static TheoryData<string> Names =>
    [
        "aligned-unchanged", "loop-bound-change", "warm-up", "loop-to-linq", "recursion-unaligned",
        "loop-break-return", "loop-break-return-mutant", "loop-invariant-livein", "loop-invariant-livein-mutant",
        "nested-aligned", "nesting-changed", "late-divergence", "late-divergence-beyond", "constant-loop-prefix-change",
        "phis-reordered", "state-unpaired", "irreducible", "loop-opaque", "loop-hard",
        "recursion-aligned", "recursion-divergent", "recursion-heap",
    ];

    [Theory]
    [MemberData(nameof(Names))]
    public void FixtureTakesTheVerdictPathItsFirstLineNames(string name)
    {
        Fixture fixture = Fixture.Load("loops/" + name);

        Verdict verdict = Verify(fixture);

        Assert.Equal(fixture.Expected, Describe(verdict));
        Assert.NotEmpty(verdict.Ladder);
        Assert.Equal(ProofMethod.Bounded, verdict.Ladder[0].Rung);
        if (verdict is Divergent divergent)
        {
            Assert.NotEqual(divergent.Counterexample.Old, divergent.Counterexample.New);
            Assert.All(new[] { divergent.Counterexample.Old, divergent.Counterexample.New }, static r => Assert.IsType<IrOutcome>(r.Outcome, exactMatch: false));
            Assert.All(new[] { divergent.Counterexample.Old.Outcome, divergent.Counterexample.New.Outcome }, static o => Assert.True(o is IrReturned or IrThrew, o.ToString()));
        }
    }

    [Fact]
    public void EveryFixtureFileIsListed()
    {
        IEnumerable<string> files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "loops"), "*.ir").Select(Path.GetFileNameWithoutExtension)!;

        Assert.Equal(files.Order(StringComparer.Ordinal), Names.Select(static row => row.Data).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ABoundedProofOverALoopNamesItsBound()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::Three()" () -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              %two: bv32 = const bv32 2
              goto B1
            B1:
              %i: bv32 = phi [B0: %z, B2: %i1]
              %c: bool = slt %i, %two
              br %c, B2, B3
            B2:
              %i1: bv32 = add %i, %one
              goto B1
            B3:
              ret %i
            ---
            proc "T::Three()" () -> bv32 entry B0
            B0:
              %two: bv32 = const bv32 2
              ret %two
            """);

        Verdict verdict = new Z3Backend().Verify(old, @new, new VerificationOptions(3, 10_000, []));

        Assert.Equal(new Equivalent(ProofMethod.Bounded, BoundedBy: 3), verdict with { Ladder = [] });
        Assert.Equal([RungOutcome.Proved], verdict.Ladder.Select(static s => s.Outcome));
    }

    [Fact]
    public void AWarmUpLoopNeedsTheThirdRung()
    {
        Verdict verdict = Verify(Fixture.Load("loops/warm-up"));

        Assert.Equal(
            [
                (ProofMethod.Bounded, RungOutcome.Inconclusive),
                (ProofMethod.LockstepInduction, RungOutcome.Inconclusive),
                (ProofMethod.KInduction, RungOutcome.Proved),
            ],
            verdict.Ladder.Select(static s => (s.Rung, s.Outcome)));
        Assert.Equal("the step obligation of loop 1 fails", verdict.Ladder[1].Detail);
    }

    [Fact]
    public void TimeoutsOnEveryRungAreUnknownTimeout()
    {
        Unknown unknown = Assert.IsType<Unknown>(Verify(Fixture.Load("loops/loop-hard")));

        Assert.StartsWith("the step obligation of loop 1: solver returned unknown (", unknown.Detail, StringComparison.Ordinal);
        Assert.Equal(
            [RungOutcome.Timeout, RungOutcome.Timeout, RungOutcome.NotApplicable],
            unknown.Ladder.Select(static s => s.Outcome));
    }

    /// <summary>A fixture that expects a timeout gets 50 ms; every other one gets ten seconds.</summary>
    private static Verdict Verify(Fixture fixture) =>
        new Z3Backend().Verify(fixture.Old, fixture.New, new VerificationOptions(3, string.Equals(fixture.Expected, "Unknown(Timeout)", StringComparison.Ordinal) ? 50 : 10_000, []));

    private static string Describe(Verdict verdict) => verdict switch
    {
        Equivalent equivalent => $"Equivalent({Name(equivalent.Method)})",
        Divergent => $"Divergent({Name(verdict.Ladder[^1].Rung)})",
        Unknown unknown => $"Unknown({unknown.Reason})",
        _ => verdict.GetType().Name,
    };

    private static string Name(ProofMethod method) => method switch
    {
        ProofMethod.Bounded => "bounded",
        ProofMethod.LockstepInduction => "lockstep-induction",
        _ => "k-induction",
    };
}
