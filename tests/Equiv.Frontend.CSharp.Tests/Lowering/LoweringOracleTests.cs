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
/// <c>P</c> starts each run at the input's <c>B</c> in both, as its backing field's map in the IR, and its final value is
/// compared (ticket M4-008); its static property <c>Q</c>, whose accessors have bodies, starts at <c>B</c> too, and the IR's
/// calls to them are answered by <see cref="CompiledRunOracle"/>, which keeps the value the compiled run's field would hold. The
/// <c>int[]</c> parameters are two arrays, one passed twice (ticket P1-006), or <c>u</c> and a null <c>v</c> (ticket P2-017), and their final elements are compared
/// along with the static field's final value and the final <c>G</c> of the <c>Cell</c> parameter <c>o</c>, which starts at the input's <c>B</c> and which
/// the IR's calls to <c>o.Bump</c> change as the compiled method does (ticket P1-005). The <c>List&lt;int&gt;</c> parameter is <c>{ A, B }</c>, and the IR's calls on its
/// enumerator are answered by an enumerator of that list (ticket M4-001). The <c>decimal</c> parameter is <c>M</c>, and the IR's
/// pure <c>decimal</c> functions are answered by <see cref="DecimalOracle"/>, which applies <see cref="decimal"/>'s own operators
/// (ticket M4-002).
/// </summary>
public sealed class LoweringOracleTests
{
    private const int Cases = 200;
    private const int InputsPerCase = 20;
    private const string Seed = "000000000000";

    private const string FieldMap = $"field.Oracle.{LoweringOracleGen.Field}";

    private const string PropertyMap = $"field.Oracle.{LoweringOracleGen.Property}";

    private const string ArraySort = "int[]";

    private const string ElementMap = "array.int__";

    private const string LengthMap = "length.int__";

    private const string ArrayNulls = "null.int__";

    private const string ListNulls = "null.System.Collections.Generic.List_1";

    private const string CellMap = $"field.{LoweringOracleGen.CellType}.{LoweringOracleGen.CellField}";

    private const string CellNulls = $"null.{LoweringOracleGen.CellType}";

    /// <summary>The one <c>Cell</c> the parameter <c>o</c> is bound to.</summary>
    private static readonly IrSortValue CellReference = new(LoweringOracleGen.CellType, 1);

    private static readonly IrSortValue Reference = new("System.String", 1);

    private static readonly IrSortValue ListReference = new("System.Collections.Generic.List`1", 1);

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
        string source = Source(cases.Select(static c => c.Method));
        // Acceptance criterion 7: the run must actually reach the constructs M2-004 added (and M3-007's void field writers).
        foreach (string construct in (string[])["while (", "+=", "++;", "--;", "s == null", "s != null", "checked", $"{LoweringOracleGen.Property} = ", $"{LoweringOracleGen.CalledProperty} = ", $"{LoweringOracleGen.Field} = ", "public static void ", "u[", "v[", "foreach (", "(decimal)", "((int)", $"{LoweringOracleGen.Cell}.{LoweringOracleGen.Bump}(", $"{LoweringOracleGen.Cell}.{LoweringOracleGen.CellField} = "])
        {
            Assert.Contains(construct, source, StringComparison.Ordinal);
        }

