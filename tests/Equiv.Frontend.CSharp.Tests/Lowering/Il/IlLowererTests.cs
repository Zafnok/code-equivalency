using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;
using Equiv.TestSupport;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.IL;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering.Il;

/// <summary>Ticket P1-014: the IL lowering of a method read back from its compilation's IL (ADR 0039).</summary>
public sealed class IlLowererTests
{
    /// <summary>Acceptance criterion 3: every sample method with a body lowers to IR that validates.</summary>
    [Fact]
    public void EverySampleMethodLowersToValidIr()
    {
        List<string> invalid = [];
        foreach ((string sample, Compilation compilation) in IlSamples.All)
        {
            foreach (EnumeratedProcedure procedure in ProcedureEnumerator.Enumerate(compilation))
            {
                ImmutableArray<IrDiagnostic> diagnostics = IrValidator.Validate(IlLowerer.Lower(procedure.Symbol, compilation));
                invalid.AddRange(diagnostics.Select(d => $"{sample} {procedure.Identity.Value}: {d.Message}"));
            }
        }

        Assert.True(invalid.Count == 0, string.Join('\n', invalid));
    }

    /// <summary>
    /// Acceptance criterion 3: in every sample method, each instruction with an unmapped key that no unmapped instruction
    /// holds is an opaque whose reason is that key, and every opaque's reason is a key of the method's ILAst.
    /// </summary>
    [Fact]
    public void UnmappedInstructionsAreOpaqueWithTheirKey()
    {
        List<string> wrong = [];
        foreach ((string sample, Compilation compilation) in IlSamples.All)
        {
            foreach (EnumeratedProcedure procedure in ProcedureEnumerator.Enumerate(compilation))
            {
                ILFunction function = IlAstReader.Read(procedure.Symbol, compilation).Function!;
                HashSet<ILVariable> caught = IlKeys.CaughtException(function);
                ImmutableHashSet<string> keys = [.. function.Descendants.Select(i => IlKeys.Key(i, caught))];
                ImmutableHashSet<string> unmapped = [.. function.Descendants.Where(i => Outermost(i, caught)).Select(i => IlKeys.Key(i, caught))];
                ImmutableHashSet<string> reasons = [.. Opaques(IlLowerer.Lower(procedure.Symbol, compilation)).Select(static o => o.Reason)];
                wrong.AddRange(unmapped.Except(reasons).Select(k => $"{sample} {procedure.Identity.Value}: {k} is not an opaque"));
                wrong.AddRange(reasons.Except(keys).Remove("undefined").Select(r => $"{sample} {procedure.Identity.Value}: {r} is no key of its ILAst"));
            }
        }

        Assert.True(wrong.Count == 0, string.Join('\n', wrong));
    }

    /// <summary>
    /// Acceptance criterion 4: a sample method the IOperation lowering lowers with no opaque and whose ILAst has only this
    /// ticket's keys names the same callees in the same order, and the same parameters with the same sorts, both ways.
    /// </summary>
    [Fact]
    public void CallIdentitiesMatchTheOperationLowering()
    {
        List<string> different = [];
        int compared = 0;
        foreach ((string sample, Compilation compilation) in IlSamples.All)
        {
            foreach (EnumeratedProcedure procedure in ProcedureEnumerator.Enumerate(compilation))
            {
                IrProcedure operation = IrLowerer.Lower(procedure.Symbol, compilation, RenameMap.Empty, []);
                ILFunction function = IlAstReader.Read(procedure.Symbol, compilation).Function!;
                HashSet<ILVariable> caught = IlKeys.CaughtException(function);
                if (!Opaques(operation).IsEmpty || !function.Descendants.All(i => IlKeys.Lowered.Contains(IlKeys.Key(i, caught))))
                {
                    continue;
                }

                compared++;
                IrProcedure il = IlLowerer.Lower(procedure.Symbol, compilation);
                string expected = Signature(operation);
                string actual = Signature(il);
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    different.Add($"{sample} {procedure.Identity.Value}:\n  operation {expected}\n  il        {actual}\n{IrText.Dump(il)}");
                }
            }
        }

