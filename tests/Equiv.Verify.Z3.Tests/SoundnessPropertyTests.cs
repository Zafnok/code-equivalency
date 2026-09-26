using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// The soundness harness of VERIFICATION-MODEL.md section 7 for the acyclic encoder (ticket M3-001
/// criteria 4, 8, 9 and 13; ADR 0021 for parameter sharing). It generates IR, so it is evidence about this encoder (and, from M3-002,
/// the ladder), not about the C# frontend: a C#-to-IR lowering gap is invisible to it by construction,
/// and the two section 2 heap gaps (ADR 0015; tickets P1-005 and P1-006) are outside it.
/// </summary>
public sealed class SoundnessPropertyTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    /// <summary><c>Verify(P, P)</c> is Equivalent for 200 generated acyclic procedures.</summary>
    [Fact]
    public void AProcedureIsEquivalentToItself()
    {
        IrGen.AcyclicProcedure.Sample(
            static p => Assert.IsType<Equivalent>(new Z3Backend().Verify(p, p, Options)),
            iter: 200,
            print: IrText.Dump);
    }

    /// <summary>
    /// <c>Verify(P, Rename(P))</c> is Equivalent for 200 generated acyclic procedures whose source-language
    /// parameters are renamed on one side: inputs are shared by position, not by name (ADR 0021).
    /// </summary>
    [Fact]
    public void RenamingTheParametersKeepsAProcedureEquivalent()
    {
        IrGen.AcyclicRenamedPair.Sample(
            static pair => Assert.IsType<Equivalent>(new Z3Backend().Verify(pair.Original, pair.Renamed, Options)),
            iter: 200,
            print: static pair => IrText.Dump(pair.Original) + "\n" + IrText.Dump(pair.Renamed));
    }

    /// <summary>
    /// <c>Verify(P, Mutate(P))</c> is never Equivalent for 200 kept mutants (the mutation includes swapping
    /// two parameters in the signature, inserting an opaque, dropping or changing a heap write, and
    /// duplicating a call), and every Divergent verdict
    /// carries the backend's <see cref="IrInterpreter"/> replay, whose two runs differ.
    /// </summary>
    [Fact]
    public void AMutantIsNeverEquivalentAndEveryDivergenceReplays()
    {
        IrGen.AcyclicProcedure.SelectMany(IrGen.Mutation).Where(static m => m is not null).Sample(
            static m =>
            {
                Verdict verdict = new Z3Backend().Verify(m!.Original, m.Mutant, Options);
                Assert.IsNotType<Equivalent>(verdict);
                if (verdict is Divergent divergent)
                {
                    Assert.NotEqual(divergent.Counterexample.Old, divergent.Counterexample.New);
                }
            },
            iter: 200,
            print: static m => $"{m!.Description}\n{IrText.Dump(m.Original)}\n{IrText.Dump(m.Mutant)}");
    }

    /// <summary>
    /// Every mutation kind is generated on acyclic procedures. It draws until every kind has been seen, up to
    /// <see cref="MaxMutationDraws"/> draws. Over 20,000 measured draws the rarest kinds, drop map and change map,
    /// were each about 0.8% of draws, so a correct generator misses one within the bound with probability about
    /// 2e-14. A fixed 600-draw sample missed one about 1.5% of the time.
    /// </summary>
    [Fact]
    public void EveryMutationKindIsGeneratedOnAcyclicProcedures()
    {
        HashSet<string> expected = new(["swap parameters", "swap operands", "flip branch", "change constant", "insert opaque", "drop map", "change map", "duplicate call"], StringComparer.Ordinal);
        HashSet<string> kinds = new(StringComparer.Ordinal);
        Gen<IrMutant?> mutants = IrGen.AcyclicProcedure.SelectMany(IrGen.Mutation);

        for (int draw = 0; draw < MaxMutationDraws && !kinds.IsSupersetOf(expected); draw++)
        {
            if (mutants.Single() is { } mutant)
            {
                kinds.Add(string.Join(' ', mutant.Description.Split(' ').Take(2)));
            }
        }

        Assert.Superset(expected, kinds);
    }

    /// <summary>
    /// The residual claim of VERIFICATION-MODEL.md section 7 (ticket M3-025; ADR 0029 decision 4): for a line-scoped
    /// Unknown, every generated input on which neither side reaches a listed cause gives equal observables in
    /// <see cref="IrInterpreter"/>, and a cause a run does reach is always listed. Pairs are a generated procedure against
    /// one or two stacked mutants, so an inserted opaque often sits beside a second change. A fragment both sides share is a
    /// call, not a cause (ticket M4-004), so the runs replay the pair as the backend encodes it.
    /// </summary>
    [Fact]
    public void LineScopedResidualClaimHolds()
    {
        int lineScoped = 0;
        IrGen.AcyclicProcedure
            .SelectMany(IrGen.Mutation)
            .Where(static m => m is not null)
            .SelectMany(static m => IrGen.Mutation(m!.Mutant).Select(second => (m.Original, New: second?.Mutant ?? m.Mutant)))
            .SelectMany(static pair => IrGen.Inputs(pair.Original).Array[32].Select(inputs => (pair.Original, pair.New, Inputs: inputs)))
            .Sample(
                sample =>
                {
                    if (new Z3Backend().Verify(sample.Original, sample.New, Options) is not Unknown { Scope: UnknownScope.Line } unknown)
                    {
                        return;
                    }

                    lineScoped++;
                    HashSet<SourceSpan> causes = [.. unknown.Causes.Select(static c => c.Span)];
                    (IrProcedure original, IrProcedure changed, _) = ProductEncoder.ShareFragments(sample.Original, sample.New);
                    foreach (IrInputs inputs in sample.Inputs)
                    {
                        IrRun old = IrGen.Run(original, inputs);
                        IrRun @new = IrGen.Run(changed, inputs);
                        if (old.Outcome is IrOpaqueReached || @new.Outcome is IrOpaqueReached)
                        {
                            Assert.All(new[] { old.Outcome, @new.Outcome }.OfType<IrOpaqueReached>(), reached => Assert.Contains(reached.Span, causes));
                            continue;
                        }

                        Assert.Equal(old, @new);
                    }
                },
                iter: 200,
                print: static sample => IrText.Dump(sample.Original) + "\n" + IrText.Dump(sample.New));

        Assert.True(lineScoped > 0, "no generated pair was a line-scoped Unknown");
    }

    private const int MaxMutationDraws = 4000;
}
