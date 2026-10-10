using System.Globalization;

using CsCheck;

using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// <c>==</c> and <c>!=</c> on two operands of one enum type are the IR's equality on the enum's sort (ticket P2-087): an
/// enum value is the element of its sort that a constant of the same underlying value is, so two values are equal exactly
/// when their elements are. Every other operator on an enum reads the underlying integer, which the sort does not hold,
/// and stays opaque with reason <c>Binary</c>, as a lifted comparison does.
/// </summary>
public sealed class EnumEqualityLoweringTests
{
    private const string Seed = "000000000000";

    private const int Samples = 100;

    private const string E = "enum E { A, B, C }\n";

    private static IrValue Element(int value) => TypeMapper.Constant("E", value.ToString(CultureInfo.InvariantCulture))!;

    [Theory]
    [InlineData("==", IrBinaryOp.Eq)]
    [InlineData("!=", IrBinaryOp.Ne)]
    public void EnumEqualityIsEqualityOnTheEnumsSort(string op, IrBinaryOp expected)
    {
        IrProcedure procedure = Source($"{E}class C {{ static bool M(E x, E y) => x {op} y; }}");

        Assert.Empty(Opaques(procedure));
        IrBinary comparison = Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrBinary>());
        Assert.Equal(expected, comparison.Op);
        Assert.Equal(new IrSort("E"), comparison.A.Type);
        Assert.Equal(new IrSort("E"), comparison.B.Type);
        Assert.Equal(new IrBool(), comparison.Target.Type);
    }

    [Theory]
    [InlineData("static int M(E x) { if (x == E.B) return 1; return 0; }", 1, 1)]
    [InlineData("static int M(E x) { if (x == E.B) return 1; return 0; }", 2, 0)]
    [InlineData("static int M(E x) { if (E.A != x) return 1; return 0; }", 0, 0)]
    [InlineData("static int M(E x) { if (E.A != x) return 1; return 0; }", 7, 1)]
    [InlineData("static int M(E x) { return x == default(E) ? 1 : 0; }", 0, 1)]
    [InlineData("static int M(E x) { return x == 0 ? 1 : 0; }", 0, 1)]
    public void AnEnumComparedWithAConstantBranchesAsCSharpDoes(string members, int x, int expected)
    {
        IrProcedure procedure = Source($"{E}class C {{ {members} }}");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Element(x)));
    }

    /// <summary>
    /// What reads the underlying integer, a lifted comparison, and a comparison of two different enums in code that does
    /// not compile, all stay opaque.
    /// </summary>
    [Theory]
    [InlineData("static bool M(E x, E y) => x < y;")]
    [InlineData("static bool M(E x, E y) => x >= y;")]
    [InlineData("static E M(E x, E y) => x & y;")]
    [InlineData("static E M(E x, E y) => x | y;")]
    [InlineData("static int M(E x, E y) => x - y;")]
    [InlineData("static bool M(E? x, E? y) => x == y;")]
    [InlineData("static bool M(E? x, E y) => x != y;")]
    public void EveryOtherEnumOperatorStaysOpaque(string members) =>
        Assert.Contains(Opaques(Source($"{E}class C {{ {members} }}")), static o => o.Reason is "Binary");

    [Fact]
    public void TwoDifferentEnumsAreNotCompared() =>
        Assert.Contains(Opaques(ErroneousBody("enum F { A } enum G { A } static bool M(F x, G y) { return x == y; }")), static o => o.Reason is "Binary");

    /// <summary>
    /// Criterion 3's property: on random operands the lowered <c>==</c> and <c>!=</c> agree with C#, whether the right
    /// operand is a value the method is given or a constant in its text, defined by the enum or not.
    /// </summary>
    [Fact]
    public void LoweredEnumEqualityAgreesWithCSharp() =>
        Gen.Select(Gen.OneOf(Gen.Int[-2, 3], Gen.Int), Gen.OneOf(Gen.Int[-2, 3], Gen.Int), Gen.Bool).Sample(
            static (a, b, equals) =>
            {
                string op = equals ? "==" : "!=";
                string source = $"{E}class C {{ static bool M(E x, E y) => x {op} y; static bool K(E x) => x {op} (E)({b.ToString(CultureInfo.InvariantCulture)}); }}";
                IrReturned expected = new(new IrBoolValue(((DayOfWeek)a == (DayOfWeek)b) == equals));

                Assert.Equal(expected, Run(Source(source), Element(a), Element(b)));
                Assert.Equal(expected, Run(Source(source, "K"), Element(a)));
            },
            seed: Seed,
            iter: Samples,
            print: static t => string.Create(CultureInfo.InvariantCulture, $"{t.Item1} {(t.Item3 ? "==" : "!=")} {t.Item2}"));
}
