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
/// Ticket P2-103 end to end: <c>T t = x as T; if (t != null)</c> is <c>if (x is T t)</c>, the rewrite IDE0019 makes,
/// because <c>x as T</c> is null exactly when <c>x</c> is null or its type test fails.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TypeTestEquivalenceTests
{
    private const string Types = "class Base { } class Derived : Base { public string Name; public void Go() { } } class Other : Base { public string Name; } ";

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    /// <summary>Criterion 1, in the shapes the five false EQ002 of <c>powershell-19687</c> have, proved by the solver and not by congruence.</summary>
    [Theory]
    [InlineData(
        "static bool M(Base b) { Derived d = b as Derived; if (d != null) { return d.Name != null; } return false; }",
        "static bool M(Base b) { if (b is Derived d) { return d.Name != null; } return false; }")]
    [InlineData(
        "static void M(object sender, Base b) { Derived d = b as Derived; if (d != null) { d.Go(); } }",
        "static void M(object sender, Base b) { if (b is Derived d) { d.Go(); } }")]
    [InlineData(
        "static string M(object o) { string s = o as string; if (s != null) { return s; } return \"\"; }",
        "static string M(object o) { if (o is string s) { return s; } return \"\"; }")]
    [InlineData(
        "static string M(object o) { string s = o as string; if (s == null) { return \"\"; } return s.Trim(); }",
        "static string M(object o) { if (o is string s) { return s.Trim(); } return \"\"; }")]
    [InlineData(
        "static string M(Base b, Base c) { Derived d = b as Derived; if (d != null) { return d.Name; } Derived e = c as Derived; if (e != null) { return e.Name; } return null; }",
        "static string M(Base b, Base c) { if (b is Derived d) { return d.Name; } if (c is Derived e) { return e.Name; } return null; }")]
    [InlineData(
        "Base field; string M() { Derived d = field as Derived; if (d != null) { return d.Name; } return null; }",
        "Base field; string M() { if (field is Derived d) { return d.Name; } return null; }")]
    [InlineData(
        "static bool M(Base b) { Derived d = b as Derived; return d != null; }",
        "static bool M(Base b) => b is Derived;")]
    public void AsPlusNullCheckEqualsAnIsPattern(string legacy, string modern)
    {
        Equivalent proved = Assert.IsType<Equivalent>(Verify(Types + legacy, Types + modern));

        Assert.NotEqual(ProofMethod.Congruence, proved.Method);
    }

    /// <summary>Criterion 2: a test of another type is another predicate, so the two sides may take different branches.</summary>
    [Theory]
    [InlineData(
        "static bool M(Base b) { Derived d = b as Derived; if (d != null) { return true; } return false; }",
        "static bool M(Base b) { if (b is Other d) { return true; } return false; }")]
    [InlineData(
        "static string M(Base b) { Derived d = b as Derived; if (d != null) { return d.Name; } return null; }",
        "static string M(Base b) { if (b is Other d) { return d.Name; } return null; }")]
    public void AsAndIsOnDifferentTypesStayDifferent(string legacy, string modern) =>
        Assert.IsNotType<Equivalent>(Verify(Types + legacy, Types + modern));

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
