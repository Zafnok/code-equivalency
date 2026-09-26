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
    public Task DecimalArithmetic() => Dump("static decimal M(decimal price, int quantity, decimal discount) { try { return price * quantity / discount; } catch (OverflowException) { return 0m; } }");

    [Fact]
    public Task FloatingPointComparisonAndConversion() => Dump("static int M(double a, float b) => a < b ? checked((int)a) : (int)-b;");

    [Fact]
    public Task UserDefinedOperatorAndStringEquality() => Dump("struct Money { public static Money operator +(Money a, Money b) => a; } static bool M(Money a, Money b, string s, string t) { Money c = a + b; return s == t; }");

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
    public Task TryCatch() => Dump("""
        static int M(int a)
        {
            try { if (a < 0) throw new ArgumentException(); return a; }
            catch (ArgumentException) { return -1; }
        }
        """);

    [Fact]
    public Task TryFinally() => Dump("""
        static int M(int a)
        {
            int s = 0;
            try { if (a > 0) return 1; s = 2; }
            finally { s = s + 1; }
            return s;
        }
        """);

    [Fact]
    public Task FinallyThatNeverCompletes() => Dump("""
        static int M(int k)
        {
            try { if (k == 1) goto done; k = 2; }
            finally { throw new InvalidOperationException(); }
            done: return k;
        }
        """);

    [Fact]
    public Task ConditionalRethrow() => Dump("""
        static int M(int a, int b)
        {
            try { return a / b; }
            catch (DivideByZeroException) { if (a > 5) throw; }
            return -1;
        }
        """);

    [Fact]
    public Task ConditionalExpression() => Dump("static int M(bool b, int x) => b ? x : -x;");

    [Fact]
    public Task OpaqueCall() => Dump("static int M(int a) { Console.WriteLine(a); return Math.Abs(a); }");

    [Fact]
    public Task RefAndOutParameters() => Dump("static void M(ref int a, out int b, bool c) { b = a; if (c) a = checked(a + 1); }");

    [Fact]
    public Task VoidEarlyReturn() => Dump("static void M(bool c, int a) { if (c) return; Console.WriteLine(a); }");

    [Fact]
    public Task InstanceFieldAndThrow() => Dump("int f; int M(int a) { if (a < 0) throw new ArgumentException(); return f + a; }");

    [Fact]
    public Task StaticFieldWrite() => Dump("static int f; static int M(int a) { f = a; return f; }");

    [Fact]
    public Task ArrayElements() => Dump("static int M(int[] a, int i) { a[i] = a[0]; return a[i] + a.Length; }");

    /// <summary>Ticket P2-001 acceptance criterion 1: an array creation and its element writes, with no opaque.</summary>
    [Fact]
    public Task ArrayCreation() => Dump("static int[] M(int a, int b) { var r = new int[2]; r[0] = a; r[1] = b; return r; }");

    /// <summary>Ticket P2-001 acceptance criterion 1: an initialiser, indexed straight away, with no opaque.</summary>
    [Fact]
    public Task ArrayInitializer() => Dump("static int M(int n) => new[] { n, n + 1 }[0];");

    [Fact]
    public Task ThrowOfANewObject() => Dump("class E : Exception { public E(int n) { } } static int M(int a) { if (a < 0) throw new E(a); return a; }");

    [Fact]
    public Task EntirelyOpaque() => Dump("static int M(object o, int a) { lock (o) { a = a + 1; } return a; }");

    [Fact]
    public Task WhileLoop() => Dump("static int M(int n) { int s = 0; while (n > 0) { s = s + n; n = n - 1; } return s; }");

    [Fact]
    public Task ForLoop() => Dump("static int M(int n) { int s = 0; for (int i = 0; i < n; i++) { if (i == 3) break; s += i; } return s; }");

    [Fact]
    public Task DoWhileLoop() => Dump("static int M(int n) { int s = 0; do { s += n; n--; } while (n > 0); return s; }");

    [Fact]
    public Task InstancePropertyRead() => Dump("public virtual int P { get; set; } static int M(C c) => c.P;");

    [Fact]
    public Task StaticPropertyWrite() => Dump("static int p; static int P { get => p; set => p = value; } static void M(int a) { P = a; }");

    [Fact]
    public Task IndexerRead() => Dump("int this[int i] => i; static int M(C c, int i) => c[i];");

    [Fact]
    public Task CompoundAssignmentToAProperty() => Dump("public virtual int P { get; set; } static int M(C c, int a) => checked(c.P += a);");

    [Fact]
    public Task BoxingAnInt() => Dump("static object M(int a) => a;");

    [Fact]
    public Task SwitchOnTypePatterns() => Dump("""
        static int M(object o)
        {
            switch (o)
            {
                case string s: return s.Length;
                case Exception: return 1;
            }

            return o switch { IComparable => 2, ArgumentException e => e.HResult, _ => 0 };
        }
        """);

    [Fact]
    public Task OutArgumentOfAnOpaqueCall() => Dump("static int M(string s, int fallback) => int.TryParse(s, out var n) ? n : fallback;");

    /// <summary>Ticket M4-003: a reference-typed <c>out</c> argument is a call output, and its variable's nullness asks the <c>null.*</c> map.</summary>
    [Fact]
    public Task TryGetValue() => Dump("static string M(System.Collections.Generic.Dictionary<int, string> d, string f) => d.TryGetValue(1, out string v) ? v : f;");

    [Fact]
    public Task ForEachOverList() => Dump("static int M(System.Collections.Generic.List<int> l) { int s = 0; foreach (int x in l) s += x; return s; }");

    [Fact]
    public Task ForEachOverIEnumerableOfInt() => Dump("static int M(System.Collections.Generic.IEnumerable<int> xs) { int s = 0; foreach (int x in xs) s += x; return s; }");

    [Fact]
    public Task ForEachWithBreak() => Dump("static int M(System.Collections.Generic.IEnumerable<int> xs) { int s = 0; foreach (int x in xs) { if (x < 0) break; s += x; } return s; }");

    [Fact]
    public Task UsingStatement() => Dump("static int M(IDisposable d, int a) { using (d) { a = a + 1; } return a; }");

    [Fact]
    public Task UsingDeclaration() => Dump("static int M(System.IO.Stream s) { using System.IO.Stream t = s; return t.ReadByte(); }");

    /// <summary>Ticket P1-005: a call with one field map live, first touched after it; the read takes the call's new version.</summary>
    [Fact]
    public Task CallWithOneHeapMap() => Dump("int f; void Foo() { } int M() { Foo(); return f; }");

    /// <summary>Ticket P1-005: a call with a field map and an array map live, each paired, in name order.</summary>
    [Fact]
    public Task CallWithTwoHeapMaps() => Dump("int f; void Foo() { } int M(int[] a) { a[0] = f; Foo(); return a[0] + f; }");

    /// <summary>Ticket P1-005: a call in a body with no field or array access has no heap pairs, so its dump is unchanged.</summary>
    [Fact]
    public Task CallWithNoHeapMap() => Dump("static int Foo(int a) => a; static int M(int a) { int b = Foo(a); return Foo(b); }");

    /// <summary>Ticket P2-003 acceptance criterion 1: a class-constrained type parameter's <c>default</c> is the null element of its sort, with its <c>isNull</c> shadow set.</summary>
    [Fact]
    public Task DefaultValueOfATypeParameter() => Dump("static T M<T>(bool has, T value) where T : class => has ? value : default;");

    /// <summary>Ticket P2-008 acceptance criterion 1: the CFG's <c>IsNull</c> of <c>?.</c> and <c>??</c> reads the operand's null shadow.</summary>
    [Fact]
    public Task NullConditionalLengthWithFallback() => Dump("static int M(string s) => s?.Length ?? 0;");

    /// <summary>Ticket P2-008 acceptance criterion 1: <c>?.</c> on a property, then <c>??</c> with a string constant.</summary>
    [Fact]
    public Task NullConditionalPropertyWithFallback() => Dump("class Person { public string Name { get; } } static string M(Person p) => p?.Name ?? \"anonymous\";");


    /// <summary>Ticket M4-008 acceptance criterion 1: an arrow-bodied getter, lowered through its expression's graph.</summary>
    [Fact]
    public Task ArrowAccessor() => Dump("int f; int P => f * 2 + 1;", "get_P");

    /// <summary>Ticket M4-008 acceptance criterion 2: an auto-property's setter writes its backing field's map at the receiver.</summary>
    [Fact]
    public Task AutoPropertySetter() => Dump("int P { get; set; }", "set_P");

    /// <summary>Ticket M4-008 acceptance criterion 4: a false filter passes the exception to the bare <c>catch</c> after it.</summary>
    [Fact]
    public Task CatchWithAFalseFilter() =>
        Dump("static int M(int a, int b) { try { return a / b; } catch (DivideByZeroException) when (a > 0) { return 1; } catch { return 2; } }");

    /// <summary>Ticket M4-008 acceptance criterion 4: the filter's own division by zero goes to its false exit.</summary>
    [Fact]
    public Task FilterThatThrows() =>
        Dump("static int M(int a, int b) { try { return a / b; } catch (DivideByZeroException) when (10 / a > 0) { return 1; } }");

    /// <summary>Ticket M4-008: the initializers' graphs, then the constructor's, its base call first.</summary>
    [Fact]
    public Task ConstructorWithFieldInitializers() => Dump("int f = 1; int P { get; } = 2; C(int a) { f = a; }", ".ctor");

    private static Task Dump(string members, string name = "M") => Verify(IrText.Dump(Lowered.Method(members, name)));
}
