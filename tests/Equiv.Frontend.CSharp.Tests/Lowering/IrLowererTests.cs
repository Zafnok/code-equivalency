using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>Behaviour of <c>IrLowerer</c> beyond the snapshots: opaque reasons (acceptance criterion 5) and interpreted results.</summary>
public sealed class IrLowererTests
{
    private static readonly IrBitVecValue Zero32 = Bits(32, 0);

    [Theory]
    [InlineData("C() { }", ".ctor", "ConstructorBodyOperation")]
    [InlineData("int M => 1;", "get_M", "Block")]
    [InlineData("int M { get; }", "get_M", "no-body")]
    [InlineData("static int M(int n) { int s = 0; while (n > 0) { s = s + n; n = n - 1; } return s; }", "M", "loop")]
    [InlineData("static void M(int[] xs) { foreach (int x in xs) { } }", "M", "loop")]
    [InlineData("static int M(int n) { switch (n) { case 1: return 2; } return 0; }", "M", "switch")]
    [InlineData("static int M(int n) => n switch { 1 => 2, _ => 0 };", "M", "switch")]
    [InlineData("static int M(int n) { try { return n; } catch (Exception) { return 0; } }", "M", "try-region")]
    [InlineData("static int M(IDisposable d) { using (d) { return 1; } }", "M", "try-region")]
    [InlineData("static int M(IDisposable d, int n) { if (n > 0) { using IDisposable e = d; return 1; } return n; }", "M", "try-region")]
    public void WholeBodyIsOneOpaque(string members, string name, string reason)
    {
        IrProcedure procedure = Method(members, name);

        IrBlock block = Assert.Single(procedure.Blocks);
        Assert.Equal(reason, Assert.Single(Opaques(procedure)).Reason);
        Assert.IsType<IrReturn>(block.Terminator);
    }

    [Fact]
    public void WholeBodyOpaqueKeepsByRefParametersAsOuts()
    {
        IrProcedure procedure = Method("static void M(ref int a, out int b) { b = 0; while (a > 0) a = a - 1; }");

        IrReturn exit = Assert.IsType<IrReturn>(Assert.Single(procedure.Blocks).Terminator);
        Assert.Null(exit.Value);
        Assert.Equal(["a", "b"], exit.Outs.Select(static o => o.Final.Name), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("int f; int M() => f;", "FieldReference")]
    [InlineData("int f; void M(int a) { f = a; }", "FieldReference")]
    [InlineData("static int M(int[] a) => a[0];", "ArrayElementReference")]
    [InlineData("static int M(string s) => s.Length;", "PropertyReference")]
    [InlineData("static string M(string s) => s.Trim();", "dereference")]
    [InlineData("static void M(System.Collections.Generic.List<int> l) { l.Clear(); }", "dereference")]
    [InlineData("int M() => GetHashCode();", "dereference")]
    [InlineData("static bool M(string s) => int.TryParse(s, out _);", "ref-argument")]
    [InlineData("static void M(ref int a) { System.Threading.Interlocked.Increment(ref a); }", "ref-argument")]
    [InlineData("static void M(int a) { if (a < 0) throw new ArgumentException(); }", "Throw")]
    [InlineData("static int M(int a) { if (a < 0) throw new ArgumentException(); return a; }", "Throw")]
    [InlineData("static void M(int a) { ref int r = ref a; r = 1; }", "SimpleAssignment")]
    [InlineData("static int M(double d) => (int)d;", "Conversion")]
    [InlineData("static double M(int i) => i;", "Conversion")]
    [InlineData("static object M() => (string)null;", "Conversion")]
    [InlineData("struct S { public static implicit operator int(S s) => 0; } static int M(S s) => s;", "Conversion")]
    [InlineData("static bool M(string a, string b) => a == b;", "Binary")]
    [InlineData("static double M(double a, double b) => a * b;", "Binary")]
    [InlineData("static double M(double d) => -d;", "Unary")]
    [InlineData("static double M(double d) => +d;", "Unary")]
    [InlineData("static decimal M(decimal d) => ~(int)d;", "Conversion")]
    [InlineData("static int? M(int? n) => ~n;", "Unary")]
    [InlineData("static bool? M(bool? b) => !b;", "Unary")]
    public void UnsupportedConstructIsOpaqueWithItsName(string members, string reason) =>
        Assert.Contains(Opaques(Method(members)), o => string.Equals(o.Reason, reason, StringComparison.Ordinal));

    [Theory]
    [InlineData("int M() => p;")]
    [InlineData("void M() { p = 1; }")]
    public void PrimaryConstructorParameterIsOpaque(string member)
    {
        IrProcedure procedure = Source($"class C(int p) {{ {member} }}");

        Assert.Equal("ParameterReference", Assert.Single(Opaques(procedure)).Reason);
    }

    [Theory]
    [InlineData("static int M(int a, int b) { a += b; return a; }", 5, 7, 12)]
    [InlineData("static int M(int a, int b) { a -= b; return a; }", 5, 7, -2)]
    [InlineData("static int M(int a, int b) { a *= b; return a; }", 5, 7, 35)]
    [InlineData("static int M(int a, int b) { a /= b; return a; }", 17, 5, 3)]
    [InlineData("static int M(int a, int b) { a %= b; return a; }", 17, 5, 2)]
    [InlineData("static int M(int a, int b) { a &= b; return a; }", 12, 10, 8)]
    [InlineData("static int M(int a, int b) { a |= b; return a; }", 12, 10, 14)]
    [InlineData("static int M(int a, int b) { a ^= b; return a; }", 12, 10, 6)]
    [InlineData("static int M(int a, int b) { a <<= b; return a; }", 3, 33, 6)]
    [InlineData("static int M(int a, int b) { a >>= b; return a; }", -8, 1, -4)]
    [InlineData("static int M(int a, int b) { a >>>= b; return a; }", -8, 1, int.MaxValue - 3)]
    public void CompoundAssignmentReadsOperatesAndWrites(string members, int a, int b, int expected) =>
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(Method(members), Bits(32, a), Bits(32, b)));

