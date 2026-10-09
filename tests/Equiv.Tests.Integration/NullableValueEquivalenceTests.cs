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
/// Ticket P2-095 end to end: a <c>T</c> converted to <c>T?</c> is one value however it is spelled, and so is a
/// <c>T?</c> with none, so a null test spelled with <c>(int?)null</c> is the null-conditional access that replaces it,
/// and a value returned where the legacy side returned <c>null</c> is seen.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NullableValueEquivalenceTests
{
    private const string Spelled = "static int? M(string x) => x == null ? (int?)null : x.Length;";

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    /// <summary>Criterion 4's pair, and the other spellings of a value and of none, proved by the solver and not by congruence.</summary>
    [Theory]
    [InlineData(Spelled, "static int? M(string x) => x?.Length;")]
    [InlineData("static int? M(int a, bool b) { if (b) { return new int?(a); } return new int?(); }", "static int? M(int a, bool b) => b ? a : null;")]
    [InlineData("static bool? M(bool a, bool b) { bool? r = default(bool?); if (b) { r = (bool?)a; } return r; }", "static bool? M(bool a, bool b) => b ? a : default(bool?);")]
    public void ANullableValueEqualsItsOtherSpelling(string legacy, string modern)
    {
        Equivalent proved = Assert.IsType<Equivalent>(Verify(legacy, modern));

        Assert.NotEqual(ProofMethod.Congruence, proved.Method);
    }

    /// <summary>
    /// A local's null shadow is exact: a converted value is never null, so its <c>??</c> never takes the fallback, and a
    /// local set to <c>null</c> always does.
    /// </summary>
    [Theory]
    [InlineData("static int M(int a, int b) { int? n = a; return n ?? b; }", "static int M(int a, int b) { int? n = a; return n.GetValueOrDefault(); }")]
    [InlineData("static int M(int a, int b) { int? n = null; return n ?? b; }", "static int M(int a, int b) => b;")]
    [InlineData("static int M(int a, int b) { int? n = default; return n ?? b; }", "static int M(int a, int b) => b;")]
    public void ANullTestOfALocalIsExact(string legacy, string modern) =>
        Assert.IsType<Equivalent>(Verify(legacy, modern));

    /// <summary>Criterion 5: <c>x?.Length ?? 0</c> returned as an <c>int?</c> is <c>0</c> where the legacy side is <c>null</c>.</summary>
    [Fact]
    public void AFallbackValueIsNotTheNullItReplaces()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Spelled, "static int? M(string x) => x?.Length ?? 0;"));

        // The inputs start with `x`, and `null.System.String` is the last: the string is null.
        ImmutableArray<IrValue> inputs = divergent.Counterexample.Inputs.Arguments;
        Assert.Equal(new IrBoolValue(Value: true), Assert.IsType<IrMapValue>(inputs[^1]).Read(inputs[0]));
    }

    /// <summary>A value is not none, and another value is another.</summary>
    [Theory]
    [InlineData("static int? M(int a) => a;", "static int? M(int a) => null;")]
    [InlineData("static int? M(int a) => a;", "static int? M(int a) => a + 1;")]
    [InlineData("static bool? M(bool a) => a;", "static bool? M(bool a) => !a;")]
    [InlineData("static int? M(int a) => new int?(a);", "static int? M(int a) => new int?();")]
    public void AnotherNullableValueDiverges(string legacy, string modern) =>
        Assert.IsType<Divergent>(Verify(legacy, modern));

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower(legacy, isLegacy: true), Lower(modern, isLegacy: false), Options);

    private static IrProcedure Lower(string members, bool isLegacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText($"using System;\nclass C {{ {members} }}", cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, Runtimes.Migration).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
