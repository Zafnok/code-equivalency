using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// A call of a local function in a member's own body (ticket P2-171; ADR 0024 as clarified by it): an opaque with reason
/// <c>LocalFunction</c> that carries the fingerprint of the bound code the call runs and reads the call's arguments and
/// then the variables that code captures, and one with no fingerprint where a function of those, the heap and the
/// position would not describe the call.
/// </summary>
public sealed class LocalFunctionCallLoweringTests
{
    private const string Reason = "LocalFunction";

    [Fact]
    public void ACallCarriesTheFunctionsFingerprintAndReadsItsArgumentsThenItsCaptures()
    {
        IrProcedure procedure = Method("int F; int M(int a, string s) { int k = a + 1; int L(int v, long w) => v + k + s.Length + F; F = 3; return L(a * 2, 5L); }");

        IrOpaque call = Assert.Single(Opaques(procedure));
        Assert.Equal(Reason, call.Reason);
        Assert.Matches("^[0-9a-f]{64}$", call.Fingerprint);
        Assert.Equal<IrType>([new IrBitVec(32), new IrBitVec(64), new IrBitVec(32), new IrSort("System.String"), new IrBool()], call.Reads.Select(static r => r.Type));
        Assert.Equal(["k", "s", "s"], call.Reads.Skip(2).Select(static r => r.SourceName), StringComparer.Ordinal);
        Assert.Equal(new IrBitVec(32), call.Target!.Type);
        Assert.Contains(procedure.Blocks, b => b.Terminator is IrBranch branch && branch.Cond == call.Threw);
        Assert.Equal(["field.C.F"], call.Heap.Select(static h => h.Map), StringComparer.Ordinal);
        Assert.Empty(Calls(procedure));
    }

    [Fact]
    public void RenamingKeepsTheFingerprintAndAnotherBodyChangesIt()
    {
        string original = Fingerprint("int M(int a) { int k = a; int L(int v) => v + k; return L(1); }");

        Assert.Equal(original, Fingerprint("int M(int b) { int n = b; int Add(int x) => x + n; return Add(2); }"), StringComparer.Ordinal);
        Assert.Equal(original, Fingerprint("int M(int a) { int k = a; return L(1) + 3; int L(int v) => v + k; }"), StringComparer.Ordinal);
        Assert.NotEqual(original, Fingerprint("int M(int a) { int k = a; int L(int v) => v - k; return L(1); }"), StringComparer.Ordinal);
        Assert.NotEqual(original, Fingerprint("int M(int a) { int k = a; int L(int v) => v + a; return L(1); }"), StringComparer.Ordinal);
        Assert.NotEqual(original, Fingerprint("int M(int a) { long k = a; int L(int v) => v + (int)k; return L(1); }"), StringComparer.Ordinal);
        Assert.NotEqual(original, Fingerprint("int M(int a) { int k = a; static int L(int v) => v + 1; return L(1); }"), StringComparer.Ordinal);
    }

    /// <summary>The method's parameters are numbered as the function first reads them, not by position: the call is given them in that order.</summary>
    [Fact]
    public void AParameterIsNamedByWhereTheFunctionFirstReadsIt()
    {
        IrOpaque call = Assert.Single(Opaques(Method("int M(int a, int b) { int L() => b - a; return L(); }")));

        Assert.Equal(["b", "a"], call.Reads.Select(static r => r.SourceName), StringComparer.Ordinal);
        Assert.Equal(call.Fingerprint, Fingerprint("int M(int a, int b) { int L() => a - b; return L(); }"), StringComparer.Ordinal);
    }

    /// <summary>The type arguments a call gives a generic local function are part of what it runs.</summary>
    [Fact]
    public void ACallOfAGenericFunctionCarriesItsTypeArguments()
    {
        static string Of(string call) => Fingerprint("int M(int a) { static T Same<T>(T v) => v; return " + call + "; }");

        Assert.Equal(Of("Same<int>(a)"), Of("Same(a)"), StringComparer.Ordinal);
        Assert.NotEqual(Of("Same<int>(a)"), Of("(int)Same<long>(a)"), StringComparer.Ordinal);
    }

