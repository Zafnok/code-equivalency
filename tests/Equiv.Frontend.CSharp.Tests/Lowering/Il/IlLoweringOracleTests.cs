using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering.Il;

/// <summary>
/// The IL lowering's oracle (tickets P1-014 acceptance criterion 5 and P1-015 acceptance criterion 2; VERIFICATION-MODEL.md
/// section 7): generated methods over integers, <c>bool</c>, <c>double</c> and <c>decimal</c>, which call fixed helpers
/// (one with a <c>ref</c> and an <c>out</c> parameter), read and write a field and array elements, test and downcast an
/// object, and throw and catch in <c>try</c>/<c>catch</c>/<c>finally</c>, are compiled into one in-memory assembly and
/// run by reflection, and lowered from their IL and run by <see cref="IrInterpreter"/>. <see cref="HelperOracle"/> answers
/// the calls as the helpers compute, a new object being a fresh element; <see cref="NumberOracle"/> answers the pure
/// functions on the values the floating-point and <c>decimal</c> elements stand for; and the heap's inputs are one object
/// model: each object has one number in every sort, the <c>cast</c> maps keep it, and the <c>istype</c> maps know its
/// class. The two agree on the value returned or the exception type thrown for every input, in a Debug and an optimised
/// build. CsCheck prints the seed on failure; <see cref="Seed"/> pins the run.
/// </summary>
public sealed class IlLoweringOracleTests
{
    private const int Cases = 120;
    private const int InputsPerCase = 16;
    private const string Seed = "0000000000P1";

    /// <summary>The first number of a <c>Box</c>; the call that makes one is numbered from here by its position in the run.</summary>
    private const int Boxes = 1000;

    private const string Helper = """
        public static class H
        {
            public static int F(int x) => unchecked(x * 3 + 1);
            public static bool P(int x) => (x & 1) == 0;
            public static void R(ref int x, out int y) { y = unchecked(x * 2); x = unchecked(x + 1); }
        }

        public sealed class Box
        {
            public int V;
        }
        """;

    private static readonly double[] Doubles = [0.5, 1.5, -2.25, 3];

    private static readonly decimal[] Decimals = [0m, 1m, 0.5m, 2.5m, -3m, 1000m, 79228162514264337593543950335m];

    /// <summary>The number of the string literal <c>"s"</c>, the element <see cref="TypeMapper"/> designates for it.</summary>
    private static readonly int Literal = ((IrSortValue)TypeMapper.Constant(RoslynTestCompilations.Compile(string.Empty).GetSpecialType(SpecialType.System_String), "s")).Id;

    private static readonly Gen<int> Small = Gen.OneOf(Gen.Int[-9, 9], Gen.Const(int.MinValue), Gen.Const(int.MaxValue), Gen.Int);

    private static readonly Gen<(int A, int B, long C, bool E)> Input = Gen.Select(Small, Small, Gen.OneOf(Gen.Long[-9, 9], Gen.Const(long.MinValue), Gen.Long), Gen.Bool);

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void LoweredIrAgreesWithCompiledCSharp(OptimizationLevel optimization) =>
        Gen.Select(Method, Input.Array[InputsPerCase]).Array[Cases]
            .Sample(cases => Check(cases, optimization), seed: Seed, iter: 1, print: static cases => $"{cases.Length} cases");

