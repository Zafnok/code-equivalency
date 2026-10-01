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
/// Ticket P2-032 end to end: a legacy method without nullable annotations and its modern form with <c>T[]?</c>
/// parameters and result verify, rather than Z3 throwing on a <c>T[]</c> / <c>T[]?</c> sort mismatch; and (ticket P2-042)
/// a generic call and its form with an annotated type argument are one uninterpreted function.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NullableArrayEquivalenceTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    [Theory]
    [InlineData("string")]
    [InlineData("byte")]
    public void AnArrayAndItsNullableFormAreEquivalent(string element) =>
        Assert.IsType<Equivalent>(Verify(
            $"class C {{ static {element}[] M({element}[] a, {element}[] b) => a; }}",
            $"#nullable enable\nclass C {{ static {element}[]? M({element}[]? a, {element}[]? b) => a; }}"));

    /// <summary>The shared sort is not a free pass: returning the other array is not Equivalent.</summary>
    [Fact]
    public void ReturningTheOtherArrayIsNotEquivalent() =>
        Assert.IsNotType<Equivalent>(Verify(
            "class C { static string[] M(string[] a, string[] b) => a; }",
            "#nullable enable\nclass C { static string[]? M(string[]? a, string[]? b) => b; }"));

    /// <summary>Ticket P2-042: <c>Empty&lt;string&gt;()</c> and <c>Empty&lt;string?&gt;()</c> are the same call.</summary>
    [Fact]
    public void AGenericCallAndItsAnnotatedTypeArgumentFormAreEquivalent() =>
        Assert.IsType<Equivalent>(Verify(
            "using System.Collections.Generic;\nusing System.Linq;\nclass C { static IEnumerable<string> M() => Enumerable.Empty<string>(); }",
            "#nullable enable\nusing System.Collections.Generic;\nusing System.Linq;\nclass C { static IEnumerable<string?> M() => Enumerable.Empty<string?>(); }"));

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower(legacy, isLegacy: true), Lower(modern, isLegacy: false), Options);

    private static IrProcedure Lower(string source, bool isLegacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, Runtimes.Migration).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
