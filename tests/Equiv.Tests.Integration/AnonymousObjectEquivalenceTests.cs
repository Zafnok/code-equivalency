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
/// Ticket P2-088 end to end: an anonymous object passed to a call is a closed call of its property values (ADR 0041), so
/// two sides that build the same object from equal values are proved Equivalent, and two that build another object are not.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AnonymousObjectEquivalenceTests
{
    private const string F = "static void F(object o) { } ";

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    [Theory]
    [InlineData("static void M(int a, int b) { F(new { X = a + b, Y = b }); }", "static void M(int a, int b) { int sum = b + a; F(new { X = sum, Y = b }); }")]
    [InlineData("static void M(string s, C c) { F(new { s, Owner = c }); }", "static void M(string s, C c) { var owner = c; F(new { s = s, Owner = owner }); }")]
    public void TheSameObjectFromEqualValuesIsEquivalent(string legacy, string modern) =>
        Assert.IsType<Equivalent>(Verify($"class C {{ {F}{legacy} }}", $"class C {{ {F}{modern} }}"));

    /// <summary>Other values, or the same values under other names, are another object.</summary>
    [Theory]
    [InlineData("static void M(int a, int b) { F(new { X = a, Y = b }); }", "static void M(int a, int b) { F(new { X = b, Y = a }); }")]
    [InlineData("static void M(int a, int b) { F(new { X = a, Y = b }); }", "static void M(int a, int b) { F(new { X = a, Z = b }); }")]
    public void AnotherObjectIsNotEquivalent(string legacy, string modern) =>
        Assert.IsNotType<Equivalent>(Verify($"class C {{ {F}{legacy} }}", $"class C {{ {F}{modern} }}"));

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
