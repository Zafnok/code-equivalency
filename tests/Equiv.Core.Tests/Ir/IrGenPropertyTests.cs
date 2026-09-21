using System.Globalization;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.TestSupport;

using Xunit;

namespace Equiv.Core.Tests.Ir;

public sealed class IrGenPropertyTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryGeneratedProcedureValidatesClean()
    {
        IrGen.Procedure.Sample(static p => Assert.Empty(IrValidator.Validate(p)), iter: 500, print: IrText.Dump);
    }

    [Fact]
    public void EveryGeneratedProcedureRunsToAnExitWithinTheBudget()
    {
        Gen<(IrProcedure, IrInputs)> gen = IrGen.Procedure.SelectMany(static p => IrGen.Inputs(p).Select(i => (p, i)));
        gen.Sample(
            static (p, inputs) => Assert.IsNotType<IrBudgetExhausted>(IrGen.Run(p, inputs).Outcome),
            iter: 500);
    }

    [Fact]
    public void ParseInvertsDumpForGeneratedProcedures()
    {
        IrGen.Procedure.Sample(static p => Assert.Equal(p, IrText.Parse(IrText.Dump(p))), iter: 500, print: IrText.Dump);
    }

    [Fact]
    public void EveryViolationFailsWithExactlyTheExpectedId()
    {
        IrGen.Violations.Sample(
            static v => Assert.Equal([v.ExpectedId], IrValidator.Validate(v.Procedure).Select(static d => d.Id).Distinct(StringComparer.Ordinal), StringComparer.Ordinal),
            iter: 500,
            print: static v => v.ExpectedId + "\n" + IrText.Dump(v.Procedure));
    }

    [Fact]
    public void EveryViolationRuleIsGenerated()
    {
        HashSet<string> seen = new(IrGen.Violations.Array[200].Single().Select(static v => v.ExpectedId), StringComparer.Ordinal);
        Assert.Equal(10, seen.Count);
    }

    [Fact]
    public void EveryKeptMutationChangesTheRunOnItsWitness()
    {
        Gen<IrMutant?> gen = IrGen.Procedure.SelectMany(IrGen.Mutation);
        gen.Sample(
            static m => Assert.True(m is null || IrGen.Run(m.Original, m.Witness) != IrGen.Run(m.Mutant, m.Witness)),
            iter: 300);
    }

    [Fact]
    public void MutationDiscardRateIsReported()
    {
        IrMutant?[] sample = IrGen.Procedure.SelectMany(IrGen.Mutation).Array[400].Single();
        double discarded = sample.Count(static m => m is null) / (double)sample.Length;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Mutation discard rate: {discarded:P1} of {sample.Length}"));
        Assert.InRange(discarded, 0, 0.4);
    }

    /// <summary>Every block gets an "insert opaque" edit candidate; this checks it is not just generated but
    /// actually kept (i.e. the opaque call is observable) for at least one procedure.</summary>
    [Fact]
    public void AnInsertOpaqueMutationIsSometimesKept()
    {
        IrMutant?[] sample = IrGen.Procedure.SelectMany(IrGen.Mutation).Array[500].Single();
        Assert.Contains(sample, static m => m is not null && m.Description.StartsWith("insert opaque", StringComparison.Ordinal));
    }

    /// <summary><see cref="IrGen.Mutation"/> documents itself as covering bitvector and Bool parameters, but
    /// <see cref="IrGen.Procedure"/> only ever generates bv32/ref/map ones (see the generator's own doc
    /// comment), so nothing else in this file exercises a Bool parameter. Builds one directly, keeping the
    /// first two parameters bv32 so the "swap parameters a and b" edit stays type-safe.</summary>
    [Fact]
    public void MutationHandlesABoolParameter()
    {
        IrVar a = new("a", new IrBitVec(32));
        IrVar b = new("b", new IrBitVec(32));
        IrVar flag = new("flag", new IrBool());
        IrBlock entry = new(new IrBlockId(0), [], new IrReturn(a, []));
        IrProcedure procedure = new(
            new ProcedureIdentity("P"),
            [new IrParameter(a, IrParameterKind.In), new IrParameter(b, IrParameterKind.In), new IrParameter(flag, IrParameterKind.In)],
            new IrBitVec(32),
            [entry],
            new IrBlockId(0));

        IrMutant? mutant = IrGen.Mutation(procedure).Array[1].Single().Single();

        Assert.True(mutant is null || mutant.Witness.Arguments.Length == 3);
    }
}
