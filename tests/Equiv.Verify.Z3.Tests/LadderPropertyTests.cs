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

    /// <summary>
    /// Rung 4 gets a second per query when it runs over generated call-free pairs: a generated procedure's bitwise and
    /// nonlinear operations are any integer on each side, so over the integers Spacer finds spurious derivations and
    /// rung 4 falls back to the bitvectors, where most queries would use up a longer timeout. An Unknown verdict breaks
    /// none of these properties.
    /// </summary>
    private static readonly VerificationOptions RungFourOptions = new(3, 1_000, []);

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
    /// <c>Verify(P, Mutate(P))</c> is never Equivalent for 200 kept mutants of looping procedures whose witness input makes
    /// both runs terminate, and every Divergent verdict carries a replay whose two runs differ and complete. Every rung
    /// proves partial equivalence (VERIFICATION-MODEL.md section 5.1), so a mutant that differs only by not terminating
    /// (a flipped loop guard that never exits) may be Equivalent; rung 4 proves such pairs (ticket P1-001).
    /// </summary>
    [Fact]
    public void ALoopingMutantIsNeverEquivalentAndEveryDivergenceReplays()
    {
        Mutants().Where(static m => Terminates(m.Original, m.Witness) && Terminates(m.Mutant, m.Witness)).Sample(
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
    /// Rung 4 on its own, over the call-free procedures it applies to (ticket P1-001): a looping procedure is never
    /// Divergent against itself, for 50 generated procedures.
    /// </summary>
    [Fact]
    public void RungFourNeverRefutesAProcedureAgainstItself()
    {
        CallFreeLooping.Sample(
            static p => Assert.IsNotType<Divergent>(RungFour(p, p).Verdict),
            iter: 50,
            print: IrText.Dump);
    }

    /// <summary>
    /// Rung 4 on its own (ticket P1-001): a call-free looping procedure's kept mutant whose witness makes both runs
    /// terminate is never Equivalent, and every Divergent verdict replays, for 100 mutants.
    /// </summary>
    [Fact]
    public void RungFourNeverProvesATerminatingMutant()
    {
        Mutants(CallFreeLooping).Where(static m => Terminates(m.Original, m.Witness) && Terminates(m.Mutant, m.Witness)).Sample(
            static m =>
            {
                Verdict? verdict = RungFour(m.Original, m.Mutant).Verdict;
                Assert.IsNotType<Equivalent>(verdict);
                if (verdict is Divergent divergent)
                {
                    AssertReplays(divergent);
                }
            },
            iter: 100,
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
        pairs.Sample(static pair => AssertMonotone(pair, Options), iter: 200, print: static pair => $"{IrText.Dump(pair.Old)}\n{IrText.Dump(pair.New)}");
    }

    /// <summary>
    /// Ladder monotonicity where rung 4 applies (ticket P1-001): as <see cref="NoRungRefutesAPairAnotherRungProves"/>, over
    /// 50 call-free pairs.
    /// </summary>
    [Fact]
    public void NoRungRefutesACallFreePairAnotherRungProves()
    {
        Gen<(IrProcedure Old, IrProcedure New)> pairs = Gen.Frequency(
            (1, CallFreeLooping.Select(static p => (p, p))),
            (1, Mutants(CallFreeLooping).Select(static m => (m.Original, m.Mutant))));
        pairs.Sample(static pair => AssertMonotone(pair, RungFourOptions), iter: 50, print: static pair => $"{IrText.Dump(pair.Old)}\n{IrText.Dump(pair.New)}");
    }

    private static Gen<IrProcedure> CallFreeLooping => IrGen.CallFreeProcedure.Where(static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty);

    private static Gen<IrMutant> Mutants() => Mutants(IrGen.Procedure.Where(static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty));

    private static Gen<IrMutant> Mutants(Gen<IrProcedure> procedures) =>
        procedures.SelectMany(IrGen.Mutation).Where(static m => m is not null).Select(static m => m!);

    private static LoopLadder.Rung RungFour(IrProcedure old, IrProcedure @new) => new SpacerRung(static () => new Context(), RungFourOptions).Prove(old, @new);

    private static void AssertMonotone((IrProcedure Old, IrProcedure New) pair, VerificationOptions options)
    {
        IReadOnlyList<LoopLadder.Rung> rungs = new LoopLadder(static () => new Context(), options).Independently(pair.Old, pair.New);
        Assert.Equal(4, rungs.Count);
        bool proved = rungs.Any(static r => r.Step.Outcome == RungOutcome.Proved);
        Assert.False(proved && rungs.Any(static r => r.Step.Outcome == RungOutcome.Refuted), string.Join("; ", rungs.Select(static r => r.Step)));
        foreach (Divergent divergent in rungs.Select(static r => r.Verdict).OfType<Divergent>())
        {
            AssertReplays(divergent);
        }
    }

    private static bool Terminates(IrProcedure procedure, IrInputs inputs) => IrGen.Run(procedure, inputs).Outcome is IrReturned or IrThrew;

    private static void AssertReplays(Divergent divergent)
    {
        Assert.NotEqual(divergent.Counterexample.Old, divergent.Counterexample.New);
        Assert.All([divergent.Counterexample.Old.Outcome, divergent.Counterexample.New.Outcome], static o => Assert.True(o is IrReturned or IrThrew, o.ToString()));
    }
}
