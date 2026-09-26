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
/// Ticket M4-008 acceptance criterion 2 end to end: an auto-property is its backing field's map, named for the property,
/// so a method that sets and then gets it is proved by Z3 Equivalent to one that writes and reads a field of that name.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AutoPropertyEquivalenceTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    [Theory]
    [InlineData("public int P;", "public int P { get; set; }", "static int M(C c, int v) { c.P = v; return c.P + 1; }")]
    [InlineData("public static int P;", "public static int P { get; set; }", "static int M(int v) { P = v; return P + 1; }")]
    public void AnAutoPropertyIsEquivalentToAFieldOfItsName(string field, string property, string method) =>
        Assert.IsType<Equivalent>(Verify($"class C {{ {field} {method} }}", $"class C {{ {property} {method} }}"));

    /// <summary>The same map is not a free pass: a property that is written a different value is not Equivalent.</summary>
    [Fact]
    public void AnAutoPropertyWrittenADifferentValueIsNotEquivalent() =>
        Assert.IsNotType<Equivalent>(Verify(
            "class C { public int P; static int M(C c, int v) { c.P = v; return c.P; } }",
            "class C { public int P { get; set; } static int M(C c, int v) { c.P = v + 1; return c.P - 1; } }"));

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
}
