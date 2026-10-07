using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;
using Equiv.Verify.Z3.Refinement;

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

    /// <summary>
    /// How many procedures <see cref="FirstOf"/> draws for one kept one. About 1 in 8 generated procedures both loops and
    /// calls, so one batch in 8 has none, and CsCheck's <c>Where</c> gives up only after 100 batches in a row have none.
    /// </summary>
    private const int Batch = 16;

    /// <summary><c>Verify(P, P)</c> is Equivalent for 200 generated looping procedures, bounded only when a bound covers every loop.</summary>
    [Fact]
    public void ALoopingProcedureIsEquivalentToItself()
    {
        Looping.Sample(
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
        TerminatingCallingMutants.Sample(static m => AssertNeverEquivalent(m), iter: 200, print: Print);
    }

    /// <summary>
    /// The seed on which the property above failed with no assertion (ticket P2-129): 100 generated procedures in a row
    /// did not both loop and call, and CsCheck's <c>Where</c> gave up ("Failing Where max count").
    /// </summary>
    [Fact]
    public void TheCallingMutantGeneratorDoesNotRunOutOnSeedA6AU7haBK73()
    {
        TerminatingCallingMutants.Sample(static m => AssertNeverEquivalent(m), iter: 1, seed: "a6-aU7haBK73", print: Print);
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
            (1, Looping.Select(static p => (p, p))),
            (1, Mutants(Looping).Select(static m => (m.Original, m.Mutant))));
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
    /// Ticket P1-031 criterion 5 over loops: rung 1 on its own, asked on the product with its hard arithmetic abstracted
    /// for every pair. It proves no kept mutant whose witness makes both runs terminate, refutes no looping procedure
    /// against itself, and every refutation replays; over 100 pairs.
    /// </summary>
    [Fact]
    public void RungOneOnTheAbstractedProductProvesNoTerminatingMutantAndRefutesNoSelfPair()
    {
        Gen<(IrProcedure Old, IrProcedure New, bool Mutant)> pairs = Gen.Frequency(
            (1, Looping.Select(static p => (p, p, false))),
            (1, Mutants(Looping).Where(static m => Terminates(m.Original, m.Witness) && Terminates(m.Mutant, m.Witness)).Select(static m => (m.Original, m.Mutant, true))));
        pairs.Sample(
            static pair =>
            {
                LoopLadder.Rung rung = new LoopLadder(static () => new Context(), Options) { Arithmetic = ArithmeticMode.Forced }.Independently(pair.Old, pair.New).First();
                Assert.NotEqual(pair.Mutant ? RungOutcome.Proved : RungOutcome.Refuted, rung.Step.Outcome);
                if (rung.Verdict is Divergent divergent)
                {
                    AssertReplays(divergent);
                }
            },
            iter: 100,
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
            static p => Assert.DoesNotContain(Independently((p, p), CallFreeOptions), static r => r.Step.Outcome == RungOutcome.Refuted),
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
        TerminatingCallFreeMutants.Sample(static m => Assert.DoesNotContain(Rungs(m), static r => r.Step.Outcome == RungOutcome.Proved), iter: 30, print: Print);
    }

    /// <summary>
    /// The seed on which rung 4 proved a mutant that rungs 1 and 3 refute (ticket P2-059): Spacer's answer did not solve
    /// the clauses. <c>Fixtures/loops/chc-uncertified.ir</c> holds the pair.
    /// </summary>
    [Fact]
    public void NoRungProvesTheMutantOfSeed4FfExD8adOs4()
    {
        TerminatingCallFreeMutants.Sample(static m => Assert.DoesNotContain(Rungs(m), static r => r.Step.Outcome == RungOutcome.Proved), iter: 1, seed: "4FfExD8adOs4", print: Print);
    }

    /// <summary>Looping procedures that call or apply a pure function in a reachable block, which rung 4 does not apply to.</summary>
    private static Gen<IrProcedure> CallingLooping => FirstOf(IrGen.Procedure, static p =>
        IrLoopAnalysis.Of(p) is { Loops.IsEmpty: false } analysis && analysis.ReversePostorder.SelectMany(static b => b.Instructions).Any(static i => i is IrCall or IrPure));

    private static Gen<IrProcedure> Looping => FirstOf(IrGen.Procedure, static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty);

    /// <summary>
    /// About 1 in 4 call-free procedures loops, so 100 in a row without a loop is not a risk (ticket P2-129), and
    /// <see cref="NoRungProvesTheMutantOfSeed4FfExD8adOs4"/> needs this generator's stream as it is.
    /// </summary>
    private static Gen<IrProcedure> CallFreeLooping => IrGen.CallFreeProcedure.Where(static p => !IrLoopAnalysis.Of(p).Loops.IsEmpty);

    /// <summary>Kept mutants of looping procedures that call or apply a pure function, whose witness makes both runs terminate.</summary>
    private static Gen<IrMutant> TerminatingCallingMutants =>
        Mutants(CallingLooping).Where(static m => Terminates(m.Original, m.Witness) && Terminates(m.Mutant, m.Witness));

    /// <summary>Call-free looping procedures' kept mutants whose witness makes both runs terminate.</summary>
    private static Gen<IrMutant> TerminatingCallFreeMutants =>
        Mutants(CallFreeLooping).Where(static m => Terminates(m.Original, m.Witness) && Terminates(m.Mutant, m.Witness));

    private static IReadOnlyList<LoopLadder.Rung> Rungs(IrMutant m) => Independently((m.Original, m.Mutant), CallFreeOptions);

    private static string Print(IrMutant m) => $"{m.Description}\n{IrText.Dump(m.Original)}\n{IrText.Dump(m.Mutant)}";

    /// <summary>
    /// The first of <see cref="Batch"/> draws of <paramref name="procedures"/> that <paramref name="keep"/> holds for:
    /// the procedures <c>procedures.Where(keep)</c> draws, each as likely as there. CsCheck's <c>Where</c> throws after
    /// 100 rejections in a row, which a filter that keeps 1 draw in 8 reaches about once in 400 runs of a property
    /// (ticket P2-129); here a rejection is a whole batch with nothing to keep.
    /// </summary>
    private static Gen<IrProcedure> FirstOf(Gen<IrProcedure> procedures, Func<IrProcedure, bool> keep) =>
        procedures.Array[Batch].Select(batch => batch.FirstOrDefault(keep)).Where(static p => p is not null).Select(static p => p!);

    private static void AssertNeverEquivalent(IrMutant m)
    {
        Verdict verdict = new Z3Backend().Verify(m.Original, m.Mutant, Options);
        Assert.IsNotType<Equivalent>(verdict);
        if (verdict is Divergent divergent)
        {
            AssertReplays(divergent);
        }
    }

    private static Gen<IrMutant> Mutants(Gen<IrProcedure> procedures) =>
        procedures.SelectMany(IrGen.Mutation).Where(static m => m is not null).Select(static m => m!);

    /// <summary>Every rung on its own, or the first <paramref name="count"/>, each refutation checked to replay.</summary>
    private static IReadOnlyList<LoopLadder.Rung> Independently((IrProcedure Old, IrProcedure New) pair, VerificationOptions options, int count = 4)
    {
        IEnumerable<LoopLadder.Rung> all = new LoopLadder(static () => new Context(), options).Independently(pair.Old, pair.New);
        IReadOnlyList<LoopLadder.Rung> rungs = [.. count < 4 ? all.Take(count) : all];
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
