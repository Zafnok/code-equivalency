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
/// and the ladder, not about the C# frontend (ADR 0015). Rung 4 applies only to call-free pairs (ticket P1-001), and
/// runs under a short budget in the properties of its own, over call-free procedures, where every rung runs on its
/// own: the M3-002 properties keep their generators and budget, with the whole ladder only over procedures that call or
/// apply a pure function, and monotonicity over rungs 1 to 3.
/// </summary>
public sealed class LadderPropertyTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    /// <summary>
    /// Half a second per query over generated call-free pairs, where rung 4 applies: a generated procedure's bitwise and
    /// nonlinear operations are any integer on each side, so over the integers Spacer finds spurious derivations and
    /// rung 4 falls back to the bitvectors, where most queries would use up a longer timeout. An Unknown verdict breaks
    /// none of these properties, and the mutation gate runs them for every mutant of the encoders (with one CsCheck
    /// thread, so at 10 s the call-free monotonicity property alone took 6 minutes).
    /// </summary>
    private static readonly VerificationOptions CallFreeOptions = new(3, 500, []);

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
    /// <c>Verify(P, Mutate(P))</c> is never Equivalent for 200 kept mutants of looping procedures that call or apply a pure
    /// function, whose witness input makes both runs terminate, and every Divergent verdict carries a replay whose two runs
    /// differ and complete. Every rung proves partial equivalence (VERIFICATION-MODEL.md section 5.1), so a mutant that
    /// differs only by not terminating (a flipped loop guard that never exits) may be Equivalent; rung 4 proves such pairs
    /// (ticket P1-001). <see cref="NoRungProvesATerminatingCallFreeMutant"/> covers the call-free procedures.
    /// </summary>
    [Fact]
    public void ALoopingMutantIsNeverEquivalentAndEveryDivergenceReplays()
    {
        Mutants(CallingLooping).Where(static m => Terminates(m.Original, m.Witness) && Terminates(m.Mutant, m.Witness)).Sample(
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
    /// Ladder monotonicity: with rungs 1 to 3 each run on its own, a pair one rung proves is refuted by no other, and every
    /// refutation replays; over 200 pairs, each a looping procedure with itself or with a kept mutant. Rung 4 is checked
    /// where it applies, by the call-free properties below.
    /// </summary>
    [Fact]
    public void NoRungRefutesAPairAnotherRungProves()
    {
        Gen<(IrProcedure Old, IrProcedure New)> pairs = Gen.Frequency(
            (1, IrGen.Procedure.Where(static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty).Select(static p => (p, p))),
            (1, Mutants(IrGen.Procedure.Where(static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty)).Select(static m => (m.Original, m.Mutant))));
        pairs.Sample(
            static pair =>
            {
                IReadOnlyList<LoopLadder.Rung> rungs = Independently(pair, Options, count: 3);
                bool proved = rungs.Any(static r => r.Step.Outcome == RungOutcome.Proved);
                Assert.False(proved && rungs.Any(static r => r.Step.Outcome == RungOutcome.Refuted), string.Join("; ", rungs.Select(static r => r.Step)));
            },
            iter: 200,
            print: static pair => $"{IrText.Dump(pair.Old)}\n{IrText.Dump(pair.New)}");
    }

    /// <summary>
    /// Where rung 4 applies (ticket P1-001): with every rung run on its own, none refutes a call-free looping procedure
    /// against itself, for 10 generated procedures. Every refutation is a replayed divergence, so this holds by
    /// construction; it runs rung 4 over generated procedures, where a crash would show.
    /// </summary>
    [Fact]
    public void NoRungRefutesACallFreeProcedureAgainstItself()
    {
        CallFreeLooping.Sample(
            static p => Assert.DoesNotContain(Independently((p, p), CallFreeOptions, count: 4), static r => r.Step.Outcome == RungOutcome.Refuted),
            iter: 10,
            print: IrText.Dump);
    }

    /// <summary>
    /// Where rung 4 applies (ticket P1-001): with every rung run on its own, none proves a call-free looping procedure's kept
    /// mutant whose witness makes both runs terminate, and every refutation replays, for 30 mutants. With the self-pairs
    /// above, this is also ladder monotonicity over call-free pairs.
    /// </summary>
    [Fact]
    public void NoRungProvesATerminatingCallFreeMutant()
    {
        Mutants(CallFreeLooping).Where(static m => Terminates(m.Original, m.Witness) && Terminates(m.Mutant, m.Witness)).Sample(
            static m => Assert.DoesNotContain(Independently((m.Original, m.Mutant), CallFreeOptions, count: 4), static r => r.Step.Outcome == RungOutcome.Proved),
            iter: 30,
            print: static m => $"{m.Description}\n{IrText.Dump(m.Original)}\n{IrText.Dump(m.Mutant)}");
    }

    /// <summary>Looping procedures that call or apply a pure function in a reachable block, which rung 4 does not apply to.</summary>
    private static Gen<IrProcedure> CallingLooping => IrGen.Procedure.Where(static p =>
        IrLoopAnalysis.Of(p) is { Loops.IsEmpty: false } analysis && analysis.ReversePostorder.SelectMany(static b => b.Instructions).Any(static i => i is IrCall or IrPure));

    private static Gen<IrProcedure> CallFreeLooping => IrGen.CallFreeProcedure.Where(static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty);

    private static Gen<IrMutant> Mutants(Gen<IrProcedure> procedures) =>
        procedures.SelectMany(IrGen.Mutation).Where(static m => m is not null).Select(static m => m!);

    /// <summary>The first <paramref name="count"/> rungs, each on its own, each refutation checked to replay.</summary>
    private static IReadOnlyList<LoopLadder.Rung> Independently((IrProcedure Old, IrProcedure New) pair, VerificationOptions options, int count)
    {
        IReadOnlyList<LoopLadder.Rung> rungs = [.. new LoopLadder(static () => new Context(), options).Independently(pair.Old, pair.New).Take(count)];
        Assert.Equal(count, rungs.Count);
        foreach (Divergent divergent in rungs.Select(static r => r.Verdict).OfType<Divergent>())
        {
            AssertReplays(divergent);
        }

        return rungs;
    }

    private static bool Terminates(IrProcedure procedure, IrInputs inputs) => IrGen.Run(procedure, inputs).Outcome is IrReturned or IrThrew;

    private static void AssertReplays(Divergent divergent)
    {
        Assert.NotEqual(divergent.Counterexample.Old, divergent.Counterexample.New);
        Assert.All([divergent.Counterexample.Old.Outcome, divergent.Counterexample.New.Outcome], static o => Assert.True(o is IrReturned or IrThrew, o.ToString()));
    }
}
