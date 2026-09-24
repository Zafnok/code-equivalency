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
/// CsCheck prints the seed on failure; <see cref="Seed"/> pins the run. The class's static auto-property
/// <c>P</c> starts each run at the input's <c>B</c> in both; the IR's accessor calls are answered by
/// <see cref="AutoPropertyOracle"/>, which keeps the value the compiled run's backing field would hold.
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
        string source = $"public static class Oracle\n{{\n    public static int {LoweringOracleGen.Property} {{ get; set; }}\n{string.Concat(cases.Select(static (c, i) => c.Method.Render($"M{i.ToString(CultureInfo.InvariantCulture)}")))}}}\n";
        // Acceptance criterion 7: the run must actually reach the constructs M2-004 added.
        foreach (string construct in (string[])["while (", "+=", "++;", "--;", "s == null", "s != null", "checked", $"{LoweringOracleGen.Property} = "])
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
        Assert.True(emitted.Success, string.Join('\n', emitted.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
        image.Position = 0;

        AssemblyLoadContext context = new("lowering-oracle", isCollectible: true);
        try
        {
            Type oracle = context.LoadFromStream(image).GetType("Oracle")!;
            PropertyInfo property = oracle.GetProperty(LoweringOracleGen.Property)!;
            int getterCalls = 0;
            SyntaxTree tree = compilation.SyntaxTrees[0];
            SemanticModel model = compilation.GetSemanticModel(tree);
            ImmutableArray<MethodDeclarationSyntax> declarations =
                [.. tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()];
            for (int i = 0; i < cases.Length; i++)
            {
                IMethodBodyOperation body = (IMethodBodyOperation)model.GetOperation(declarations[i], TestContext.Current.CancellationToken)!;
                IrProcedure procedure = IrLowerer.Lower(body, model, RenameMap.Empty, []);
                Assert.Empty(IrValidator.Validate(procedure));
                getterCalls += procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>().Count(static c => c.Callee == AutoPropertyOracle.Getter);
                MethodInfo method = oracle.GetMethod(declarations[i].Identifier.Text)!;
                foreach (OracleInput input in cases[i].Inputs)
                {
                    property.SetValue(null, input.B);
                    string expected = Compiled(method, input);
                    string actual = Interpreted(procedure, input);
                    Assert.True(
                        string.Equals(expected, actual, StringComparison.Ordinal),
                        $"{input}: C# {expected}, IR {actual}\n{cases[i].Method.Render("M")}\n{IrText.Dump(procedure)}");
                }
            }

            // Ticket M3-010 acceptance criterion 6: the run reaches the getter as well as the setter.
            Assert.NotEqual(0, getterCalls);
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
            return $"return {result}";
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
        return IrInterpreter.Run(procedure, arguments, new AutoPropertyOracle(input.B), IrGen.StepBudget).Outcome switch
        {
            IrReturned { Value: IrBitVecValue bits } => string.Create(CultureInfo.InvariantCulture, $"return {bits.TwosComplement}"),
            IrReturned { Value: IrBoolValue flag } => $"return {flag.Value}",
            IrThrew thrown => $"throw {thrown.ExceptionType}",
            var other => other.ToString(),
        };
    }

    /// <summary>
    /// Answers one run's accessor calls on <c>Oracle.P</c> as its backing field would: a getter returns the last value
    /// set, starting from <c>initial</c>, and neither accessor throws. Within one run the history is a function of the
    /// call position, so this is as deterministic as <see cref="Equiv.Core.ICallOracle"/> asks. No other call is generated.
    /// </summary>
    private sealed class AutoPropertyOracle(int initial) : Equiv.Core.ICallOracle
    {
        public static readonly Equiv.Core.CallIdentity Getter = new($"Oracle::get_{LoweringOracleGen.Property}()");

        private static readonly Equiv.Core.CallIdentity Setter = new($"Oracle::set_{LoweringOracleGen.Property}(int)");

        private IrValue value = IrBitVecValue.FromSigned(32, initial);

        public IrCallResult Answer(Equiv.Core.CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position)
        {
            if (callee == Setter)
            {
                value = arguments[0];
                return new IrCallResult(Value: null, Threw: false);
            }

            Assert.Equal(Getter, callee);
            return new IrCallResult(value, Threw: false);
        }
    }
}
