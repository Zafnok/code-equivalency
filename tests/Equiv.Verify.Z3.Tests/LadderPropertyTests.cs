using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// The soundness harness of VERIFICATION-MODEL.md section 7 extended to looping procedures, and ladder monotonicity
/// (ticket M3-002 criteria 2 and 5). Generated procedures have counted loops, nested and inside branches, over a
/// <c>ref</c> heap map and opaque calls. Like <see cref="SoundnessPropertyTests"/>, this is evidence about the encoder
/// and the ladder, not about the C# frontend (ADR 0015).
/// </summary>
public sealed class LadderPropertyTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    /// <summary><c>Verify(P, P)</c> is Equivalent for 200 generated looping procedures, bounded only when a bound covers every loop.</summary>
    [Fact]
    public void ALoopingProcedureIsEquivalentToItself()
    {
        Gen<IrProcedure> looping = IrGen.Procedure.Where(static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty);
        looping.Sample(
            static p =>
            {
                Equivalent equivalent = Assert.IsType<Equivalent>(new Z3Backend().Verify(p, p, Options));
                Assert.Equal(equivalent.Method == ProofMethod.Bounded ? Options.Bound : null, equivalent.BoundedBy);
            },
            iter: 200,
            print: IrText.Dump);
    }

    /// <summary>
    /// <c>Verify(P, Mutate(P))</c> is never Equivalent for 200 kept mutants of looping procedures, and every Divergent
    /// verdict carries a replay whose two runs differ and complete.
    /// </summary>
    [Fact]
    public void ALoopingMutantIsNeverEquivalentAndEveryDivergenceReplays()
    {
        Mutants().Sample(
            static m =>
            {
                Verdict verdict = new Z3Backend().Verify(m.Original, m.Mutant, Options);
                Assert.IsNotType<Equivalent>(verdict);
                if (verdict is Divergent divergent)
                {
                    AssertReplays(divergent);
                }
            },
            iter: 200,
            print: static m => $"{m.Description}\n{IrText.Dump(m.Original)}\n{IrText.Dump(m.Mutant)}");
    }

    /// <summary>
    /// Ladder monotonicity: with every rung run on its own, a pair one rung proves is refuted by no other, and every
    /// refutation replays; over 200 pairs, each a looping procedure with itself or with a kept mutant.
    /// </summary>
    [Fact]
    public void NoRungRefutesAPairAnotherRungProves()
    {
        Gen<(IrProcedure Old, IrProcedure New)> pairs = Gen.Frequency(
            (1, IrGen.Procedure.Where(static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty).Select(static p => (p, p))),
            (1, Mutants().Select(static m => (m.Original, m.Mutant))));
        pairs.Sample(
            static pair =>
            {
                IReadOnlyList<LoopLadder.Rung> rungs = new LoopLadder(static () => new Context(), Options).Independently(pair.Old, pair.New);
                Assert.Equal(3, rungs.Count);
                bool proved = rungs.Any(static r => r.Step.Outcome == RungOutcome.Proved);
                Assert.False(proved && rungs.Any(static r => r.Step.Outcome == RungOutcome.Refuted), string.Join("; ", rungs.Select(static r => r.Step)));
                foreach (Divergent divergent in rungs.Select(static r => r.Verdict).OfType<Divergent>())
                {
                    AssertReplays(divergent);
                }
            },
            iter: 200,
            print: static pair => $"{IrText.Dump(pair.Old)}\n{IrText.Dump(pair.New)}");
    }

    private static Gen<IrMutant> Mutants() =>
        IrGen.Procedure.Where(static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty).SelectMany(IrGen.Mutation).Where(static m => m is not null).Select(static m => m!);

    private static void AssertReplays(Divergent divergent)
    {
        Assert.NotEqual(divergent.Counterexample.Old, divergent.Counterexample.New);
        Assert.All(new[] { divergent.Counterexample.Old.Outcome, divergent.Counterexample.New.Outcome }, static o => Assert.True(o is IrReturned or IrThrew, o.ToString()));
    }
}
