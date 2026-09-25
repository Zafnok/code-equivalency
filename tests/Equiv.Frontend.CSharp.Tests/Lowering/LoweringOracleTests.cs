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
/// <see cref="AutoPropertyOracle"/>, which keeps the value the compiled run's backing field would hold. The
/// <c>int[]</c> parameters are two arrays, one passed twice (ticket P1-006), or <c>u</c> and a null <c>v</c> (ticket P2-017), and their final elements are compared
/// along with the static field's final value.
/// </summary>
public sealed class LoweringOracleTests
{
    private const int Cases = 200;
    private const int InputsPerCase = 20;
    private const string Seed = "000000000000";

    private const string FieldMap = $"field.Oracle.{LoweringOracleGen.Field}";

    private const string ArraySort = "int[]";

    private const string ElementMap = "array.int__";

    private const string LengthMap = "length.int__";

    private const string ArrayNulls = "null.int__";

    private static readonly IrSortValue Reference = new("System.String", 1);

    private static readonly IrSortValue First = new(ArraySort, 1);

    private static readonly IrSortValue Second = new(ArraySort, 2);

    /// <summary>The reference a null <c>v</c> is bound to: the one element the <c>null.int__</c> input answers true for.</summary>
    private static readonly IrSortValue Null = new(ArraySort, 3);

    /// <summary>The key a static field's map is read at: element 0 of its declaring type's sort.</summary>
    private static readonly IrSortValue Token = new("Oracle", 0);

    [Fact]
    public void LoweredIrAgreesWithCompiledCSharp() =>
        Gen.Select(LoweringOracleGen.Method, LoweringOracleGen.Input.Array[InputsPerCase]).Array[Cases]
            .Sample(static cases => Check(cases), seed: Seed, iter: 1, print: static cases => $"{cases.Length} cases");