        Assert.True(different.Count == 0, string.Join('\n', different));
        Assert.True(compared > 10, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"only {compared} sample methods compared"));
    }

    /// <summary>The Design's pitfall: the instruction's sign decides the division, not its operands' C# types.</summary>
    [Fact]
    public void UnsignedDivisionFollowsTheInstructionSign()
    {
        IrProcedure procedure = Lower("static int M(int a, int b) => (int)((uint)a / (uint)b);");

        Assert.Contains(procedure.Blocks.SelectMany(static b => b.Instructions), static i => i is IrBinary { Op: IrBinaryOp.UDiv });
        Assert.Equal(new IrReturned(IrBitVecValue.FromSigned(32, int.MaxValue)), Run(procedure, IrBitVecValue.FromSigned(32, -1), IrBitVecValue.FromSigned(32, 2)));
    }

    internal static IrProcedure Lower(string members, string name = "M")
    {
        Compilation compilation = RoslynTestCompilations.Compile($"using System;\nclass C\n{{\n{members}\n}}\n");
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IrProcedure procedure = IlLowerer.Lower(compilation.GetTypeByMetadataName("C")!.GetMembers(name).OfType<IMethodSymbol>().Single(), compilation);
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    internal static IrOutcome Run(IrProcedure procedure, params IrValue[] arguments) =>
        IrInterpreter.Run(procedure, new IrInputs([.. arguments]), IrGenOracle.Instance, IrGen.StepBudget).Outcome;

    internal static ImmutableArray<IrOpaque> Opaques(IrProcedure procedure) =>
        [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>()];

    /// <summary>An instruction with an unmapped key that no instruction with an unmapped key holds.</summary>
    private static bool Outermost(ILInstruction instruction, HashSet<ILVariable> caught) =>
        !IlKeys.Lowered.Contains(IlKeys.Key(instruction, caught))
        && !instruction.Ancestors.Skip(1).Any(a => !IlKeys.Lowered.Contains(IlKeys.Key(a, caught)));

    /// <summary>
    /// Each path's callees in order, as a sorted set of sequences, so that two lowerings that lay out the same branches
    /// differently (ILSpy may negate a condition and swap its arms) agree; then each parameter's name and sort. A path is cut
    /// where it would enter a block it has already entered.
    /// </summary>
    private static string Signature(IrProcedure procedure)
    {
        Dictionary<IrBlockId, IrBlock> blocks = procedure.Blocks.ToDictionary(static b => b.Id);
        SortedSet<string> paths = new(StringComparer.Ordinal);
        void Walk(IrBlockId id, ImmutableList<string> calls, ImmutableHashSet<IrBlockId> entered)
        {
            IrBlock block = blocks[id];
            ImmutableList<string> through = calls.AddRange(block.Instructions.OfType<IrCall>().Select(static c => c.Callee.Value));
            ImmutableArray<IrBlockId> next = [.. Successors(block.Terminator).Where(s => !entered.Contains(s))];
            if (next.IsEmpty)
            {
                paths.Add(string.Join(" > ", through));
            }

            foreach (IrBlockId successor in next)
            {
                Walk(successor, through, entered.Add(successor));
            }
        }

        Walk(procedure.Entry, [], [procedure.Entry]);
        return $"calls {{{string.Join(" | ", paths)}}} parameters [{string.Join(", ", procedure.Parameters.Select(static p => $"{p.Var.Name}: {p.Var.Type}"))}]";
    }

    private static IEnumerable<IrBlockId> Successors(IrTerminator terminator) => terminator switch
    {
        IrGoto jump => [jump.Target],
        IrBranch branch => [branch.Then, branch.Else],
        IrSwitch choice => [.. choice.Cases.Select(static c => c.Target), choice.Default],
        _ => [],
    };

    internal static string Text(ILFunction function)
    {
        using PlainTextOutput output = new();
        function.WriteTo(output, new ILAstWritingOptions());
        return output.ToString();
    }
}
