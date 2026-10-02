using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// A lambda, a static method or a method of <c>this</c> converted to a delegate is the pure function
/// <c>delegate:&lt;fingerprint&gt;</c> of the variables it reads (ticket P2-067; ADR 0024 and ADR 0025 clarifications): the
/// conversion runs no code, so it is no trace event, touches no heap and cannot throw. What ADR 0024 does not fingerprint, and
/// a method group whose receiver is evaluated, stay opaque with reason <c>DelegateCreation</c>.
/// </summary>
public sealed class DelegateLoweringTests
{
    private const string Linq = "using System;\nusing System.Linq;\nusing System.Collections.Generic;\n";

    private const string DelegateCreation = "DelegateCreation";

    private static ImmutableArray<IrPure> Delegates(IrProcedure procedure) =>
        [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>().Where(static p => p.Function.StartsWith("delegate:", StringComparison.Ordinal))];

    [Fact]
    public void ALambdaIsAPureFunctionNamedByItsFingerprintOverWhatItReads()
    {
        IrProcedure procedure = Source(Linq + "class C { int F; int M(int[] xs, int k, int unused) { F = 1; return xs.Count(x => x > k + F); } }");

        IrPure lambda = Assert.Single(Delegates(procedure));
        Assert.Empty(Opaques(procedure));
        Assert.Matches("^delegate:[0-9a-f]{64}#0$", lambda.Function);
        Assert.Equal(["k"], lambda.Args.Select(static a => a.SourceName), StringComparer.Ordinal);
        Assert.Equal(new IrSort("System.Func`2"), lambda.Target.Type);
        Assert.Empty(lambda.Throws);
        Assert.False(lambda.RuntimeSensitive);

        // The conversion is no event of its own and no heap pair: the only call is the one the delegate is passed to.
        IrCall count = Assert.Single(Calls(procedure));
        Assert.Contains(lambda.Target, count.Args);
    }

    [Fact]
    public void AReferenceReadIsFollowedByItsNullShadow()
    {
        IrPure lambda = Assert.Single(Delegates(Source(Linq + "class C { int M(int[] xs, string s) => xs.Count(x => x > s.Length); }")));

        Assert.Equal<IrType>([new IrSort("System.String"), new IrBool()], lambda.Args.Select(static a => a.Type));
    }

    [Fact]
    public void RenamingLocalsAndParametersKeepsTheFunctionAndAnotherBodyChangesIt()
    {
        static string Function(string method) => Assert.Single(Delegates(Source(Linq + "class C { " + method + " }"))).Function;

        string original = Function("int M(int[] xs, int k) { int m = k; return xs.Count(x => x > m); }");

        Assert.Equal(original, Function("int M(int[] ys, int limit) { int bound = limit; return ys.Count(y => y > bound); }"));
        Assert.NotEqual(original, Function("int M(int[] xs, int k) { int m = k; return xs.Count(x => x >= m); }"), StringComparer.Ordinal);
    }

    /// <summary>Two lambdas are two methods, so their delegates are never equal: <c>e += a; e -= b</c> removes nothing.</summary>
    [Fact]
    public void TwoLambdaSitesWithOneBodyAreTwoFunctions()
    {
        ImmutableArray<IrPure> lambdas = Delegates(Source(Linq + "class C { int M(int[] xs) => xs.Count(x => x > 0) + xs.Count(x => x > 0); }"));

        Assert.Equal(2, lambdas.Length);
        Assert.Equal(lambdas[0].Function[..^2], lambdas[1].Function[..^2]);
        Assert.EndsWith("#0", lambdas[0].Function, StringComparison.Ordinal);
        Assert.EndsWith("#1", lambdas[1].Function, StringComparison.Ordinal);
    }

