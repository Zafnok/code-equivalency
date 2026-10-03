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
/// Ticket P2-099 end to end: a collection expression is the <c>new</c> and initializer it replaces, so the solver proves
/// the two Equivalent, and it keeps the order of its elements, so two that swap elements with side effects are not.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CollectionExpressionEquivalenceTests
{
    private const string Helpers = "static int A() => 1; static int B() => 2; ";

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    /// <summary>Criterion 2: each of the four shapes, proved by the solver and not by congruence.</summary>
    [Theory]
    [InlineData("static int[] M(int a, int b) => new int[] { a, b };", "static int[] M(int a, int b) => [a, b];")]
    [InlineData("static int M(int a, int b) { var r = new[] { a, b }; return r[1] + r.Length; }", "static int M(int a, int b) { int[] r = [a, b]; return r[1] + r.Length; }")]
    [InlineData("static List<int> M() => new List<int>();", "static List<int> M() => [];")]
    [InlineData("static List<string> M() { List<string> l = new(); return l; }", "static List<string> M() { List<string> l = []; return l; }")]
    [InlineData("static List<int> M(int a, int b) => new List<int> { a, b };", "static List<int> M(int a, int b) => [a, b];")]
    [InlineData("static int M(int a) { var l = new List<int> { A(), a }; return l.Count; }", "static int M(int a) { List<int> l = [A(), a]; return l.Count; }")]
    [InlineData("static int[] M() => Array.Empty<int>();", "static int[] M() => [];")]
    [InlineData("static IEnumerable<string> M() => Array.Empty<string>();", "static IEnumerable<string> M() => [];")]
    [InlineData("static IReadOnlyList<int> M(int a) => new[] { a, A() };", "static IReadOnlyList<int> M(int a) => [a, A()];")]
    public void CollectionExpressionEqualsItsInitializer(string legacy, string modern)
    {
        Equivalent proved = Assert.IsType<Equivalent>(Verify(legacy, modern));

        Assert.NotEqual(ProofMethod.Congruence, proved.Method);
    }

    /// <summary>Criterion 3: the elements are evaluated in order, so swapping two with side effects is seen.</summary>
    [Theory]
    [InlineData("static int[] M() => new int[] { A(), B() };", "static int[] M() => [B(), A()];")]
    [InlineData("static List<int> M() => new List<int> { A(), B() };", "static List<int> M() => [B(), A()];")]
    [InlineData("static IEnumerable<int> M() => new[] { A(), B() };", "static IEnumerable<int> M() => [B(), A()];")]
    public void SwappedElementsWithSideEffectsDiverge(string legacy, string modern) =>
        Assert.IsType<Divergent>(Verify(legacy, modern));

    /// <summary>Other elements, or one more, are another collection.</summary>
    [Theory]
    [InlineData("static int[] M(int a, int b) => new int[] { a, b };", "static int[] M(int a, int b) => [b, a];")]
    [InlineData("static int[] M(int a) => new int[] { a };", "static int[] M(int a) => [a, a];")]
    [InlineData("static List<int> M(int a) => new List<int> { a };", "static List<int> M(int a) => [a, a];")]
    public void AnotherCollectionIsNotEquivalent(string legacy, string modern) =>
        Assert.IsNotType<Equivalent>(Verify(legacy, modern));

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower(legacy, isLegacy: true), Lower(modern, isLegacy: false), Options);

    private static IrProcedure Lower(string members, bool isLegacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText($"using System;\nusing System.Collections.Generic;\nclass C {{ {Helpers}{members} }}", cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, Runtimes.Migration).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
