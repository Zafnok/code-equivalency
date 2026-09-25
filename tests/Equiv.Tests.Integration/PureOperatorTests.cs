using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M4-002 end to end (ADR 0025): C# with floating-point, <c>decimal</c> and user-defined operators, lowered to
/// <see cref="IrPure"/> and verified by Z3. Unchanged arithmetic is provable, and exact exception types keep a
/// <c>catch</c> from being silently ignored. These need the frontend together with the solver, which is why they live here.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PureOperatorTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    /// <summary>
    /// Criterion 6: a single <c>ArithmeticException</c> edge would make the <c>catch</c> unreachable in the IR, and the two
    /// methods look identical, a silent false Equivalent. The overflow flag instead routes into the <c>catch</c>, and the
    /// divergence depends on how the solver interprets <c>dec.mul</c>, so it is Unknown(Abstraction).
    /// </summary>
    [Fact]
    public void OverflowCatchOnDecimalIsNotEquivalent()
    {
        Verdict verdict = Verify(
            "class C { static decimal M(decimal a, decimal b) { try { return a * b; } catch (OverflowException) { return 0m; } } }",
            "class C { static decimal M(decimal a, decimal b) => a * b; }");

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Contains(unknown.Abstractions, static a => a.Identity.Value is "dec.mul");
    }

    /// <summary>The other side of exact types: multiplication never divides by zero, so this <c>catch</c> is dead code.</summary>
    [Fact]
    public void ACatchOfAnExceptionTheOperatorCannotRaiseChangesNothing()
    {
        Verdict verdict = Verify(
            "class C { static decimal M(decimal a, decimal b) { try { return a * b; } catch (DivideByZeroException) { return 0m; } } }",
            "class C { static decimal M(decimal a, decimal b) => a * b; }");

        Assert.IsType<Equivalent>(verdict);
    }

    [Theory]
    [InlineData(
        "class L { public decimal P { get; set; } public int Q { get; set; } public decimal D { get; set; } } class C { static decimal M(L l) => l.P * l.Q - l.D; }",
        "class L { public decimal P { get; set; } public int Q { get; set; } public decimal D { get; set; } } class C { static decimal M(L l) { decimal gross = l.P * l.Q; return gross - l.D; } }")]
    [InlineData(
        "class C { static bool M(double a, float b) => a / b < 1.5; }",
        "class C { static bool M(double x, float y) { double ratio = x / y; return ratio < 1.5; } }")]
    [InlineData(
        "class C { static bool M(string a, string b) => a == b; }",
        "class C { static bool M(string a, string b) { bool same = a == b; return same; } }")]
    public void UnchangedArithmeticIsEquivalent(string legacy, string modern)
    {
        Assert.IsType<Equivalent>(Verify(legacy, modern));
    }

    [Fact]
    public void SwappedDoubleOperandsAreUnknownAbstraction()
    {
        Verdict verdict = Verify("class C { static double M(double a, double b) => a + b; }", "class C { static double M(double a, double b) => b + a; }");

        Assert.Equal(UnknownReason.Abstraction, Assert.IsType<Unknown>(verdict).Reason);
    }

    [Fact]
    public void AFloatToIntConversionIsNeverProvedEqual()
    {
        Verdict verdict = Verify("class C { static int M(double a) => (int)a; }", "class C { static int M(double a) => (int)a; }");

        Assert.IsNotType<Equivalent>(verdict);
    }

    /// <summary>Criterion 7: <c>OrderService.LineTotal</c> extracts a local from unchanged <c>decimal</c> arithmetic, and is decided.</summary>
    [Fact]
    public void BusinessLayerLineTotalIsEquivalent()
    {
        MatchResult match = new CSharpFrontend().Analyze(Solution("legacy", "*.sln"), Solution("modern", "*.slnx"), EquivConfig.Default, TestContext.Current.CancellationToken).Match;
        ProcedurePair lineTotal = match.Pairs.Single(static p => p.New.Value.Contains("OrderService::LineTotal(", StringComparison.Ordinal));
        VerificationOptions options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, EquivConfig.Default.CallIdentityRenames);

        Assert.DoesNotContain(lineTotal.OldBody!.Blocks.SelectMany(static b => b.Instructions), static i => i is IrOpaque);
        Assert.IsType<Equivalent>(new Z3Backend().Verify(lineTotal.OldBody!, lineTotal.NewBody!, options));
    }

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower(legacy, isLegacy: true), Lower(modern, isLegacy: false), Options);

    private static IrProcedure Lower(string source, bool isLegacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText("using System;\n" + source, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    private static string Solution(string side, string pattern) =>
        Directory.GetFiles(Path.Combine(SamplesRoot, "business-layer", side), pattern).Single();
}