        (CSharpCompilation compilation, MemoryStream image) = Emit(source);
        using MemoryStream disposed = image;
        AssemblyLoadContext context = new("lowering-oracle", isCollectible: true);
        try
        {
            Type oracle = context.LoadFromStream(image).GetType("Oracle")!;
            CompiledClass compiled = new(oracle);
            HashSet<Equiv.Core.CallIdentity> callees = [];
            HashSet<string> pures = new(StringComparer.Ordinal);
            bool compoundAdd = false;
            IrSortValue one = (IrSortValue)TypeMapper.Constant(compilation.GetSpecialType(SpecialType.System_Decimal), 1m);
            SyntaxTree tree = compilation.SyntaxTrees[0];
            SemanticModel model = compilation.GetSemanticModel(tree);
            ImmutableArray<MethodDeclarationSyntax> declarations =
                [.. tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()];
            for (int i = 0; i < cases.Length; i++)
            {
                IMethodBodyOperation body = (IMethodBodyOperation)model.GetOperation(declarations[i], TestContext.Current.CancellationToken)!;
                IrProcedure procedure = IrLowerer.Lower(body, model, RenameMap.Empty, []);
                Assert.Empty(IrValidator.Validate(procedure));
                callees.UnionWith(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>().Select(static c => c.Callee));
                pures.UnionWith(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>().Select(static p => p.Function));
                // Ticket P2-022: only a compound assignment or an increment writes a `dec.add` to `m`.
                compoundAdd |= procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>()
                    .Any(static p => p is { Function: "dec.add", Target.SourceName: "m" });
                MethodInfo method = oracle.GetMethod(declarations[i].Identifier.Text)!;
                foreach (OracleInput input in cases[i].Inputs)
                {
                    string expected = compiled.Run(method, input);
                    string actual = Interpreted(procedure, input, one);
                    Assert.True(
                        string.Equals(expected, actual, StringComparison.Ordinal),
                        $"{input}: C# {expected}, IR {actual}\n{cases[i].Method.Render("M")}\n{IrText.Dump(procedure)}");
                }
            }

            AssertReached(callees, pures);
            Assert.True(compoundAdd);
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>
    /// Ticket M4-008: the auto-property's getter and setter and the arrow-bodied property's getter, lowered from their
    /// symbols, agree with the compiled accessors on the static state each input sets: <c>F</c> at <c>A</c> and <c>P</c> at <c>B</c>.
    /// </summary>
    [Fact]
    public void AccessorsAgreeWithCompiledCSharp() =>
        LoweringOracleGen.Input.Array[InputsPerCase].Sample(static inputs => CheckAccessors(inputs), seed: Seed, iter: 1, print: static inputs => $"{inputs.Length} inputs");

    private static void CheckAccessors(OracleInput[] inputs)
    {
        (CSharpCompilation compilation, MemoryStream image) = Emit(Source([]));
        using MemoryStream disposed = image;
        AssemblyLoadContext context = new("lowering-oracle-accessors", isCollectible: true);
        try
        {
            Type oracle = context.LoadFromStream(image).GetType("Oracle")!;
            INamedTypeSymbol type = compilation.GetTypeByMetadataName("Oracle")!;
            IrProcedure get = Accessor(type, compilation, $"get_{LoweringOracleGen.Property}");
            IrProcedure set = Accessor(type, compilation, $"set_{LoweringOracleGen.Property}");
            IrProcedure arrow = Accessor(type, compilation, $"get_{LoweringOracleGen.ArrowProperty}");
            PropertyInfo property = oracle.GetProperty(LoweringOracleGen.Property)!;
            PropertyInfo arrowProperty = oracle.GetProperty(LoweringOracleGen.ArrowProperty)!;
            FieldInfo field = oracle.GetField(LoweringOracleGen.Field)!;
            foreach (OracleInput input in inputs)
            {
                field.SetValue(null, input.A);
                property.SetValue(null, input.B);
                Assert.Equal(new IrReturned(IrBitVecValue.FromSigned(32, (int)property.GetValue(null)!)), RunAccessor(get, input).Outcome);
                Assert.Equal(new IrReturned(IrBitVecValue.FromSigned(32, (int)arrowProperty.GetValue(null)!)), RunAccessor(arrow, input).Outcome);
                property.SetValue(null, input.A);
                IrRun written = RunAccessor(set, input);
                Assert.Equal(new IrReturned(Value: null), written.Outcome);
                Assert.Equal((int)property.GetValue(null)!, Value((IrMapValue)Assert.Single(written.Outs), Token));
            }
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>An accessor lowered from its symbol, with no opaque and no call left in it.</summary>
    private static IrProcedure Accessor(INamedTypeSymbol type, CSharpCompilation compilation, string name)
    {
        IrProcedure procedure = IrLowerer.Lower(type.GetMembers(name).OfType<IMethodSymbol>().Single(), compilation, RenameMap.Empty, []);
        Assert.Empty(IrValidator.Validate(procedure));
        Assert.DoesNotContain(procedure.Blocks.SelectMany(static b => b.Instructions), static i => i is IrOpaque or IrCall);
        return procedure;
    }

    /// <summary>An accessor's run: <c>value</c> is the input's <c>A</c>, and the static maps start as <see cref="Argument"/> sets them.</summary>
    private static IrRun RunAccessor(IrProcedure procedure, OracleInput input) =>
        IrInterpreter.Run(
            procedure,
            new IrInputs([.. procedure.Parameters.Select(p => string.Equals(p.Var.Name, "value", StringComparison.Ordinal) ? IrBitVecValue.FromSigned(32, input.A) : Argument(p.Var, input))]),
            IrGenOracle.Instance,
            IrGen.StepBudget);

    /// <summary>The class the generated methods are compiled into, then the <c>Cell</c> class.</summary>
    private static string Source(IEnumerable<OracleMethod> methods) =>
        $"public static class Oracle\n{{\n{LoweringOracleGen.ClassMembers}{string.Concat(methods.Select(static (m, i) => m.Render($"M{i.ToString(CultureInfo.InvariantCulture)}")))}}}\n{LoweringOracleGen.CellSource}";

    private static (CSharpCompilation Compilation, MemoryStream Image) Emit(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Oracle",
            [CSharpSyntaxTree.ParseText(source, path: "Oracle.cs", cancellationToken: TestContext.Current.CancellationToken)],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        MemoryStream image = new();
        EmitResult emitted = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join('\n', emitted.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
        image.Position = 0;
        return (compilation, image);
    }

    /// <summary>The compiled class's static state and the <c>Cell</c> type, reset from each input before a method runs.</summary>
    private sealed class CompiledClass(Type oracle)
    {
        private readonly PropertyInfo property = oracle.GetProperty(LoweringOracleGen.Property)!;
        private readonly PropertyInfo called = oracle.GetProperty(LoweringOracleGen.CalledProperty)!;
        private readonly FieldInfo field = oracle.GetField(LoweringOracleGen.Field)!;
        private readonly Type cellType = oracle.Assembly.GetType(LoweringOracleGen.CellType)!;

        /// <summary>Runs <paramref name="method"/> on <paramref name="input"/>: its outcome, then the final static field, arrays and <c>o.G</c>.</summary>
        public string Run(MethodInfo method, OracleInput input)
        {
            FieldInfo cellField = cellType.GetField(LoweringOracleGen.CellField)!;
            property.SetValue(null, input.B);
            called.SetValue(null, input.B);
            field.SetValue(null, input.A);
            int[] u = [input.A, input.B];
            int[]? v = Bind(input, u);
            object cell = Activator.CreateInstance(cellType)!;
            cellField.SetValue(cell, input.B);
            string compiled = Compiled(method, input, u, v, cell);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{compiled} {LoweringOracleGen.Field}={(int)field.GetValue(null)!} {LoweringOracleGen.Property}={(int)property.GetValue(null)!} u={u[0]},{u[1]} v={(v is null ? "null" : $"{v[0]},{v[1]}")} {LoweringOracleGen.CellField}={(int)cellField.GetValue(cell)!}");
        }
    }

    /// <summary>
    /// Ticket M3-010 acceptance criterion 6: the run reaches the getter as well as the setter; ticket M4-001: and a
    /// <c>foreach</c>; ticket M4-002: and <c>decimal</c> arithmetic, its conversions both ways and a comparison, as pure functions.
    /// </summary>
    private static void AssertReached(HashSet<Equiv.Core.CallIdentity> callees, HashSet<string> pures)
    {
        Assert.Contains(CompiledRunOracle.Getter, callees);
        Assert.Contains(CompiledRunOracle.MoveNext, callees);
        Assert.Superset(new HashSet<string>(["conv.i32.dec", "conv.dec.i32", "dec.mul", "dec.div", "dec.lt"], StringComparer.Ordinal), pures);
    }

    /// <summary>The compiled run's <c>v</c>: <c>{ B, A }</c>, <paramref name="u"/> itself, or null.</summary>
    private static int[]? Bind(OracleInput input, int[] u) => input.V switch
    {
        ArrayBinding.Aliased => u,
        ArrayBinding.Null => null,
        _ => [input.B, input.A],
    };

    private static string Compiled(MethodInfo method, OracleInput input, int[] u, int[]? v, object cell)
    {
        try
        {
            object? result = method.Invoke(null, [input.A, input.B, input.C, input.D, input.E, input.SIsNull ? null : "s", u, v, List(input), input.M, cell]);
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
        "l" => ListReference,
        LoweringOracleGen.Cell => CellReference,
        ListNulls or CellNulls => new IrMapValue((IrMap)parameter.Type, new IrBoolValue(Value: false), []),
        CellMap => InitialCell(input),
        FieldMap => InitialField(input),
        PropertyMap => InitialProperty(input),
        ElementMap => InitialArrays(input),
        LengthMap => new IrMapValue((IrMap)parameter.Type, IrBitVecValue.FromSigned(32, 2), []),
        ArrayNulls => new IrMapValue((IrMap)parameter.Type, new IrBoolValue(Value: false), ImmutableDictionary<IrValue, IrValue>.Empty.Add(Null, new IrBoolValue(Value: true))),
        _ => new IrMapValue(
            (IrMap)parameter.Type,
            new IrBoolValue(input.SIsNull),
            []),
    };

    private static List<int> List(OracleInput input) => [input.A, input.B];

    /// <summary>Every <c>Cell</c>'s <c>G</c> by reference: the one <c>o</c> is bound to starts at the input's <c>B</c>.</summary>
    private static IrMapValue InitialCell(OracleInput input) => new(
        new IrMap(CellReference.Type, new IrBitVec(32)),
        IrBitVecValue.FromSigned(32, 0),
        ImmutableDictionary<IrValue, IrValue>.Empty.Add(CellReference, IrBitVecValue.FromSigned(32, input.B)));

    /// <summary>The array <c>v</c> is bound to: <c>u</c>'s when the input aliases them, the null reference when it is null.</summary>
    private static IrSortValue V(OracleInput input) => input.V switch
    {
        ArrayBinding.Aliased => First,
        ArrayBinding.Null => Null,
        _ => Second,
    };

    private static IrMapValue InitialProperty(OracleInput input) => new(
        new IrMap(Token.Type, new IrBitVec(32)),
        IrBitVecValue.FromSigned(32, 0),
        ImmutableDictionary<IrValue, IrValue>.Empty.Add(Token, IrBitVecValue.FromSigned(32, input.B)));

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

    private static string Interpreted(IrProcedure procedure, OracleInput input, IrSortValue one)
    {
        // By name, because the synthesised heap inputs (M2-004) are only there when the body needs them.
        DecimalOracle decimals = new(one);
        IrInputs arguments = new([.. procedure.Parameters.Select(p => string.Equals(p.Var.Name, "m", StringComparison.Ordinal) ? decimals.Element(input.M) : Argument(p.Var, input))]);
        CompiledRunOracle oracle = new(input.B, List(input), InitialCell(input));
        IrRun run = IrInterpreter.Run(procedure, arguments, oracle, IrGen.StepBudget, pure: decimals);
        string outcome = run.Outcome switch
        {
            IrReturned { Value: IrBitVecValue bits } => string.Create(CultureInfo.InvariantCulture, $"return {bits.TwosComplement}"),
            IrReturned { Value: IrBoolValue flag } => $"return {flag.Value}",
            IrReturned { Value: null } => "return ",
            IrThrew thrown => $"throw {thrown.ExceptionType}",
            var other => other.ToString(),
        };

        // Ticket M3-007: the final heap is the run's outs, one per by-ref map in parameter order; a map the body never
        // touches is not a parameter and keeps its initial value, except that `o.Bump` changes `o.G` whether or not the body
        // touches it, which the oracle threads as the encoder does (ticket P1-005). A run that neither returned nor threw has no outs.
        ImmutableArray<string> maps = [.. procedure.Parameters.Where(static p => p.Kind == IrParameterKind.Ref).Select(static p => p.Var.Name)];
        if (run.Outs.Length != maps.Length)
        {
            return $"{outcome} outs {run.Outs.Length}";
        }

        Dictionary<string, IrValue> heap = maps.Zip(run.Outs).ToDictionary(static e => e.First, static e => e.Second, StringComparer.Ordinal);
        IrMapValue field = (IrMapValue)heap.GetValueOrDefault(FieldMap, InitialField(input));
        IrMapValue property = (IrMapValue)heap.GetValueOrDefault(PropertyMap, InitialProperty(input));
        IrMapValue arrays = (IrMapValue)heap.GetValueOrDefault(ElementMap, InitialArrays(input));
        IrMapValue cells = (IrMapValue)heap.GetValueOrDefault(CellMap, oracle.Cells);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{outcome} {LoweringOracleGen.Field}={Value(field, Token)} {LoweringOracleGen.Property}={Value(property, Token)} u={Array(arrays, First)} v={(input.V == ArrayBinding.Null ? "null" : Array(arrays, V(input)))} {LoweringOracleGen.CellField}={Value(cells, CellReference)}");
    }

    private static string Array(IrMapValue arrays, IrSortValue array)
    {
        IrMapValue elements = (IrMapValue)arrays.Read(array);
        return string.Create(CultureInfo.InvariantCulture, $"{Value(elements, Index(0))},{Value(elements, Index(1))}");
    }

    private static long Value(IrMapValue map, IrValue key) => ((IrBitVecValue)map.Read(key)).TwosComplement;

    /// <summary>
    /// Answers one run's pure <c>decimal</c> functions (ticket M4-002) with <see cref="decimal"/>'s own operators and
    /// conversions. An element of <c>System.Decimal</c> stands for one value, bit for bit, so <c>1.0</c> and <c>1.00</c> are
    /// two elements, as they are two values. A function that throws raises the flag of the exception's exact type, and its
    /// value is then any value of its type. The literal <c>1m</c> of <c>m++</c> (ticket P2-022) is the constant element
    /// <paramref name="one"/>.
    /// </summary>
    private sealed class DecimalOracle(IrSortValue one) : Equiv.Core.IPureOracle
    {
        private readonly List<decimal> values = [];

        public IrSortValue Element(decimal value)
        {
            int id = values.FindIndex(v => decimal.GetBits(v).AsSpan().SequenceEqual(decimal.GetBits(value)));
            if (id < 0)
            {
                id = values.Count;
                values.Add(value);
            }

            return new IrSortValue("System.Decimal", id + 1);
        }

        public IrPureResult Answer(IrPure pure, ImmutableArray<IrValue> arguments)
        {
            Func<IrValue> apply = pure.Function switch
            {
                "conv.i32.dec" => () => Element(((IrBitVecValue)arguments[0]).TwosComplement),
                "conv.dec.i32" => () => IrBitVecValue.FromSigned(32, (int)Value(arguments[0])),
                "dec.add" => () => Element(Value(arguments[0]) + Value(arguments[1])),
                "dec.sub" => () => Element(Value(arguments[0]) - Value(arguments[1])),
                "dec.mul" => () => Element(Value(arguments[0]) * Value(arguments[1])),
                "dec.div" => () => Element(Value(arguments[0]) / Value(arguments[1])),
                "dec.rem" => () => Element(Value(arguments[0]) % Value(arguments[1])),
                "dec.eq" => () => new IrBoolValue(Value(arguments[0]) == Value(arguments[1])),
                "dec.ne" => () => new IrBoolValue(Value(arguments[0]) != Value(arguments[1])),
                "dec.lt" => () => new IrBoolValue(Value(arguments[0]) < Value(arguments[1])),
                "dec.le" => () => new IrBoolValue(Value(arguments[0]) <= Value(arguments[1])),
                "dec.gt" => () => new IrBoolValue(Value(arguments[0]) > Value(arguments[1])),
                "dec.ge" => () => new IrBoolValue(Value(arguments[0]) >= Value(arguments[1])),
                _ => throw new InvalidOperationException($"The lowering oracle generates no {pure.Function}."),
            };
            try
            {
                return new IrPureResult(apply(), [.. pure.Throws.Select(static _ => false)]);
            }
            catch (ArithmeticException exception)
            {
                string thrown = exception.GetType().FullName!;
                Assert.Contains(pure.Throws, t => string.Equals(t.ExceptionType, thrown, StringComparison.Ordinal));
                IrValue any = pure.Target.Type is IrBitVec ? IrBitVecValue.FromSigned(32, 0) : Element(0m);
                return new IrPureResult(any, [.. pure.Throws.Select(t => string.Equals(t.ExceptionType, thrown, StringComparison.Ordinal))]);
            }
        }

        private decimal Value(IrValue element) => element == one ? 1m : values[((IrSortValue)element).Id - 1];
    }

    /// <summary>
    /// Answers one run's calls as the compiled run's callees would. An accessor call on <c>Oracle.P</c> acts as its backing
    /// field: a getter returns the last value set, starting from <c>initial</c>, and neither accessor throws. A call on the
    /// list's enumerator (ticket M4-001) is made on a real enumerator of <paramref name="list"/>, one per
    /// <c>GetEnumerator</c>, so a nested <c>foreach</c> has its own. Within one run the history is a function of the call
    /// position, so this is as deterministic as <see cref="Equiv.Core.ICallOracle"/> asks. <c>o.Bump(k)</c> (ticket P1-005) adds
    /// <c>k</c> to <c>o</c>'s <c>G</c>, wrapping as the compiled method's <c>unchecked</c> add does, and leaves every other map as
    /// it is; no other call writes the heap. The <c>G</c> it adds to is the heap's when the call is given <c>field.Cell.G</c>, else
    /// <see cref="Cells"/>, the version threaded through the earlier calls, as the encoder threads a map a side never names
    /// (VERIFICATION-MODEL.md section 5). <c>o.TryParse(k, out n)</c> (ticket M4-003) answers as the compiled method does, its
    /// <c>n</c> as the call's one ref output. No other call is generated.
    /// </summary>
    private sealed class CompiledRunOracle(int initial, List<int> list, IrMapValue cells) : Equiv.Core.ICallOracle
    {
        public static readonly Equiv.Core.CallIdentity Getter = new($"Oracle::get_{LoweringOracleGen.CalledProperty}()");

        public static readonly Equiv.Core.CallIdentity MoveNext = new("System.Collections.Generic.List`1.Enumerator::MoveNext()<int>");

        private static readonly Equiv.Core.CallIdentity Setter = new($"Oracle::set_{LoweringOracleGen.CalledProperty}(int)");

        private static readonly Equiv.Core.CallIdentity GetEnumerator = new("System.Collections.Generic.List`1::GetEnumerator()<int>");

        private static readonly Equiv.Core.CallIdentity Current = new("System.Collections.Generic.List`1.Enumerator::get_Current()<int>");

        private static readonly Equiv.Core.CallIdentity Dispose = new("System.IDisposable::Dispose()");

        private static readonly Equiv.Core.CallIdentity Bump = new($"{LoweringOracleGen.CellType}::{LoweringOracleGen.Bump}(int)");

        private static readonly Equiv.Core.CallIdentity TryParse = new($"{LoweringOracleGen.CellType}::{LoweringOracleGen.TryParse}(int,out int)");

        /// <summary>Boxed, so each call advances the one enumerator and not a copy of the struct.</summary>
        private readonly List<IEnumerator<int>> enumerators = [];

        private IrValue value = IrBitVecValue.FromSigned(32, initial);

        /// <summary><c>field.Cell.G</c> after the last call to <c>o.Bump</c>, or its input before the first.</summary>
        public IrMapValue Cells { get; private set; } = cells;

        public IrCallResult Answer(Equiv.Core.CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            if (callee == Bump)
            {
                Assert.Equal(CellReference, arguments[0]);
                Cells = Bumped((IrMapValue?)heap.FirstOrDefault(static h => h.Map is CellMap)?.Value ?? Cells, arguments[1]);
                return new IrCallResult(Value: null, Threw: false) { Heap = [.. heap.Select(h => h.Map is CellMap ? Cells : h.Value)] };
            }

            if (callee == TryParse)
            {
                Assert.Equal(CellReference, arguments[0]);
                int k = (int)((IrBitVecValue)arguments[1]).TwosComplement;
                Assert.Equal([new IrBitVec(32)], refOuts);
                return new IrCallResult(new IrBoolValue((k & 1) == 0), Threw: false) { RefOuts = [IrBitVecValue.FromSigned(32, unchecked(k * 3))] };
            }

            if (callee == Setter)
            {
                value = arguments[0];
                return new IrCallResult(Value: null, Threw: false);
            }

            if (callee == GetEnumerator)
            {
                Assert.Equal(ListReference, arguments[0]);
                enumerators.Add(list.GetEnumerator());
                return new IrCallResult(new IrSortValue(((IrSort)resultType!).Name, enumerators.Count), Threw: false);
            }

            if (callee == MoveNext)
            {
                return new IrCallResult(new IrBoolValue(Enumerator(arguments).MoveNext()), Threw: false);
            }

            if (callee == Current)
            {
                return new IrCallResult(IrBitVecValue.FromSigned(32, Enumerator(arguments).Current), Threw: false);
            }

            if (callee == Dispose)
            {
                Enumerator(arguments).Dispose();
                return new IrCallResult(Value: null, Threw: false);
            }

            Assert.Equal(Getter, callee);
            return new IrCallResult(value, Threw: false);
        }

        private static IrMapValue Bumped(IrMapValue cells, IrValue k) =>
            cells.Write(CellReference, IrBitVecValue.FromSigned(32, unchecked((int)(((IrBitVecValue)cells.Read(CellReference)).TwosComplement + ((IrBitVecValue)k).TwosComplement))));

        private IEnumerator<int> Enumerator(ImmutableArray<IrValue> arguments) => enumerators[((IrSortValue)arguments[0]).Id - 1];
    }
}
