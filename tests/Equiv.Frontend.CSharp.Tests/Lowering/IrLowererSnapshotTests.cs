using Equiv.Core.Ir;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>IR dumps of one snippet per lowered construct (ticket M2-003 acceptance criterion 2).</summary>
public sealed class IrLowererSnapshotTests
{
    [Fact]
    public Task StraightLineArithmetic() => Dump("static int M(int a, int b) { int x = a + b; x = x * 2; return x - b; }");

    [Fact]
    public Task BoolLogic() => Dump("static bool M(bool a, bool b, bool c) => (a && b) || !c;");

    [Fact]
    public Task Comparisons() => Dump("static bool M(uint a, uint b, long c, long d) { bool x = a < b; bool y = c >= d; return x ^ y; }");

    [Fact]
    public Task IfElse() => Dump("static int M(int a) { int r; if (a > 0) r = a; else r = -a; return r; }");

    [Fact]
    public Task NestedIf() => Dump("""
        static int M(int a, int b)
        {
            int r = 0;
            if (a > b) { if (a > 0) r = a; else r = b; }
            else if (b == 0) return -1;
            return r + 1;
        }
        """);

    [Fact]
    public Task CheckedArithmetic() => Dump("static int M(int a, int b) => checked(a * b + a);");

    [Fact]
    public Task Division() => Dump("static uint M(int a, int b, uint c, uint d) => (uint)(a / b) + c % d;");

    [Fact]
    public Task Conversions() => Dump("static long M(byte b, short s, char c, int i, uint u) { long x = b + s + c; int n = (int)u; sbyte t = (sbyte)i; return x + n + t; }");

    [Fact]
    public Task CheckedConversion() => Dump("static byte M(int i, long l) => checked((byte)(i + (int)l));");

    [Fact]
    public Task Shifts() => Dump("static long M(long a, int n, int m) => (a << n) + (m >> 3) + ((uint)m >> n);");

    [Fact]
    public Task CompoundAssignment() => Dump("static byte M(byte b, int a, int n) { b += 1; a *= a; a <<= n; a /= n; return b; }");

    [Fact]
    public Task IncrementAndDecrement() => Dump("static int M(int a, char c) { a++; --a; c--; return a + c; }");

    [Fact]
    public Task SwitchStatement() => Dump("static int M(int n) { switch (n) { case 1: return 10; case 2: case 3: return 30; default: return 0; } }");

    [Fact]
    public Task SwitchExpression() => Dump("static int M(char c) => c switch { 'a' => 1, 'b' => 2, _ => 0 };");

    [Fact]
    public Task NullChecks() => Dump("static int M(string s, C c) { if (s == null) return 0; c.F(); return s.CompareTo(s); } void F() { }");

    [Fact]
    public Task ConditionalExpression() => Dump("static int M(bool b, int x) => b ? x : -x;");

    [Fact]
    public Task OpaqueCall() => Dump("static int M(int a) { Console.WriteLine(a); return Math.Abs(a); }");

    [Fact]
    public Task RefAndOutParameters() => Dump("static void M(ref int a, out int b, bool c) { b = a; if (c) a = checked(a + 1); }");

    [Fact]
    public Task VoidEarlyReturn() => Dump("static void M(bool c, int a) { if (c) return; Console.WriteLine(a); }");

    [Fact]
    public Task FieldAndThrowAreOpaque() => Dump("int f; int M(int a) { if (a < 0) throw new ArgumentException(); return f + a; }");

    [Fact]
    public Task ThrowOfANewObject() => Dump("class E : Exception { public E(int n) { } } static int M(int a) { if (a < 0) throw new E(a); return a; }");

    [Fact]
    public Task EntirelyOpaque() => Dump("static int M(int[] xs) { int s = 0; foreach (int x in xs) s += x; return s; }");

    [Fact]
    public Task WhileLoop() => Dump("static int M(int n) { int s = 0; while (n > 0) { s = s + n; n = n - 1; } return s; }");

    [Fact]
    public Task ForLoop() => Dump("static int M(int n) { int s = 0; for (int i = 0; i < n; i++) { if (i == 3) break; s += i; } return s; }");

    [Fact]
    public Task DoWhileLoop() => Dump("static int M(int n) { int s = 0; do { s += n; n--; } while (n > 0); return s; }");

    private static Task Dump(string members) => Verify(IrText.Dump(Lowered.Method(members)));
}
