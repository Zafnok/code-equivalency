using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Expression-level opaque fragments carry their bound fingerprint and the variables they read (ADR 0024 decision 2; ticket
/// M4-004 criterion 2), and none when a function of those reads, the heap and the position would not describe them.
/// </summary>
public sealed class FragmentLoweringTests
{
    private const string Linq = "using System.Linq;\nusing System.Collections.Generic;\n";

    [Fact]
    public void ExpressionOpaqueCarriesFingerprintAndReads()
    {
        IrProcedure procedure = Source(Linq + "class C { int F; int M(int[] xs, int k, int unused) { F = 1; return xs.Count(x => x > k + F); } }");

        IrOpaque fragment = Assert.Single(Opaques(procedure));
        Assert.Equal("DelegateCreation", fragment.Reason);
        Assert.Matches("^[0-9a-f]{64}$", fragment.Fingerprint);
        Assert.Equal(["k"], fragment.Reads.Select(static r => r.SourceName), StringComparer.Ordinal);
        Assert.NotNull(fragment.Threw);
        Assert.Contains(procedure.Blocks, b => b.Terminator is IrBranch branch && branch.Cond == fragment.Threw);
        Assert.Equal(["field.C.F"], fragment.Heap.Select(static h => h.Map), StringComparer.Ordinal);
    }

    [Fact]
    public void AReferenceReadIsFollowedByItsNullShadow()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { int M(int[] xs, string s) => xs.Count(x => x > s.Length); }")));

        Assert.Equal<IrType>([new IrSort("System.String"), new IrBool()], fragment.Reads.Select(static r => r.Type));
    }

    [Fact]
    public void RenamingLocalsAndParametersKeepsTheFingerprintAndAnotherBodyChangesIt()
    {
        static string Fingerprint(string method) => Assert.Single(Opaques(Source(Linq + "class C { " + method + " }"))).Fingerprint!;

        string original = Fingerprint("int M(int[] xs, int k) { int m = k; return xs.Count(x => x > m); }");

        Assert.Equal(original, Fingerprint("int M(int[] ys, int limit) { int bound = limit; return ys.Count(y => y > bound); }"));
        Assert.NotEqual(original, Fingerprint("int M(int[] xs, int k) { int m = k; return xs.Count(x => x >= m); }"), StringComparer.Ordinal);
    }

    [Fact]
    public void FragmentAssigningALocalHasNoFingerprint()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { int M(int[] xs) { int t = 0; xs.Count(x => (t = x) > 0); return t; } }")));

        Assert.Null(fragment.Fingerprint);
        Assert.Empty(fragment.Reads);
    }

    [Fact]
    public void FragmentWritingAnOutArgumentHasNoFingerprint()
    {
        IrProcedure procedure = Method("static void Out(ref int n) { } int M(int n) { Out(ref n); return n; }");

        Assert.All(Opaques(procedure), static o => Assert.Null(o.Fingerprint));
    }

    [Theory]
    [InlineData("int k = 1; var q = xs.Where(x => x > k); k = 2; return q.Count();")]
    [InlineData("int k = 0; int n = 0; for (int i = 0; i < 3; i++) { n += xs.Count(x => x > k); k++; } return n;")]
    [InlineData("int k = 1; System.Action bump = () => k++; var q = xs.Where(x => x > k); bump(); return q.Count();")]
    public void LambdaCapturingALaterWrittenLocalHasNoFingerprint(string body)
    {
        IrProcedure procedure = Source(Linq + "class C { int M(int[] xs) { " + body + " } }");

        Assert.All(Opaques(procedure).Where(static o => string.Equals(o.Reason, "DelegateCreation", StringComparison.Ordinal)), static o => Assert.Null(o.Fingerprint));
    }

    [Fact]
    public void LambdaCapturingALocalWrittenOnlyBeforeItIsFingerprinted()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { int M(int[] xs, int a) { int k = 1; if (a > 0) { k = 2; } var q = xs.Where(x => x > k); return q.Count(); } }")));

        Assert.NotNull(fragment.Fingerprint);
        Assert.Equal(["k"], fragment.Reads.Select(static r => r.SourceName), StringComparer.Ordinal);
    }

    [Fact]
    public void RuntimeSensitiveFragmentHasNoFingerprint()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { int M(double[] xs) => xs.Sum(x => (int)x); }")));

        Assert.Null(fragment.Fingerprint);
    }

    [Theory]
    [InlineData("int M(int[] xs) { int Twice(int x) => 2 * x; return xs.Sum(x => Twice(x)); }")]
    [InlineData("int M(int[] xs) { int Twice(int x) => 2 * x; return xs.Sum(Twice); }")]
    public void AFragmentThatCallsALocalFunctionDeclaredOutsideItHasNoFingerprint(string method)
    {
        Assert.All(Opaques(Source(Linq + "class C { " + method + " }")), static o => Assert.Null(o.Fingerprint));
    }

    [Fact]
    public void ALocalFunctionDeclaredInsideTheFragmentIsPartOfIt()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { int M(int[] xs) => xs.Sum(x => { int Twice(int y) => 2 * y; return Twice(x); }); }")));

        Assert.NotNull(fragment.Fingerprint);
    }

    [Fact]
    public void AStructsThisInAFragmentHasNoFingerprint()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source("struct C { int F; int M() => F; }")));

        Assert.Equal("InstanceReference", fragment.Reason);
        Assert.Null(fragment.Fingerprint);
    }

    [Fact]
    public void AFragmentOfAFlowCaptureHasNoFingerprint()
    {
        IrOpaque captured = Assert.Single(Opaques(Source("class C { int? M(int? a, int? b, int? c) => (a ?? b) + c; }")), static o => string.Equals(o.Reason, "Binary", StringComparison.Ordinal));
        IrOpaque direct = Assert.Single(Opaques(Source("class C { int? M(int? a, int? b, int? c) => a + c; }")), static o => string.Equals(o.Reason, "Binary", StringComparison.Ordinal));

        Assert.Null(captured.Fingerprint);
        Assert.NotNull(direct.Fingerprint);
    }

    [Fact]
    public void AFragmentCallingAMethodIsFingerprinted()
    {
        IrProcedure procedure = Source(Linq + "class C { int M(int[] xs) => xs.Sum(x => System.Math.Abs(x)) + xs.Sum(System.Math.Abs); }");

        Assert.All(Opaques(procedure), static o => Assert.NotNull(o.Fingerprint));
        Assert.Equal(2, Opaques(procedure).Length);
    }

    [Fact]
    public void AQueryIsOneFragmentOverWhatItReads()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { int M(int[] xs, int k) => (from x in xs where x > k select x).Count(); }")));

        Assert.NotNull(fragment.Fingerprint);
        Assert.Equal(["xs", "xs", "k"], fragment.Reads.Select(static r => r.SourceName), StringComparer.Ordinal);
        Assert.Equal<IrType>([new IrSort("int[]"), new IrBool(), new IrBitVec(32)], fragment.Reads.Select(static r => r.Type));
    }

    [Fact]
    public void APatternIsNotAnExpressionAndHasNoFingerprint()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source("class C { int M(object o) { switch (o) { case string { Length: 3 }: return 1; default: return 0; } } }")));

        Assert.Equal("switch-pattern", fragment.Reason);
        Assert.Null(fragment.Fingerprint);
    }

    [Fact]
    public void AFragmentReadingAVariableTheLoweringDoesNotTrackHasNoFingerprint()
    {
        IrProcedure procedure = Source(Linq + "class C(int p) { int M(int[] xs) => xs.Count(x => x > p); }");

        Assert.Null(Assert.Single(Opaques(procedure), static o => string.Equals(o.Reason, "DelegateCreation", StringComparison.Ordinal)).Fingerprint);
    }

    [Fact]
    public void AFragmentReadingARefLocalHasNoFingerprint()
    {
        IrProcedure procedure = Source("class C { int? M(int[] xs, int? c) { ref int r = ref xs[0]; return r + c; } }");

        Assert.Null(Assert.Single(Opaques(procedure), static o => string.Equals(o.Reason, "Binary", StringComparison.Ordinal)).Fingerprint);
    }
}
