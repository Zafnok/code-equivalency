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

    [Fact]
    public void EveryMutationKindIsGeneratedOnAcyclicProcedures()
    {
        IrMutant?[] sample = IrGen.AcyclicProcedure.SelectMany(IrGen.Mutation).Array[600].Single();

        HashSet<string> kinds = new(
            sample.OfType<IrMutant>().Select(static m => string.Join(' ', m.Description.Split(' ').Take(2))),
            StringComparer.Ordinal);

        Assert.Superset(
            new HashSet<string>(["swap parameters", "swap operands", "flip branch", "change constant", "insert opaque", "drop map", "change map", "duplicate call"], StringComparer.Ordinal),
            kinds);
    }
}
