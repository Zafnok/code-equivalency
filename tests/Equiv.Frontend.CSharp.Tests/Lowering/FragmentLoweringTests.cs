using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Expression-level opaque fragments carry their bound fingerprint and the variables they read (ADR 0024 decision 2; ticket
/// M4-004 criterion 2), and none when a function of those reads, the heap and the position would not describe them. A lambda
/// converted to a delegate is no longer such a fragment (ticket P2-067; <see cref="DelegateLoweringTests"/>), so the
/// fragments here are a method group whose receiver is evaluated, a lifted operator and a query.
/// </summary>
public sealed class FragmentLoweringTests
{
    private const string Linq = "using System.Linq;\nusing System.Collections.Generic;\n";

    [Fact]
    public void ExpressionOpaqueCarriesFingerprintAndReads()
    {
        IrProcedure procedure = Source(Linq + "class C { int F; bool P(int x) => x > F; int M(int[] xs, C o, int unused) { F = 1; return xs.Count(o.P); } }");

        IrOpaque fragment = Assert.Single(Opaques(procedure));
        Assert.Equal("DelegateCreation", fragment.Reason);
        Assert.Matches("^[0-9a-f]{64}$", fragment.Fingerprint);
        Assert.Equal(["o", "o"], fragment.Reads.Select(static r => r.SourceName), StringComparer.Ordinal);
        Assert.NotNull(fragment.Threw);
        Assert.Contains(procedure.Blocks, b => b.Terminator is IrBranch branch && branch.Cond == fragment.Threw);
        Assert.Equal(["field.C.F"], fragment.Heap.Select(static h => h.Map), StringComparer.Ordinal);
    }

    [Fact]
    public void AReferenceReadIsFollowedByItsNullShadow()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { bool P(int x) => x > 0; int M(int[] xs, C o) => xs.Count(o.P); }")));

        Assert.Equal<IrType>([new IrSort("C"), new IrBool()], fragment.Reads.Select(static r => r.Type));
    }

    [Fact]
    public void RenamingLocalsAndParametersKeepsTheFingerprintAndAnotherBodyChangesIt()
    {
        static string Fingerprint(string method) => Assert.Single(Opaques(Source(Linq + "class C { " + method + " }"))).Fingerprint!;

        string original = Fingerprint("int? M(int? x, int? k) { int? m = k; return x + m; }");

        Assert.Equal(original, Fingerprint("int? M(int? y, int? limit) { int? bound = limit; return y + bound; }"));
        Assert.NotEqual(original, Fingerprint("int? M(int? x, int? k) { int? m = k; return x - m; }"), StringComparer.Ordinal);
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
    [InlineData("int k = 1; var q = from x in xs where x > k select x; k = 2; return q.Count();")]
    [InlineData("int k = 0; int n = 0; for (int i = 0; i < 3; i++) { n += (from x in xs where x > k select x).Count(); k++; } return n;")]
    [InlineData("int k = 1; System.Action bump = () => k++; var q = from x in xs where x > k select x; bump(); return q.Count();")]
    public void LambdaCapturingALaterWrittenLocalHasNoFingerprint(string body)
    {
        IrProcedure procedure = Source(Linq + "class C { int M(int[] xs) { " + body + " } }");

        IrOpaque query = Assert.Single(Opaques(procedure), static o => string.Equals(o.Reason, "TranslatedQuery", StringComparison.Ordinal));
        Assert.Null(query.Fingerprint);
        Assert.Empty(query.Reads);
    }

    [Fact]
    public void LambdaCapturingALocalWrittenOnlyBeforeItIsFingerprinted()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { int M(int[] xs, int a) { int k = 1; if (a > 0) { k = 2; } var q = from x in xs where x > k select x; return q.Count(); } }")));

        Assert.NotNull(fragment.Fingerprint);
        Assert.Equal(["xs", "xs", "k"], fragment.Reads.Select(static r => r.SourceName), StringComparer.Ordinal);
    }

    [Fact]
    public void RuntimeSensitiveFragmentHasNoFingerprint()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { int M(double[] xs) => (from x in xs select (int)x).Sum(); }")));

        Assert.Equal("TranslatedQuery", fragment.Reason);
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
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { int M(int[] xs) => (from x in xs select ((System.Func<int, int>)(y => { int Twice(int z) => 2 * z; return Twice(y); }))(x)).Sum(); }")));

        Assert.Equal("TranslatedQuery", fragment.Reason);
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
        IrProcedure procedure = Source(Linq + "class C { int M(int[] xs) => (from x in xs select System.Math.Abs(x)).Sum() + (from x in xs select xs.Sum(System.Math.Abs)).Sum(); }");

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
    public void AQueryOverALoopIsOneTranslatedQueryFragment()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { static int M(int[] xs) { int s = 0; foreach (var x in from n in xs where n % 2 == 0 select n) s += x; return s; } }")), static o => string.Equals(o.Reason, "TranslatedQuery", StringComparison.Ordinal));

        Assert.NotNull(fragment.Fingerprint);
        Assert.Equal(["xs"], fragment.Reads.Select(static r => r.SourceName).Distinct(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-026 criterion 2, reduced from Git Extensions' <c>RecentRepoSplitter.SplitRecentRepos</c>: a second <c>from</c>
    /// clause is a lambda whose syntax is the clause, neither an expression nor a statement, which the data-flow analysis of the
    /// graph's functions cast to a statement and threw on. The first case's call of its local function is an opaque no side
    /// shares (ticket P2-127); every other opaque is a fingerprinted fragment.
    /// </summary>
    [Theory]
    [InlineData("int M(List<List<int>> groups, bool top) { var all = new List<int>(); void Add(List<int> into) { into.AddRange(from g in groups from x in g where (x > 0) == top select x); } Add(all); return all.Count(x => (x > 0) == top); }")]
    [InlineData("int M(int[][] groups, int k) => (from g in groups from x in g where x > k select x).Count();")]
    public void ANestedFromClauseLowersWithoutThrowing(string method)
    {
        IrProcedure procedure = Source(Linq + "class C { " + method + " }");

        Assert.All(Opaques(procedure), static o => Assert.Equal(string.Equals(o.Reason, "LocalFunction", StringComparison.Ordinal), o.Fingerprint is null));
    }

    [Fact]
    public void ALambdaCapturingAVariableANestedFromClauseWritesHasNoFingerprint()
    {
        IrProcedure procedure = Source(Linq + "class C { int M(int[] xs, int k) { int t = 0; var q = from x in xs from y in xs.Take(t = k) select y; return xs.Count(x => x > t); } }");

        Assert.Null(Assert.Single(Opaques(procedure), static o => string.Equals(o.Reason, "DelegateCreation", StringComparison.Ordinal)).Fingerprint);
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