    private static void Check((OracleMethod Method, OracleInput[] Inputs)[] cases)
    {
        string source = $"public static class Oracle\n{{\n    public static int {LoweringOracleGen.Property} {{ get; set; }}\n    public static int {LoweringOracleGen.Field};\n{string.Concat(cases.Select(static (c, i) => c.Method.Render($"M{i.ToString(CultureInfo.InvariantCulture)}")))}}}\n";
        // Acceptance criterion 7: the run must actually reach the constructs M2-004 added (and M3-007's void field writers).
        foreach (string construct in (string[])["while (", "+=", "++;", "--;", "s == null", "s != null", "checked", $"{LoweringOracleGen.Property} = ", $"{LoweringOracleGen.Field} = ", "public static void ", "u[", "v["])
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
            FieldInfo field = oracle.GetField(LoweringOracleGen.Field)!;
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
                    field.SetValue(null, input.A);
                    int[] u = [input.A, input.B];
                    int[]? v = Bind(input, u);
                    string compiled = Compiled(method, input, u, v);
                    string expected = string.Create(CultureInfo.InvariantCulture, $"{compiled} {LoweringOracleGen.Field}={(int)field.GetValue(null)!} u={u[0]},{u[1]} v={(v is null ? "null" : $"{v[0]},{v[1]}")}");
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

    /// <summary>The compiled run's <c>v</c>: <c>{ B, A }</c>, <paramref name="u"/> itself, or null.</summary>
    private static int[]? Bind(OracleInput input, int[] u) => input.V switch
    {
        ArrayBinding.Aliased => u,
        ArrayBinding.Null => null,
        _ => [input.B, input.A],
    };

    private static string Compiled(MethodInfo method, OracleInput input, int[] u, int[]? v)
    {
        try
        {
            object? result = method.Invoke(null, [input.A, input.B, input.C, input.D, input.E, input.SIsNull ? null : "s", u, v]);
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
        "u" => First,
        "v" => V(input),
        FieldMap => InitialField(input),
        ElementMap => InitialArrays(input),
        LengthMap => new IrMapValue((IrMap)parameter.Type, IrBitVecValue.FromSigned(32, 2), []),
        ArrayNulls => new IrMapValue((IrMap)parameter.Type, new IrBoolValue(Value: false), ImmutableDictionary<IrValue, IrValue>.Empty.Add(Null, new IrBoolValue(Value: true))),
        _ => new IrMapValue(
            (IrMap)parameter.Type,
            new IrBoolValue(input.SIsNull),
            []),
    };

    /// <summary>The array <c>v</c> is bound to: <c>u</c>'s when the input aliases them, the null reference when it is null.</summary>
    private static IrSortValue V(OracleInput input) => input.V switch
    {
        ArrayBinding.Aliased => First,
        ArrayBinding.Null => Null,
        _ => Second,
    };

    private static IrMapValue InitialField(OracleInput input) => new(
        new IrMap(Token.Type, new IrBitVec(32)),
        IrBitVecValue.FromSigned(32, 0),
        ImmutableDictionary<IrValue, IrValue>.Empty.Add(Token, IrBitVecValue.FromSigned(32, input.A)));

    /// <summary><c>{ A, B }</c> and <c>{ B, A }</c>, as the compiled run's arrays start; an aliased <c>v</c> never reads the second.</summary>
    private static IrMapValue InitialArrays(OracleInput input)
    {
        IrMapValue first = Elements(input.A, input.B);
        return new(
            new IrMap(First.Type, first.MapType),
            Elements(0, 0),
            ImmutableDictionary<IrValue, IrValue>.Empty.Add(First, first).Add(Second, Elements(input.B, input.A)));
    }

    private static IrMapValue Elements(int first, int second) => new(
        new IrMap(new IrBitVec(32), new IrBitVec(32)),
        IrBitVecValue.FromSigned(32, 0),
        ImmutableDictionary<IrValue, IrValue>.Empty.Add(Index(0), IrBitVecValue.FromSigned(32, first)).Add(Index(1), IrBitVecValue.FromSigned(32, second)));

    private static IrBitVecValue Index(int index) => IrBitVecValue.FromSigned(32, index);

    private static string Interpreted(IrProcedure procedure, OracleInput input)
    {
        // By name, because the synthesised heap inputs (M2-004) are only there when the body needs them.
        IrInputs arguments = new([.. procedure.Parameters.Select(p => Argument(p.Var, input))]);
        IrRun run = IrInterpreter.Run(procedure, arguments, new AutoPropertyOracle(input.B), IrGen.StepBudget);
        string outcome = run.Outcome switch
        {
            IrReturned { Value: IrBitVecValue bits } => string.Create(CultureInfo.InvariantCulture, $"return {bits.TwosComplement}"),
            IrReturned { Value: IrBoolValue flag } => $"return {flag.Value}",
            IrReturned { Value: null } => "return ",
            IrThrew thrown => $"throw {thrown.ExceptionType}",
            var other => other.ToString(),
        };

        // Ticket M3-007: the final heap is the run's outs, one per by-ref map in parameter order; a map the body never
        // touches is not a parameter and keeps its initial value. A run that neither returned nor threw has no outs.
        ImmutableArray<string> maps = [.. procedure.Parameters.Where(static p => p.Kind == IrParameterKind.Ref).Select(static p => p.Var.Name)];
        if (run.Outs.Length != maps.Length)
        {
            return $"{outcome} outs {run.Outs.Length}";
        }

        Dictionary<string, IrValue> heap = maps.Zip(run.Outs).ToDictionary(static e => e.First, static e => e.Second, StringComparer.Ordinal);
        IrMapValue field = (IrMapValue)heap.GetValueOrDefault(FieldMap, InitialField(input));
        IrMapValue arrays = (IrMapValue)heap.GetValueOrDefault(ElementMap, InitialArrays(input));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{outcome} {LoweringOracleGen.Field}={Value(field, Token)} u={Array(arrays, First)} v={(input.V == ArrayBinding.Null ? "null" : Array(arrays, V(input)))}");
    }

    private static string Array(IrMapValue arrays, IrSortValue array)
    {
        IrMapValue elements = (IrMapValue)arrays.Read(array);
        return string.Create(CultureInfo.InvariantCulture, $"{Value(elements, Index(0))},{Value(elements, Index(1))}");
    }

    private static long Value(IrMapValue map, IrValue key) => ((IrBitVecValue)map.Read(key)).TwosComplement;

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
