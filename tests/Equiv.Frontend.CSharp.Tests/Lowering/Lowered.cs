using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>Lowers one method of a snippet and checks that the result validates (ticket M2-003: the validator runs in every test).</summary>
internal static class Lowered
{
    /// <summary>Lowers method <paramref name="name"/> of <c>class C { <paramref name="members"/> }</c>.</summary>
    public static IrProcedure Method(string members, string name = "M", bool allowErrors = false) =>
        Source($"using System;\nclass C\n{{\n{members}\n}}\n", name, allowErrors);

    /// <summary>Lowers the method named <paramref name="name"/> (metadata name: <c>.ctor</c>, <c>get_P</c>) of class <c>C</c> in a whole compilation unit.</summary>
    public static IrProcedure Source(string source, string name = "M", bool allowErrors = false, RenameMap? renames = null)
    {
        Compilation compilation = RoslynTestCompilations.Compile(source);
        if (!allowErrors)
        {
            Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        }

        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers(name).OfType<IMethodSymbol>().Single();
        IrProcedure procedure = IrLowerer.Lower(method, compilation, renames ?? RenameMap.Empty);
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    public static IrOutcome Run(IrProcedure procedure, params IrValue[] arguments) =>
        IrInterpreter.Run(procedure, new IrInputs([.. arguments]), IrGenOracle.Instance, IrGen.StepBudget).Outcome;

    public static ImmutableArray<IrOpaque> Opaques(IrProcedure procedure) =>
        [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>()];

    public static ImmutableArray<IrCall> Calls(IrProcedure procedure) =>
        [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>()];

    public static IrBitVecValue Bits(int width, long value) => IrBitVecValue.FromSigned(width, value);
}