    [Theory]
    [InlineData("static int M(int a) { a++; return a; }", 5, 6)]
    [InlineData("static int M(int a) { a--; return a; }", 5, 4)]
    [InlineData("static int M(int a) { return ++a; }", 5, 6)]
    [InlineData("static int M(int a) { return a++; }", 5, 5)]
    [InlineData("static int M(int a) { return a--; }", 5, 5)]
    [InlineData("static int M(int a) { return --a; }", 5, 4)]
    public void IncrementAndDecrementYieldTheOldValueOnlyWhenPostfix(string members, int a, int expected) =>
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(Method(members), Bits(32, a)));

    /// <summary>The operands are promoted to <c>int</c> and the result is narrowed back, as C# does.</summary>
    [Theory]
    [InlineData("static byte M(byte b) { b += 250; return b; }", 8, 10, 4)]
    [InlineData("static char M(char c) { c++; return c; }", 16, 0xFFFF, 0)]
    [InlineData("static byte M(byte b) { b <<= 1; return b; }", 8, 0x81, 2)]
    [InlineData("static long M(long a) { a <<= 65; return a; }", 64, 3, 6)]
    public void CompoundAssignmentNarrowsBackToTheTargetType(string members, int width, long a, long expected) =>
        Assert.Equal(new IrReturned(Bits(width, expected)), Run(Method(members), Bits(width, a)));

    [Theory]
    [InlineData("static byte M(byte b) { checked { b += 250; } return b; }", 8, 10, "System.OverflowException")]
    [InlineData("static byte M(byte b) { checked { b += 1; } return b; }", 8, 10, null)]
    [InlineData("static int M(int a) { checked { a += 1; } return a; }", 32, int.MaxValue, "System.OverflowException")]
    [InlineData("static int M(int a) { checked { a++; } return a; }", 32, int.MaxValue, "System.OverflowException")]
    [InlineData("static int M(int a, int b) { a /= b; return a; }", 32, 1, "System.DivideByZeroException")]
    public void CompoundAssignmentKeepsTheOperatorExceptionEdges(string members, int width, long a, string? thrown)
    {
        IrProcedure procedure = Method(members);
        IrValue[] arguments = procedure.Parameters.Length == 1 ? [Bits(width, a)] : [Bits(width, a), Bits(width, 0)];

        Assert.Equal(thrown is null ? new IrReturned(Bits(width, a + 1)) : new IrThrew(thrown), Run(procedure, arguments));
    }

    [Theory]
    [InlineData("int f; void M(int a) { f += a; }", "FieldReference")]
    [InlineData("static void M(int[] xs) { xs[0]++; }", "ArrayElementReference")]
    [InlineData("static void M(double d) { d += 1; }", "CompoundAssignment")]
    [InlineData("static void M(double d) { d++; }", "Increment")]
    [InlineData("enum E { A } static void M(E e) { e += 1; }", "ParameterReference")]
    [InlineData("struct S { public static int operator +(int a, S b) => 0; } static void M(int a, S s) { a += s; }", "CompoundAssignment")]
    public void CompoundAssignmentToAnUnsupportedTargetIsOpaque(string members, string reason) =>
        Assert.Contains(Opaques(Method(members)), o => string.Equals(o.Reason, reason, StringComparison.Ordinal));

    [Fact]
    public void ReadWithoutDefinitionIsUndefined()
    {
        IrProcedure procedure = Method("static int M() { int x; return x; }", allowErrors: true);

        IrOpaque opaque = Assert.IsType<IrOpaque>(procedure.Blocks[0].Instructions[0]);
        Assert.Equal("undefined", opaque.Reason);
    }

    [Fact]
    public void FallingOffANonVoidMethodIsMissingReturn()
    {
        IrProcedure procedure = Method("static int M() { goto missing; }", allowErrors: true);

        Assert.Contains(Opaques(procedure), static o => o.Reason is "missing-return");
        Assert.Contains(Opaques(procedure), static o => o.Reason is "Invalid" && o.Target is null);
    }

    [Theory]
    [InlineData(5, 7)]
    [InlineData(-3, -3)]
    public void IfElseSelectsTheTakenBranch(int a, int expected) =>
        Assert.Equal(
            new IrReturned(Bits(32, expected)),
            Run(Method("static int M(int a) { int r = a; if (a > 0) { r = a + 2; } return r; }"), Bits(32, a)));

    [Fact]
    public void UnsignedAndCharConstantsKeepTheirBits()
    {
        Assert.Equal(new IrReturned(Bits(32, 6)), Run(Method("static uint M(uint a) => a + 5u;"), Bits(32, 1)));
        Assert.Equal(new IrReturned(Bits(16, 97)), Run(Method("static char M() => 'a';")));
        Assert.Equal(new IrReturned(Bits(32, 5)), Run(Method("const int K = 5; static int M() => K;")));
    }

    [Fact]
    public void CharIsSixteenBitsWide() =>
        Assert.Equal(new IrReturned(Bits(32, 4464)), Run(Method("static int M(int a) => (char)a;"), Bits(32, 70000)));

    [Theory]
    [InlineData(0x7FFF_FFFFL, false)]
    [InlineData(0x8000_0000L, true)]
    public void CheckedUnsignedToSignedThrowsWhenNegative(long u, bool throws) =>
        Assert.Equal(
            throws ? new IrThrew("System.OverflowException") : new IrReturned(Bits(32, u)),
            Run(Method("static int M(uint u) => checked((int)u);"), Bits(32, u)));

    [Fact]
    public void CheckedImplicitWideningEmitsNoOverflowTest()
    {
        IrProcedure procedure = Method("static long M(int a, long b) => checked(a + b);");

        Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOverflows>());
    }

    [Fact]
    public void CheckedBitwiseOperationCannotOverflow() =>
        Assert.Empty(Method("static int M(int a, int b) => checked(a & b);").Blocks.SelectMany(static b => b.Instructions).OfType<IrOverflows>());

    [Theory]
    [InlineData(int.MinValue, "System.OverflowException")]
    [InlineData(5, null)]
    public void CheckedNegationOverflowsOnlyAtMinValue(int a, string? thrown) =>
        Assert.Equal(
            thrown is null ? new IrReturned(Bits(32, -a)) : new IrThrew(thrown),
            Run(Method("static int M(int a) => checked(-a);"), Bits(32, a)));

    [Fact]
    public void BitwiseNotAndUnaryPlusLower()
    {
        IrProcedure procedure = Method("static int M(int a) => +(~a);");

        Assert.Equal(new IrReturned(Bits(32, ~41)), Run(procedure, Bits(32, 41)));
        Assert.Empty(Opaques(procedure));
    }

    [Fact]
    public void ValueTypeReceiverIsTheFirstArgument()
    {
        IrCall call = Assert.Single(Calls(Method("static string M(int a, IFormatProvider p) => a.ToString(p);")));

        Assert.Equal("System.Int32::ToString(System.IFormatProvider)", call.Callee.Value);
        Assert.Equal(["a", "p"], call.Args.Select(static a => a.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void NamedArgumentsArePassedInParameterOrder()
    {
        IrCall call = Assert.Single(Calls(Method("static int F(int x, int y) => x; static int M(int a, int b) => F(y: b, x: a);")));

        Assert.Equal(["a", "b"], call.Args.Select(static a => a.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void GotoLoopGetsPhisAtItsHeader() =>
        Assert.Equal(
            new IrReturned(Bits(32, 6)),
            Run(Method("static int M(int n) { int s = 0; top: if (n > 0) { s = s + n; n = n - 1; goto top; } return s; }"), Bits(32, 3)));

    [Fact]
    public void NestedGotoLoopsRemoveEveryTrivialPhi()
    {
        IrProcedure procedure = Method("""
            static int M(int n, int m)
            {
                int s = 0;
                outer:
                if (n > 0)
                {
                    int k = m;
                    inner:
                    if (k > 0) { s = s + 1; k = k - 1; goto inner; }
                    n = n - 1;
                    goto outer;
                }
                return s;
            }
            """);

        Assert.Equal(new IrReturned(Bits(32, 6)), Run(procedure, Bits(32, 2), Bits(32, 3)));
        Assert.DoesNotContain(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPhi>(), static p => p.Target.SourceName is "m");
    }

    [Fact]
    public void CapturedLocalIsAssignedThroughTheCapture() =>
        Assert.Equal(
            new IrReturned(Bits(32, 3)),
            Run(Method("static int M(bool b, int a) { int x = 0; x = b ? a : -a; return x; }"), new IrBoolValue(true), Bits(32, 3)));

    [Fact]
    public void DivisionByZeroThrows() =>
        Assert.Equal(new IrThrew("System.DivideByZeroException"), Run(Method("static uint M(uint a, uint b) => a / b;"), Bits(32, 1), Zero32));
}