    private static void Check((string Body, (int A, int B, long C, bool E)[] Inputs)[] cases, OptimizationLevel optimization)
    {
        StringBuilder source = new("using System;\npublic static class Oracle\n{\n");
        for (int i = 0; i < cases.Length; i++)
        {
            source.Append(CultureInfo.InvariantCulture, $"public static int M{i}(int a, int b, long c, bool e)\n{{\n{cases[i].Body}}}\n");
        }

        source.Append("}\n").Append(Helper);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "IlOracle",
            [CSharpSyntaxTree.ParseText(SourceText.From(source.ToString(), Encoding.UTF8), path: "IlOracle.cs", cancellationToken: TestContext.Current.CancellationToken)],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: optimization));
        using MemoryStream image = new();
        EmitResult emitted = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join('\n', emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext context = new("il-lowering-oracle", isCollectible: true);
        try
        {
            Type oracle = context.LoadFromStream(image).GetType("Oracle")!;
            INamedTypeSymbol type = compilation.GetTypeByMetadataName("Oracle")!;
            NumberOracle numbers = new(compilation);
            for (int i = 0; i < cases.Length; i++)
            {
                string name = string.Create(CultureInfo.InvariantCulture, $"M{i}");
                IrProcedure procedure = IlLowerer.Lower(type.GetMembers(name).OfType<IMethodSymbol>().Single(), compilation);
                Assert.Empty(IrValidator.Validate(procedure));
                Assert.True(IlLowererTests.Opaques(procedure).IsEmpty, $"{cases[i].Body}\n{IrText.Dump(procedure)}");
                MethodInfo method = oracle.GetMethod(name)!;
                foreach ((int a, int b, long c, bool e) in cases[i].Inputs)
                {
                    string expected = Compiled(method, a, b, c, e);
                    string actual = Interpreted(procedure, numbers, a, b, c, e);
                    Assert.True(
                        string.Equals(expected, actual, StringComparison.Ordinal),
                        string.Create(CultureInfo.InvariantCulture, $"({a}, {b}, {c}, {e}): C# {expected}, IR {actual}\n{cases[i].Body}\n{IrText.Dump(procedure)}"));
                }
            }
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Compiled(MethodInfo method, int a, int b, long c, bool e)
    {
        try
        {
            return string.Create(CultureInfo.InvariantCulture, $"return {(int)method.Invoke(null, [a, b, c, e])!}");
        }
        catch (TargetInvocationException exception)
        {
            return $"throw {exception.InnerException!.GetType().FullName}";
        }
    }

    private static string Interpreted(IrProcedure procedure, NumberOracle numbers, int a, int b, long c, bool e)
    {
        ImmutableArray<IrValue> arguments = [IrBitVecValue.FromSigned(32, a), IrBitVecValue.FromSigned(32, b), IrBitVecValue.FromSigned(64, c), new IrBoolValue(e)];
        IrRun run = IrInterpreter.Run(
            procedure,
            new IrInputs([.. arguments, .. procedure.Parameters.Skip(arguments.Length).Select(Heap)]),
            HelperOracle.Instance,
            IrGen.StepBudget,
            pure: numbers);
        return run.Outcome switch
        {
            IrReturned { Value: IrBitVecValue bits } => string.Create(CultureInfo.InvariantCulture, $"return {bits.TwosComplement}"),
            IrThrew thrown => $"throw {thrown.ExceptionType}",
            var other => other.ToString(),
        };
    }

    /// <summary>
    /// The heap as it is when a generated method starts: no object is null but number 0; a field of a new <c>Box</c> and an
    /// element of a new array hold 0; the k-th new array is number 2000 + k; <c>cast</c> maps an object to the same number
    /// in the other sort, and <c>istype</c> holds of a <c>Box</c> for a number from <see cref="Boxes"/> and of a string for
    /// a literal's.
    /// </summary>
    private static IrValue Heap(IrParameter parameter)
    {
        IrMap map = (IrMap)parameter.Var.Type;
        string name = parameter.Var.Name;
        IEnumerable<KeyValuePair<IrValue, IrValue>> objects = Enumerable.Range(Boxes, 256).Append(Literal).Select(n => KeyValuePair.Create(Element(map.Key, n), Element(map.Value, n)));
        return name switch
        {
            _ when name.StartsWith("null.", StringComparison.Ordinal) => Map(map, new IrBoolValue(Value: false), [KeyValuePair.Create(Element(map.Key, 0), (IrValue)new IrBoolValue(Value: true))]),
            _ when name.StartsWith("new.", StringComparison.Ordinal) => Map(map, Element(map.Value, 1999), [.. Enumerable.Range(0, 256).Select(k => KeyValuePair.Create((IrValue)new IrBitVecValue(32, (ulong)k), Element(map.Value, 2000 + k)))]),
            _ when name.StartsWith("cast.", StringComparison.Ordinal) => Map(map, Element(map.Value, 1), objects),
            "istype.System.Object.Box" => Map(map, new IrBoolValue(Value: false), [.. Enumerable.Range(Boxes, 256).Select(n => KeyValuePair.Create(Element(map.Key, n), (IrValue)new IrBoolValue(Value: true)))]),
            "istype.System.Object.System.String" => Map(map, new IrBoolValue(Value: false), [KeyValuePair.Create(Element(map.Key, Literal), (IrValue)new IrBoolValue(Value: true))]),
            _ => Map(map, Zero(map.Value), []),
        };
    }


    private static IrValue Element(IrType type, int number) => type is IrSort sort ? new IrSortValue(sort.Name, number) : IrBitVecValue.FromSigned(32, number);

    private static IrMapValue Map(IrMap type, IrValue @default, IEnumerable<KeyValuePair<IrValue, IrValue>> entries) => new(type, @default, entries.ToImmutableDictionary());

    private static IrValue Zero(IrType type) => type switch
    {
        IrBitVec bits => new IrBitVecValue(bits.Width, 0),
        IrBool => new IrBoolValue(Value: false),
        IrMap inner => Map(inner, Zero(inner.Value), []),
        _ => new IrSortValue(((IrSort)type).Name, 0),
    };

    // Every constant is added to a variable less itself, so that no expression is a C# constant: the compiler rejects a
    // constant division by zero or a constant overflow in a checked context, and folds every other constant expression.

    /// <summary>A method body: a few statements over the locals, then a <c>return</c> of an <c>int</c>.</summary>
    private static readonly Gen<string> Method = Gen.Select(Statements(2), IntExpression(2), static (body, result) =>
        "int x = a; long y = c; bool f = e; uint u = (uint)b; byte k = (byte)a; int t = 0;\n"
        + "Box bx = new Box(); int[] arr = new int[(a & 3) + 1]; object o = f ? (object)\"s\" : bx;\n"
        + $"double d = a; decimal m = b;\n{body}return {result};\n");

    private static Gen<string> Statements(int depth, bool catches = true) => Statement(depth, catches).Array[1, 4].Select(static s => string.Concat(s));

    /// <summary>
    /// A statement; one in a <c>catch</c> region's <c>try</c> block holds no other <c>catch</c> region, since a call's
    /// exception of no known type that two could take is <c>call-throw-in-try</c> in either lowering.
    /// </summary>
    private static Gen<string> Statement(int depth, bool catches)
    {
        Gen<string> simple = Gen.OneOf(
            IntExpression(2).Select(static v => $"x = {v};\n"),
            LongExpression(2).Select(static v => $"y = {v};\n"),
            BoolExpression(2).Select(static v => $"f = {v};\n"),
            UIntExpression(2).Select(static v => $"u = {v};\n"),
            IntExpression(1).Select(static v => $"k = (byte)({v});\n"),
            IntExpression(1).Select(static v => $"x = H.F({v});\n"),
            IntExpression(1).Select(static v => $"bx.V = {v};\n"),
            Gen.Select(IntExpression(1), IntExpression(1), static (i, v) => $"arr[({i}) & 7] = {v};\n"),
            Gen.Const("H.R(ref x, out t);\nx = x + t;\n"),
            Gen.Const("o = bx;\n"),
            DoubleExpression(2).Select(static v => $"d = {v};\n"),
            DecimalExpression(2).Select(static v => $"m = {v};\n"));
        if (depth == 0)
        {
            return simple;
        }

        Gen<string> nested = Gen.OneOf(
            simple,
            Gen.Select(BoolExpression(1), Statements(depth - 1, catches), Statements(depth - 1, catches), static (c, t, o) => $"if ({c})\n{{\n{t}}}\nelse\n{{\n{o}}}\n"),
            Gen.Select(BoolExpression(1), Statements(depth - 1, catches), static (c, t) => $"if ({c})\n{{\n{t}}}\n"),
            Gen.Select(IntExpression(1), Statements(depth - 1, catches), Statements(depth - 1, catches), IntExpression(1), static (s, one, two, r) =>
                $"switch (({s}) & 7)\n{{\ncase 1:\n{one}break;\ncase 2:\ncase 3:\n{two}break;\ncase 6:\nreturn {r};\ndefault:\nx++;\nbreak;\n}}\n"),
            Gen.Select(Statements(depth - 1, catches), Statements(0), static (body, after) => $"try\n{{\n{body}}}\nfinally\n{{\n{after}}}\n"));
        return catches
            ? Gen.OneOf(
                nested,
                Gen.Select(Statements(depth - 1, catches: false), BoolExpression(1), Gen.OneOfConst("InvalidOperationException", "ArithmeticException"), IntExpression(1), static (body, c, caught, v) =>
                    $"try\n{{\n{body}if ({c}) throw new InvalidOperationException();\n}}\ncatch ({caught})\n{{\nx = {v};\n}}\nfinally\n{{\ny = y + 1;\n}}\n"))
            : nested;
    }

    private static Gen<string> IntExpression(int depth)
    {
        Gen<string> leaf = Gen.OneOf(
            Gen.Const("a"), Gen.Const("b"), Gen.Const("x"), Gen.Const("k"), Gen.Const("(int)u"), Gen.Const("(int)y"),
            Gen.Const("bx.V"), Gen.Const("arr.Length"), Gen.Const("arr[x & 3]"), Gen.Const("((Box)o).V"),
            Small.Select(static n => $"(a - a + ({n.ToString(CultureInfo.InvariantCulture)}))"));
        if (depth == 0)
        {
            return leaf;
        }

        Gen<string> operand = IntExpression(depth - 1);
        return Gen.OneOf(
            leaf,
            Gen.Select(operand, Gen.OneOfConst("+", "-", "*", "/", "%", "&", "|", "^"), operand, static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(operand, Gen.OneOfConst("<<", ">>"), operand, static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(operand, Gen.OneOfConst("+", "-", "*"), operand, static (l, op, r) => $"checked({l} {op} {r})"),
            operand.Select(static v => $"(-{v})"),
            operand.Select(static v => $"H.F({v})"),
            LongExpression(depth - 1).Select(static v => $"(int)({v})"),
            LongExpression(depth - 1).Select(static v => $"checked((int)({v}))"),
            UIntExpression(depth - 1).Select(static v => $"checked((int)({v}))"),
            operand.Select(static v => $"(short)({v})"),
            operand.Select(static v => $"(sbyte)({v})"),
            operand.Select(static v => $"(char)({v})"),
            DoubleExpression(depth - 1).Select(static v => $"(int)({v})"),
            DecimalExpression(depth - 1).Select(static v => $"(int)(({v}) % 1000m)"),
            Gen.Select(BoolExpression(depth - 1), operand, operand, static (c, t, o) => $"({c} ? {t} : {o})"));
    }

    private static Gen<string> LongExpression(int depth)
    {
        Gen<string> leaf = Gen.OneOf(Gen.Const("c"), Gen.Const("y"), Gen.Const("(long)a"), Gen.Const("(long)u"), Gen.Long[-9, 9].Select(static n => $"(c - c + ({n.ToString(CultureInfo.InvariantCulture)}L))"));
        if (depth == 0)
        {
            return leaf;
        }

        Gen<string> operand = LongExpression(depth - 1);
        return Gen.OneOf(
            leaf,
            Gen.Select(operand, Gen.OneOfConst("+", "-", "*", "/", "%", "&", "|", "^"), operand, static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(operand, Gen.OneOfConst("<<", ">>"), IntExpression(depth - 1), static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(operand, Gen.OneOfConst("+", "*"), operand, static (l, op, r) => $"checked({l} {op} {r})"),
            IntExpression(depth - 1).Select(static v => $"(long)({v})"),
            UIntExpression(depth - 1).Select(static v => $"(long)({v})"),
            IntExpression(depth - 1).Select(static v => $"(long)(ulong)(uint)({v})"));
    }

    private static Gen<string> UIntExpression(int depth)
    {
        Gen<string> leaf = Gen.OneOf(Gen.Const("u"), Gen.Const("(uint)a"), Gen.Const("(uint)k"), Gen.UInt[0, 9].Select(static n => $"(u - u + {n.ToString(CultureInfo.InvariantCulture)}u)"));
        if (depth == 0)
        {
            return leaf;
        }

        Gen<string> operand = UIntExpression(depth - 1);
        return Gen.OneOf(
            leaf,
            Gen.Select(operand, Gen.OneOfConst("+", "-", "*", "/", "%", "&", "|", "^"), operand, static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(operand, Gen.OneOfConst("<<", ">>"), IntExpression(depth - 1), static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(operand, Gen.OneOfConst("+", "-", "*"), operand, static (l, op, r) => $"checked({l} {op} {r})"),
            IntExpression(depth - 1).Select(static v => $"checked((uint)({v}))"),
            LongExpression(depth - 1).Select(static v => $"(uint)({v})"));
    }

    private static Gen<string> DoubleExpression(int depth)
    {
        Gen<string> leaf = Gen.OneOf(
            Gen.Const("d"), Gen.Const("(double)x"), Gen.Const("(double)y"), Gen.Const("(double)u"), Gen.Const("(double)(float)d"),
            Gen.OneOfConst(Doubles).Select(static n => $"(d - d + {n.ToString("R", CultureInfo.InvariantCulture)})"));
        if (depth == 0)
        {
            return leaf;
        }

        Gen<string> operand = DoubleExpression(depth - 1);
        return Gen.OneOf(
            leaf,
            Gen.Select(operand, Gen.OneOfConst("+", "-", "*", "/", "%"), operand, static (l, op, r) => $"({l} {op} {r})"),
            operand.Select(static v => $"(-{v})"),
            DecimalExpression(depth - 1).Select(static v => $"(double)({v})"),
            Gen.Select(BoolExpression(depth - 1), operand, operand, static (c, t, o) => $"({c} ? {t} : {o})"));
    }

    private static Gen<string> DecimalExpression(int depth)
    {
        Gen<string> leaf = Gen.OneOf(
            Gen.Const("m"), Gen.Const("(decimal)x"),
            Gen.OneOfConst(Decimals).Select(static n => $"(m - m + {n.ToString(CultureInfo.InvariantCulture)}m)"));
        if (depth == 0)
        {
            return leaf;
        }

        Gen<string> operand = DecimalExpression(depth - 1);
        return Gen.OneOf(
            leaf,
            Gen.Select(operand, Gen.OneOfConst("+", "-", "*", "/", "%"), operand, static (l, op, r) => $"({l} {op} {r})"),
            operand.Select(static v => $"(-{v})"),
            DoubleExpression(depth - 1).Select(static v => $"(decimal)({v})"));
    }

    private static Gen<string> BoolExpression(int depth)
    {
        Gen<string> leaf = Gen.OneOf(Gen.Const("e"), Gen.Const("f"), Gen.Const("true"), Gen.Const("false"), Gen.Const("((o as string) != null)"), Gen.Const("(o is Box)"));
        if (depth == 0)
        {
            return leaf;
        }

        Gen<string> operand = BoolExpression(depth - 1);
        Gen<string> comparison = Gen.OneOfConst("<", "<=", ">", ">=", "==", "!=");
        return Gen.OneOf(
            leaf,
            Gen.Select(IntExpression(depth - 1), comparison, IntExpression(depth - 1), static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(LongExpression(depth - 1), comparison, LongExpression(depth - 1), static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(UIntExpression(depth - 1), comparison, UIntExpression(depth - 1), static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(DoubleExpression(depth - 1), comparison, DoubleExpression(depth - 1), static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(DecimalExpression(depth - 1), comparison, DecimalExpression(depth - 1), static (l, op, r) => $"({l} {op} {r})"),
            Gen.Select(operand, Gen.OneOfConst("&&", "||", "&", "|", "^", "==", "!="), operand, static (l, op, r) => $"({l} {op} {r})"),
            operand.Select(static v => $"!{v}"),
            IntExpression(depth - 1).Select(static v => $"H.P({v})"));
    }

    /// <summary>
    /// Answers the helpers' calls as <see cref="Helper"/> computes them, and a constructor with a new object, numbered by the
    /// call's position from <see cref="Boxes"/>; no other call is generated, and none throws.
    /// </summary>
    private sealed class HelperOracle : ICallOracle
    {
        public static readonly HelperOracle Instance = new();

        private static readonly CallIdentity F = new("H::F(int)");

        private static readonly CallIdentity P = new("H::P(int)");

        private static readonly CallIdentity R = new("H::R(ref int,out int)");

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            if (callee.Value.EndsWith("::.ctor()", StringComparison.Ordinal))
            {
                return new IrCallResult(new IrSortValue(((IrSort)resultType!).Name, Boxes + position), Threw: false);
            }

            int x = (int)((IrBitVecValue)arguments[0]).TwosComplement;
            Assert.True(callee == F || callee == P || callee == R, callee.Value);
            return callee.Value switch
            {
                "H::F(int)" => new IrCallResult(IrBitVecValue.FromSigned(32, unchecked((x * 3) + 1)), Threw: false),
                "H::P(int)" => new IrCallResult(new IrBoolValue((x & 1) == 0), Threw: false),
                _ => new IrCallResult(Value: null, Threw: false) { RefOuts = [IrBitVecValue.FromSigned(32, unchecked(x + 1)), IrBitVecValue.FromSigned(32, unchecked(x * 2))] },
            };
        }
    }

    /// <summary>
    /// Answers <see cref="PureCatalogue"/>'s functions on the values their elements stand for: each floating-point or
    /// <c>decimal</c> element is one value, a literal's the one <see cref="TypeMapper"/> designates, a result's a new element
    /// the first time the value is made, so the same value is always the same element; an exception the value's own
    /// operator throws sets its flag.
    /// </summary>
    private sealed class NumberOracle : IPureOracle
    {
        private readonly Dictionary<(string Sort, int Element), object> values = [];
        private readonly Dictionary<(string Sort, string Key), int> elements = [];
        private int next = 1;

        public NumberOracle(Compilation compilation)
        {
            ITypeSymbol @double = compilation.GetSpecialType(SpecialType.System_Double);
            ITypeSymbol single = compilation.GetSpecialType(SpecialType.System_Single);
            ITypeSymbol @decimal = compilation.GetSpecialType(SpecialType.System_Decimal);
            foreach (double value in Doubles.Append(0))
            {
                Register((IrSortValue)TypeMapper.Constant(@double, value), value);
            }

            Register((IrSortValue)TypeMapper.Constant(single, 0f), 0f);
            foreach (decimal value in Decimals.Concat([0m, 1m, -1m, decimal.MinValue]))
            {
                Register((IrSortValue)TypeMapper.Constant(@decimal, value), value);
            }
        }

        public IrPureResult Answer(IrPure pure, ImmutableArray<IrValue> arguments)
        {
            string[] parts = pure.Function.Split('.');
            object[] operands = [.. arguments.Select(a => Decode(a, string.Equals(parts[0], "conv", StringComparison.Ordinal) ? parts[1] : parts[0]))];
            (object? result, Type? thrown) = Evaluate(parts, operands, isChecked: !pure.Throws.IsEmpty);
            IrValue value = result is null ? Zero(pure.Target.Type) : Encode(pure.Target.Type, result);
            return new IrPureResult(value, [.. pure.Throws.Select(t => string.Equals(t.ExceptionType, thrown?.FullName, StringComparison.Ordinal))]);
        }

        private void Register(IrSortValue element, object value)
        {
            values[(element.Sort, element.Id)] = value;
            elements[(element.Sort, Key(value))] = element.Id;
        }

        private object Decode(IrValue value, string code) => value switch
        {
            IrSortValue element => values[(element.Sort, element.Id)],
            IrBitVecValue bits when code is "u32" => (uint)bits.Bits,
            IrBitVecValue bits when code is "i64" => bits.TwosComplement,
            IrBitVecValue bits => (int)bits.TwosComplement,
            _ => ((IrBoolValue)value).Value,
        };

        private IrValue Encode(IrType type, object value) => (type, value) switch
        {
            (IrBool, bool flag) => new IrBoolValue(flag),
            (IrBitVec bits, _) => IrBitVecValue.FromSigned(bits.Width, System.Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            _ => Element(((IrSort)type).Name, value),
        };

        private IrSortValue Element(string sort, object value)
        {
            if (!elements.TryGetValue((sort, Key(value)), out int element))
            {
                while (values.ContainsKey((sort, next)))
                {
                    next++;
                }

                element = next;
                values[(sort, element)] = value;
                elements[(sort, Key(value))] = element;
            }

            return new IrSortValue(sort, element);
        }

        private static string Key(object value) => value switch
        {
            double number => BitConverter.DoubleToInt64Bits(number).ToString(CultureInfo.InvariantCulture),
            float number => BitConverter.SingleToInt32Bits(number).ToString(CultureInfo.InvariantCulture),
            _ => string.Join(',', decimal.GetBits((decimal)value)),
        };

        /// <summary>The value of a function, and the exception it throws; a conversion from floating point to an integer that can throw is a checked one.</summary>
        private static (object? Result, Type? Thrown) Evaluate(string[] parts, object[] operands, bool isChecked)
        {
            try
            {
                return (parts, operands) switch
                {
                    (["conv", _, "f64"], [var from]) => (System.Convert.ToDouble(Widen(from), CultureInfo.InvariantCulture), null),
                    (["conv", "f64", "f32"], [double from]) => ((float)from, null),
                    (["conv", "f64", "i32"], [double from]) => (isChecked ? Checked(from) : (int)from, null),
                    (["conv", "f64", "dec"], [double from]) => ((decimal)from, null),
                    (["conv", "i32", "dec"], [int from]) => ((decimal)from, null),
                    (["conv", "dec", "i32"], [decimal from]) => ((int)from, null),
                    (["f64", var op], [double left, double right]) => (Double(op, left, right), null),
                    (["f64", "neg"], [double operand]) => (-operand, null),
                    (["dec", var op], [decimal left, decimal right]) => (Decimal(op, left, right), null),
                    (["dec", "neg"], [decimal operand]) => (-operand, null),
                    _ => throw new InvalidOperationException($"{string.Join('.', parts)} is not generated"),
                };
            }
            catch (ArithmeticException exception)
            {
                return (null, exception.GetType());
            }
        }

        private static int Checked(double value) => checked((int)value);

        /// <summary><c>float</c>'s <c>double</c>, exactly, and every integer's.</summary>
        private static object Widen(object value) => value is float single ? (double)single : value;

        private static object Double(string op, double left, double right) => op switch
        {
            "add" => left + right,
            "sub" => left - right,
            "mul" => left * right,
            "div" => left / right,
            "rem" => left % right,
            "eq" => left == right,
            "ne" => left != right,
            "lt" => left < right,
            "le" => left <= right,
            "gt" => left > right,
            _ => left >= right,
        };

        private static object Decimal(string op, decimal left, decimal right) => op switch
        {
            "add" => left + right,
            "sub" => left - right,
            "mul" => left * right,
            "div" => left / right,
            "rem" => left % right,
            "eq" => left == right,
            "ne" => left != right,
            "lt" => left < right,
            "le" => left <= right,
            "gt" => left > right,
            _ => left >= right,
        };
    }
}
