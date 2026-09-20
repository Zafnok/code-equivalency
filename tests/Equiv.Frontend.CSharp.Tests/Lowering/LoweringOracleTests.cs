using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;

using CsCheck;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Operations;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// The lowering oracle (VERIFICATION-MODEL.md section 7; ticket M2-003 acceptance criterion 3): generated
/// methods are compiled into one in-memory assembly and run by reflection, lowered and run by
/// <see cref="IrInterpreter"/>, and must agree on the return value or thrown exception type for every input.
/// CsCheck prints the seed on failure; <see cref="Seed"/> pins the run.
/// </summary>
public sealed class LoweringOracleTests
{
    private const int Cases = 200;
    private const int InputsPerCase = 20;
    private const string Seed = "000000000000";

    private static readonly IrSortValue Reference = new("System.String", 1);

    [Fact]
    public void LoweredIrAgreesWithCompiledCSharp() =>
        Gen.Select(LoweringOracleGen.Method, LoweringOracleGen.Input.Array[InputsPerCase]).Array[Cases]
            .Sample(static cases => Check(cases), seed: Seed, iter: 1, print: static cases => $"{cases.Length} cases");

    private static void Check((OracleMethod Method, OracleInput[] Inputs)[] cases)
    {
        string source = $"public static class Oracle\n{{\n{string.Concat(cases.Select(static (c, i) => c.Method.Render($"M{i.ToString(CultureInfo.InvariantCulture)}")))}}}\n";
        // Acceptance criterion 7: the run must actually reach the constructs M2-004 added.
        foreach (string construct in (string[])["while (", "+=", "++;", "--;", "s == null", "s != null", "checked"])
        {
            Assert.Contains(construct, source, StringComparison.Ordinal);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "Oracle",
            [CSharpSyntaxTree.ParseText(source, path: "Oracle.cs", cancellationToken: TestContext.Current.CancellationToken)],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream image = new();
        EmitResult emitted = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
        image.Position = 0;

        AssemblyLoadContext context = new("lowering-oracle", isCollectible: true);
        try
        {
            Type oracle = context.LoadFromStream(image).GetType("Oracle")!;
            SyntaxTree tree = compilation.SyntaxTrees[0];
            SemanticModel model = compilation.GetSemanticModel(tree);
            ImmutableArray<MethodDeclarationSyntax> declarations =
                [.. tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()];
            for (int i = 0; i < cases.Length; i++)
            {
                IMethodBodyOperation body = (IMethodBodyOperation)model.GetOperation(declarations[i], TestContext.Current.CancellationToken)!;
                IrProcedure procedure = IrLowerer.Lower(body, model, RenameMap.Empty);
                Assert.Empty(IrValidator.Validate(procedure));
                MethodInfo method = oracle.GetMethod(declarations[i].Identifier.Text)!;
                foreach (OracleInput input in cases[i].Inputs)
                {
                    string expected = Compiled(method, input);
                    string actual = Interpreted(procedure, input);
                    Assert.True(
                        string.Equals(expected, actual, StringComparison.Ordinal),
                        $"{input}: C# {expected}, IR {actual}\n{cases[i].Method.Render("M")}\n{IrText.Dump(procedure)}");
                }
            }
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Compiled(MethodInfo method, OracleInput input)
    {
        try
        {
            object result = method.Invoke(null, [input.A, input.B, input.C, input.D, input.E, input.SIsNull ? null : "s"])!;
            return string.Create(CultureInfo.InvariantCulture, $"return {result}");
        }
        catch (TargetInvocationException exception)
        {
            return $"throw {exception.InnerException!.GetType().FullName}";
        }
    }

    private static IrValue Argument(IrVar parameter, OracleInput input) => parameter.Name switch
    {
        "a" => IrBitVecValue.FromSigned(32, input.A),
        "b" => IrBitVecValue.FromSigned(32, input.B),
        "c" => IrBitVecValue.FromSigned(64, input.C),
        "d" => IrBitVecValue.FromSigned(64, input.D),
        "e" => new IrBoolValue(input.E),
        "s" => Reference,
        _ => new IrMapValue(
            (IrMap)parameter.Type,
            new IrBoolValue(input.SIsNull),
            []),
    };

    private static string Interpreted(IrProcedure procedure, OracleInput input)
    {
        // By name, because the synthesised heap inputs (M2-004) are only there when the body needs them.
        IrInputs arguments = new([.. procedure.Parameters.Select(p => Argument(p.Var, input))]);
        return IrInterpreter.Run(procedure, arguments, IrGenOracle.Instance, IrGen.StepBudget).Outcome switch
        {
            IrReturned { Value: IrBitVecValue bits } => string.Create(CultureInfo.InvariantCulture, $"return {bits.TwosComplement}"),
            IrReturned { Value: IrBoolValue flag } => string.Create(CultureInfo.InvariantCulture, $"return {flag.Value}"),
            IrThrew thrown => $"throw {thrown.ExceptionType}",
            var other => other.ToString(),
        };
    }
}
