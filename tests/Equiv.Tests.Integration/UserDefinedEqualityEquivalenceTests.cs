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
/// Ticket P2-234 criterion 2 end to end: <c>d == null</c> under a user-defined <c>==</c> is the operator the compiler calls,
/// which need not be a null test, so a migration to <c>d is null</c> is not Equivalent; under the predefined reference
/// comparison it is a null test and still proves Equivalent to <c>d is null</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class UserDefinedEqualityEquivalenceTests
{
    /// <summary><c>D</c>'s <c>==</c> answers <c>true</c> for every pair, and its <c>!=</c> <c>false</c>; <c>P</c> has neither.</summary>
    private const string Types = "class D { public static bool operator ==(D a, D b) => true; public static bool operator !=(D a, D b) => false; public override bool Equals(object o) => true; public override int GetHashCode() => 0; } class P { } ";

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    [Theory]
    [InlineData("static bool M(D d) => d == null;", "static bool M(D d) => d is null;")]
    [InlineData("static bool M(D d) => d != null;", "static bool M(D d) => d is not null;")]
    public void AUserDefinedComparisonWithNullIsNotANullPattern(string legacy, string modern) =>
        Assert.IsNotType<Equivalent>(Verify(legacy, modern));

    [Theory]
    [InlineData("static bool M(P p) => p == null;", "static bool M(P p) => p is null;")]
    [InlineData("static bool M(P p) => p != null;", "static bool M(P p) => p is not null;")]
    public void AReferenceComparisonWithNullIsANullPattern(string legacy, string modern)
    {
        Equivalent proved = Assert.IsType<Equivalent>(Verify(legacy, modern));

        Assert.NotEqual(ProofMethod.Congruence, proved.Method);
    }

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower(legacy, isLegacy: true), Lower(modern, isLegacy: false), Options);

    private static IrProcedure Lower(string members, bool isLegacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText($"using System;\n{Types}class C {{ {members} }}", cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, Runtimes.Migration).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
