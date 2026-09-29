using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering.Il;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering.Il;

/// <summary>
/// The IL lowering's oracle (ticket P1-014 acceptance criterion 5; VERIFICATION-MODEL.md section 7): generated
/// straight-line and branching integral methods, which call a fixed helper, are compiled into one in-memory assembly and
/// run by reflection, and lowered from their IL and run by <see cref="IrInterpreter"/>, whose calls to the helper
/// <see cref="HelperOracle"/> answers as the helper computes. The two agree on the value returned or the exception type
/// thrown for every input, in a Debug and an optimised build. CsCheck prints the seed on failure; <see cref="Seed"/> pins
/// the run.
/// </summary>
public sealed class IlLoweringOracleTests
{
    private const int Cases = 120;
    private const int InputsPerCase = 16;
    private const string Seed = "0000000000P1";

    private const string Helper = """
        public static class H
        {
            public static int F(int x) => unchecked(x * 3 + 1);
            public static bool P(int x) => (x & 1) == 0;
        }
        """;

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
        StringBuilder source = new("public static class Oracle\n{\n");
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
                    string actual = Interpreted(procedure, a, b, c, e);
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

    private static string Interpreted(IrProcedure procedure, int a, int b, long c, bool e)
    {
        IrRun run = IrInterpreter.Run(
            procedure,
            new IrInputs([IrBitVecValue.FromSigned(32, a), IrBitVecValue.FromSigned(32, b), IrBitVecValue.FromSigned(64, c), new IrBoolValue(e)]),
            HelperOracle.Instance,
            IrGen.StepBudget);
        return run.Outcome switch
        {
            IrReturned { Value: IrBitVecValue bits } => string.Create(CultureInfo.InvariantCulture, $"return {bits.TwosComplement}"),
            IrThrew thrown => $"throw {thrown.ExceptionType}",
            var other => other.ToString(),
        };
    }

    // Every constant is added to a variable less itself, so that no expression is a C# constant: the compiler rejects a
    // constant division by zero or a constant overflow in a checked context, and folds every other constant expression.

    /// <summary>A method body: a few statements over the locals, then a <c>return</c> of an <c>int</c>.</summary>
    private static readonly Gen<string> Method = Gen.Select(Statements(2), IntExpression(2), static (body, result) =>
        $"int x = a; long y = c; bool f = e; uint u = (uint)b; byte k = (byte)a;\n{body}return {result};\n");

    private static Gen<string> Statements(int depth) => Statement(depth).Array[1, 4].Select(static s => string.Concat(s));

    private static Gen<string> Statement(int depth)
    {
        Gen<string> simple = Gen.OneOf(
            IntExpression(2).Select(static v => $"x = {v};\n"),
            LongExpression(2).Select(static v => $"y = {v};\n"),
            BoolExpression(2).Select(static v => $"f = {v};\n"),
            UIntExpression(2).Select(static v => $"u = {v};\n"),
            IntExpression(1).Select(static v => $"k = (byte)({v});\n"),
            IntExpression(1).Select(static v => $"x = H.F({v});\n"));
        return depth == 0
            ? simple
            : Gen.OneOf(
                simple,
                Gen.Select(BoolExpression(1), Statements(depth - 1), Statements(depth - 1), static (c, t, o) => $"if ({c})\n{{\n{t}}}\nelse\n{{\n{o}}}\n"),
                Gen.Select(BoolExpression(1), Statements(depth - 1), static (c, t) => $"if ({c})\n{{\n{t}}}\n"),
                Gen.Select(IntExpression(1), Statements(depth - 1), Statements(depth - 1), IntExpression(1), static (s, one, two, r) =>
                    $"switch (({s}) & 7)\n{{\ncase 1:\n{one}break;\ncase 2:\ncase 3:\n{two}break;\ncase 6:\nreturn {r};\ndefault:\nx++;\nbreak;\n}}\n"));
    }

    private static Gen<string> IntExpression(int depth)
    {
        Gen<string> leaf = Gen.OneOf(
            Gen.Const("a"), Gen.Const("b"), Gen.Const("x"), Gen.Const("k"), Gen.Const("(int)u"), Gen.Const("(int)y"),
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

    private static Gen<string> BoolExpression(int depth)
    {
        Gen<string> leaf = Gen.OneOf(Gen.Const("e"), Gen.Const("f"), Gen.Const("true"), Gen.Const("false"));
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
            Gen.Select(operand, Gen.OneOfConst("&&", "||", "&", "|", "^", "==", "!="), operand, static (l, op, r) => $"({l} {op} {r})"),
            operand.Select(static v => $"!{v}"),
            IntExpression(depth - 1).Select(static v => $"H.P({v})"));
    }

    /// <summary>Answers the helper's calls as <see cref="Helper"/> computes them; no other call is generated.</summary>
    private sealed class HelperOracle : ICallOracle
    {
        public static readonly HelperOracle Instance = new();

        private static readonly CallIdentity F = new("H::F(int)");

        private static readonly CallIdentity P = new("H::P(int)");

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            int x = (int)((IrBitVecValue)arguments[0]).TwosComplement;
            Assert.True(callee == F || callee == P, callee.Value);
            return callee == F
                ? new IrCallResult(IrBitVecValue.FromSigned(32, unchecked((x * 3) + 1)), Threw: false)
                : new IrCallResult(new IrBoolValue((x & 1) == 0), Threw: false);
        }
    }
}
