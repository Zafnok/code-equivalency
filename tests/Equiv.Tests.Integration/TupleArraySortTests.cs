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
/// Ticket P2-125 end to end: a field whose type is an array of a tuple with element names, written with an array of the
/// same tuple under other names, verifies, rather than Z3 throwing on a sort mismatch between the field's map and the
/// value stored into it; and a generic call whose type argument is a tuple is one uninterpreted function whatever the
/// tuple's element names.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TupleArraySortTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    [Theory]
    [InlineData("(object x, string y)[]")]
    [InlineData("(object, string)[]")]
    public void ElementNamesDoNotSplitTheSort(string parameter) =>
        Assert.IsType<Equivalent>(Verify(
            $"class C {{ static (object First, string Second)[] f; static void M({parameter} a) {{ f = a; }} }}",
            $"class C {{ static (object First, string Second)[] f; static void M({parameter} a) {{ var b = a; f = b; }} }}"));

    /// <summary>The shared sort is not a free pass: storing the other array is not Equivalent.</summary>
    [Fact]
    public void StoringTheOtherArrayIsNotEquivalent() =>
        Assert.IsNotType<Equivalent>(Verify(
            "class C { static (object First, string Second)[] f; static void M((object x, string y)[] a, (object x, string y)[] b) { f = a; } }",
            "class C { static (object First, string Second)[] f; static void M((object x, string y)[] a, (object x, string y)[] b) { f = b; } }"));

    [Fact]
    public void AGenericCallAndItsRenamedTupleTypeArgumentFormAreEquivalent() =>
        Assert.IsType<Equivalent>(Verify(
            "using System.Collections.Generic;\nusing System.Linq;\nclass C { static IEnumerable<(object a, string b)> M() => Enumerable.Empty<(object a, string b)>(); }",
            "using System.Collections.Generic;\nusing System.Linq;\nclass C { static IEnumerable<(object First, string Second)> M() => Enumerable.Empty<(object First, string Second)>(); }"));

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
        return CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, Runtimes.Migration).Body;
    }
}
