using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-025 end to end: a deconstruction of a tuple literal is its stores, left to right after every element is read,
/// so a swap by tuple is proved Equivalent to a swap through a temporary, and a deconstruction that stores in another order is not.
/// Ticket P2-170: a deconstruction of a tuple-typed value is one read of each element of the value, in order.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DeconstructionEquivalenceTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    [Theory]
    [InlineData("static int M(int a, int b) { int t = a; a = b; b = t; return a - b; }", "static int M(int a, int b) { (a, b) = (b, a); return a - b; }")]
    [InlineData("static int M(string s, string t) { string u = s; s = t; t = u; return s == null ? 1 : t == null ? 2 : 3; }", "static int M(string s, string t) { (s, t) = (t, s); return s == null ? 1 : t == null ? 2 : 3; }")]
    [InlineData("int f; static int g; int M(int a) { f = a; g = a + 1; return f - g; }", "int f; static int g; int M(int a) { (f, g, _) = (a, a + 1, a * 2); return f - g; }")]
    public void ADeconstructionIsEquivalentToItsStores(string stores, string deconstruction) =>
        Assert.IsType<Equivalent>(Verify($"class C {{ {stores} }}", $"class C {{ {deconstruction} }}"));

    /// <summary>Reading each element before any store matters: storing as it reads does not swap, and is not Equivalent.</summary>
    [Fact]
    public void ADeconstructionIsNotEquivalentToStoringAsItReads() =>
        Assert.IsNotType<Equivalent>(Verify(
            "class C { static int M(int a, int b) { a = b; b = a; return a - b; } }",
            "class C { static int M(int a, int b) { (a, b) = (b, a); return a - b; } }"));

    /// <summary>
    /// Ticket P2-170 acceptance criterion 3: a deconstruction of a call's tuple is the same IR whether its targets are
    /// declared with <c>var</c> or with their types, as IDE0008 rewrites it, so the pair is Equivalent.
    /// </summary>
    [Theory]
    [InlineData("(string, int)", "var (s, n)", "(string s, int n)", "s == null ? n : n + 1")]
    [InlineData("(int, long)", "var (s, n)", "(int s, long n)", "s - (int)n")]
    public void ADeconstructionWithExplicitTypesIsEquivalentToOneWithVar(string tuple, string var, string typed, string result) =>
        Assert.IsType<Equivalent>(Verify(
            $"class C {{ static {tuple} F(int a) => default; static int M(int a) {{ {var} = F(a); return {result}; }} }}",
            $"class C {{ static {tuple} F(int a) => default; static int M(int a) {{ {typed} = F(a); return {result}; }} }}"));

    /// <summary>Ticket P2-170 acceptance criterion 3: each target takes the element at its own position, so swapping two targets is Divergent.</summary>
    [Fact]
    public void ADeconstructionThatSwapsTwoTargetsIsDivergent() =>
        Assert.IsType<Divergent>(Verify(
            "class C { static (int, int, string) F(int a) => default; static int M(int a) { var (x, y, _) = F(a); return x - y; } }",
            "class C { static (int, int, string) F(int a) => default; static int M(int a) { (int y, int x, _) = F(a); return x - y; } }"));

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
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, Runtimes.Migration).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