    /// <summary>A <c>finally</c> is copied onto each path that runs it; its lambda is still one site.</summary>
    [Fact]
    public void ALambdaSiteCopiedOntoTwoPathsIsOneFunction()
    {
        ImmutableArray<IrPure> lambdas = Delegates(Source(Linq + "class C { static void G() { } int M(int[] xs) { int n = 0; try { G(); } finally { n = xs.Count(x => x > 0); } return n; } }"));

        Assert.True(lambdas.Length > 1);
        Assert.EndsWith("#0", Assert.Single(lambdas.Select(static l => l.Function).Distinct(StringComparer.Ordinal)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two conversions of one method group with one receiver are equal delegates: <c>e += P; e -= P</c> removes it. The
    /// function is named by ADR 0024's fingerprint, so the two must also be written alike.
    /// </summary>
    [Theory]
    [InlineData("static bool P(int x) => x > 0; int M(int[] xs) => xs.Count(P) + xs.Count(P);")]
    [InlineData("bool P(int x) => x > 0; int M(int[] xs) => xs.Count(P) + xs.Count(P);")]
    [InlineData("bool P(int x) => x > 0; int M(int[] xs) => xs.Count(new Func<int, bool>(this.P)) + xs.Count(new Func<int, bool>(this.P));")]
    public void TwoConversionsOfOneMethodGroupAreOneFunction(string members)
    {
        IrProcedure procedure = Source(Linq + "class C { " + members + " }");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(2, Delegates(procedure).Length);
        Assert.Matches("^delegate:[0-9a-f]{64}$", Assert.Single(Delegates(procedure).Select(static d => d.Function).Distinct(StringComparer.Ordinal)));
        Assert.All(Delegates(procedure), static d => Assert.Empty(d.Args));
    }

    [Fact]
    public void AStaticAndAnInstanceMethodGroupAreDifferentFunctions()
    {
        ImmutableArray<IrPure> groups = Delegates(Source(Linq + "class C { static bool P(int x) => x > 0; bool Q(int x) => x > 0; int M(int[] xs) => xs.Count(P) + xs.Count(Q); }"));

        Assert.Equal(2, groups.Select(static g => g.Function).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Converting <c>o.P</c> evaluates <c>o</c> and throws when it is null, and so does wrapping a delegate <c>f</c> in
    /// another, so neither is a pure function: each stays a shared fragment.
    /// </summary>
    [Theory]
    [InlineData("bool P(int x) => x > 0; int M(int[] xs, C o) => xs.Count(o.P);")]
    [InlineData("bool P(int x) => x > 0; static C Other() => new C(); int M(int[] xs) => xs.Count(Other().P);")]
    [InlineData("int M(int[] xs, Func<int, bool> f) => xs.Count(new Func<int, bool>(f));")]
    public void AMethodGroupWhoseReceiverIsEvaluatedStaysOpaque(string members)
    {
        IrProcedure procedure = Source(Linq + "class C { " + members + " }");

        Assert.Empty(Delegates(procedure));
        Assert.Contains(Opaques(procedure), static o => string.Equals(o.Reason, DelegateCreation, StringComparison.Ordinal));
    }

    [Fact]
    public void AMethodGroupOfALocalReceiverIsStillASharedFragment()
    {
        IrOpaque fragment = Assert.Single(Opaques(Source(Linq + "class C { bool P(int x) => x > 0; int M(int[] xs, C o) => xs.Count(o.P); }")));

        Assert.Equal(DelegateCreation, fragment.Reason);
        Assert.NotNull(fragment.Fingerprint);
        Assert.NotNull(fragment.Threw);
    }

    /// <summary>A lambda's body is in its fingerprint whatever it holds: a call, or a local function it declares.</summary>
    [Theory]
    [InlineData("int M(int[] xs) => xs.Sum(x => Math.Abs(x)) + xs.Sum(Math.Abs);", 2)]
    [InlineData("int M(int[] xs) => xs.Sum(x => { int Twice(int y) => 2 * y; return Twice(x); });", 1)]
    public void ALambdaCallingAMethodOrDeclaringALocalFunctionIsPure(string members, int delegates)
    {
        IrProcedure procedure = Source(Linq + "class C { " + members + " }");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(delegates, Delegates(procedure).Length);
    }

    /// <summary>What ADR 0024 gives no fingerprint is not a function of its reads, as a delegate any more than as a fragment.</summary>
    [Theory]
    [InlineData("class C { int M(double[] xs) => xs.Sum(x => (int)x); }")]
    [InlineData("class C { int M(int[] xs) { int t = 0; xs.Count(x => (t = x) > 0); return t; } }")]
    [InlineData("class C { int M(int[] xs) { int Twice(int x) => 2 * x; return xs.Sum(Twice); } }")]
    [InlineData("class C(int p) { int M(int[] xs) => xs.Count(x => x > p); }")]
    [InlineData("struct C { bool P(int x) => x > 0; int M(int[] xs) => xs.Count(P); }")]
    public void AConversionWithNoFingerprintStaysOpaque(string source)
    {
        IrProcedure procedure = Source(Linq + source);

        Assert.Empty(Delegates(procedure));
        IrOpaque opaque = Assert.Single(Opaques(procedure), static o => string.Equals(o.Reason, DelegateCreation, StringComparison.Ordinal));
        Assert.Null(opaque.Fingerprint);
    }

    [Theory]
    [InlineData("int k = 1; var q = xs.Where(x => x > k); k = 2; return q.Count();")]
    [InlineData("int k = 0; int n = 0; for (int i = 0; i < 3; i++) { n += xs.Count(x => x > k); k++; } return n;")]
    public void ALambdaCapturingALocalStoredAfterItIsOpaqueAgain(string body)
    {
        IrProcedure procedure = Source(Linq + "class C { int M(int[] xs) { " + body + " } }");

        Assert.Empty(Delegates(procedure));
        IrOpaque opaque = Assert.Single(Opaques(procedure), static o => string.Equals(o.Reason, DelegateCreation, StringComparison.Ordinal));
        Assert.Null(opaque.Fingerprint);
        Assert.Empty(opaque.Reads);
        Assert.Null(opaque.Threw);
        Assert.Equal(new IrSort("System.Func`2"), opaque.Target!.Type);
    }

    [Fact]
    public void ALambdaCapturingALocalWrittenOnlyBeforeItIsPure()
    {
        IrProcedure procedure = Source(Linq + "class C { int M(int[] xs, int a) { int k = 1; if (a > 0) { k = 2; } var q = xs.Where(x => x > k); return q.Count(); } }");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(["k"], Assert.Single(Delegates(procedure)).Args.Select(static r => r.SourceName), StringComparer.Ordinal);
    }

    /// <summary>A delegate creation never yields null, pure or opaque: the null test before invoking it is of the constant false.</summary>
    [Theory]
    [InlineData("int M(int a) { Func<int, int> f = x => x + 1; return f(a); }")]
    [InlineData("int M(int a) { int k = a; Func<int, int> f = x => x + k; k = 2; return f(a); }")]
    public void ADelegateIsNeverNull(string members)
    {
        IrProcedure procedure = Source(Linq + "class C { " + members + " }");

        IrBlockId thrown = Assert.Single(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" }).Id;
        IrBranch test = Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrBranch>(), b => b.Then == thrown);
        IrConst isNull = Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>(), c => c.Target == test.Cond);
        Assert.Equal(new IrBoolValue(Value: false), isNull.Value);
    }
}