    /// <summary>The fingerprint holds every local function the call can run: one the called function declares, and one it reaches.</summary>
    [Fact]
    public void TheFingerprintHoldsEveryFunctionTheCallReaches()
    {
        const string Nested = "int M(int a) { int Outer(int v) { int Inner(int w) => w + 1; return Inner(v); } return Outer(a); }";
        const string Reached = "int M(int a) { int k = a + 1; int Inner(int w) => w + k; int Outer(int v) => Inner(v) + Inner(v); System.Func<int, int> f = Inner; return Outer(a); }";

        IrOpaque nested = Assert.Single(Opaques(Method(Nested)));
        IrOpaque reached = Assert.Single(Opaques(Method(Reached)), static o => string.Equals(o.Reason, Reason, StringComparison.Ordinal));

        Assert.Single(nested.Reads);
        Assert.Equal(["a", "k"], reached.Reads.Select(static r => r.SourceName), StringComparer.Ordinal);
        Assert.NotEqual(nested.Fingerprint, Fingerprint(Nested.Replace("w + 1", "w + 2", StringComparison.Ordinal)), StringComparer.Ordinal);
        Assert.NotEqual(reached.Fingerprint, Fingerprint(Reached.Replace("w + k", "w - k", StringComparison.Ordinal)), StringComparer.Ordinal);
        Assert.NotEqual(reached.Fingerprint, Fingerprint(Reached.Replace("=> Inner(v) + Inner(v)", "{ System.Func<int, int> g = Inner; return g(v) + g(v); }", StringComparison.Ordinal)), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("int M(int n) { int Fact(int i) => i <= 1 ? 1 : i * Fact(i - 1); return Fact(n); }", "int M(int n) { int Fact(int i) => i <= 1 ? 1 : i * Fact(i - 2); return Fact(n); }")]
    [InlineData(
        "bool M(int n) { bool Even(int i) => i == 0 || Odd(i - 1); bool Odd(int i) => i != 0 && Even(i - 1); return Even(n); }",
        "bool M(int n) { bool Even(int i) => i == 0 || Odd(i - 1); bool Odd(int i) => i != 0 && Even(i - 2); return Even(n); }")]
    public void ARecursiveFunctionIsInItsFingerprintOnce(string members, string changed)
    {
        IrOpaque call = Assert.Single(Opaques(Method(members)));

        Assert.NotNull(call.Fingerprint);
        Assert.NotEqual(call.Fingerprint, Fingerprint(changed), StringComparer.Ordinal);
    }

    [Fact]
    public void TwoCallsOfOneFunctionHaveOneFingerprint()
    {
        ImmutableArray<IrOpaque> calls = Opaques(Method("int M(int a) { int L(int v) => v + 1; return L(a) + L(2); }"));

        Assert.Equal(2, calls.Length);
        Assert.Equal(calls[0].Fingerprint, calls[1].Fingerprint, StringComparer.Ordinal);
        Assert.NotNull(calls[0].Fingerprint);
    }

    /// <summary>The arguments are evaluated in source order, as any call's, and read in parameter order.</summary>
    [Fact]
    public void TheArgumentsAreEvaluatedInSourceOrderAndPassedInParameterOrder()
    {
        IrProcedure procedure = Method("static int F() => 1; static int G() => 2; int M() { int L(int x, int y) => x - y; return L(y: G(), x: F()); }");

        IrOpaque call = Assert.Single(Opaques(procedure));
        ImmutableArray<IrCall> evaluated = Calls(procedure);
        Assert.Equal(["C::G()", "C::F()"], evaluated.Select(static c => c.Callee.Value), StringComparer.Ordinal);
        Assert.Equal([evaluated[1].Target!, evaluated[0].Target!], call.Reads);
    }

    /// <summary>
    /// The calls a function of the arguments, the reads, the heap and the position would not describe: an output that is
    /// not the result (a <c>ref</c> or <c>out</c> argument, a capture the function writes), a read whose value the
    /// lowering does not have (one a lambda writes, a primary constructor's parameter), a result that is a reference to
    /// a variable, and a body the pair's two runtimes run differently.
    /// </summary>
    [Theory]
    [InlineData("int M() { void L(out int v) { v = 1; } L(out int x); return x; }")]
    [InlineData("int M(int a) { void L(ref int v) { v++; } L(ref a); return a; }")]
    [InlineData("int M() { int n = 0; void L() { n++; } L(); return n; }")]
    [InlineData("int M() { int n = 0; void Inner() { n = 2; } void L() { Inner(); } L(); return n; }")]
    [InlineData("int M() { int n = 0; Action bump = () => n++; int L() => n; bump(); return L(); }")]
    [InlineData("int M(int[] xs) { ref int L() => ref xs[0]; return L(); }")]
    [InlineData("int M(double d) { int L(double v) => (int)v; return L(d); }")]
    public void ACallThatIsNoFunctionOfWhatItReadsHasNoFingerprint(string members)
    {
        ImmutableArray<IrOpaque> calls = [.. Opaques(Method(members)).Where(static o => string.Equals(o.Reason, Reason, StringComparison.Ordinal))];

        Assert.NotEmpty(calls);
        Assert.All(calls, static o => Assert.Null(o.Fingerprint));
        Assert.All(calls, static o => Assert.Empty(o.Reads));
    }

    [Fact]
    public void ACallThatReadsAVariableTheLoweringDoesNotTrackHasNoFingerprint()
    {
        IrOpaque call = Assert.Single(Opaques(Source("class C(int p) { int M() { int L() => p; return L(); } }")));

        Assert.Equal(Reason, call.Reason);
        Assert.Null(call.Fingerprint);
    }

    /// <summary>
    /// A local function holds its captures by reference, and a lambda, an iterator or an <c>async</c> function in it can
    /// read one after the call returns, so a call one of whose reads is assigned after it is not shared (ADR 0024 decision 2).
    /// </summary>
    [Theory]
    [InlineData("int M(int a) { int k = a; int L() => k; int r = L(); k = 2; return r + k; }", false)]
    [InlineData("int M(int a) { int k = a; int L() => k; int r = 0; while (r < 3) { r += L(); k++; } return r; }", false)]
    [InlineData("int M(int a) { int k = a; k = 2; int L() => k; int r = L(); return r + k; }", true)]
    public void ACallWhoseReadIsAssignedAfterItIsNotShared(string members, bool shared)
    {
        IrOpaque call = Assert.Single(Opaques(Method(members)));

        Assert.Equal(Reason, call.Reason);
        Assert.Equal(shared, call.Fingerprint is not null);
    }

    /// <summary>On one runtime no runtime rule applies (ADR 0040), so the function the migration pair does not share is shared.</summary>
    [Fact]
    public void ARuntimeSensitiveFunctionIsSharedOnOneRuntime()
    {
        const string source = "class C { int M(double d) { int L(double v) => (int)v; return L(d); } }";

        Assert.Null(Assert.Single(Opaques(Source(source))).Fingerprint);
        Assert.NotNull(Assert.Single(Opaques(Source(source, runtime: Runtimes.Between("net10.0", "net10.0")))).Fingerprint);
    }

    /// <summary>
    /// Ticket P2-233: a local function can be called anywhere in the block that declares it, so an early exit earlier in
    /// that block does not guard its path, and its call is not shared. A guard in the function, or one around the block,
    /// does.
    /// </summary>
    [Theory]
    [InlineData("object M(string p) { var r = L(); if (!File.Exists(p)) return null; return r; object L() => new StreamReader(p); }", false)]
    [InlineData("object M(string p) { if (!File.Exists(p)) return null; var r = L(); return r; object L() => new StreamReader(p); }", false)]
    [InlineData("object M(string p) { var r = L(); return r; object L() { if (!File.Exists(p)) return null; return new StreamReader(p); } }", true)]
    [InlineData("object M(string p) { if (File.Exists(p)) { var r = L(); return r; object L() => new StreamReader(p); } return null; }", true)]
    [InlineData("object M(string p) { if (!File.Exists(p)) return null; { var r = L(); return r; object L() => new StreamReader(p); } }", true)]
    public void AnEarlyExitBeforeALocalFunctionInItsBlockDoesNotGuardIt(string members, bool shared)
    {
        IrOpaque call = Assert.Single(Opaques(Source("using System.IO;\nclass C\n{\n" + members + "\n}\n")), static o => string.Equals(o.Reason, Reason, StringComparison.Ordinal));

        Assert.Equal(shared, call.Fingerprint is not null);
    }

    private static string Fingerprint(string members) =>
        Assert.Single(Opaques(Method(members)), static o => string.Equals(o.Reason, Reason, StringComparison.Ordinal)).Fingerprint!;
}
