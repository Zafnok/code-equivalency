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
/// Ticket P2-061: P2-046's three Preserving seeds on Git Extensions that turned Equivalent into Divergent, standalone.
/// <c>Commute</c> swapped a <c>string</c> concatenation (S177, S186), which is not commutative, and <c>InlineTemporary</c>
/// moved a field read of an assignment's receiver across a property getter call (S150), which an external getter can
/// change. Each seeded shape really differs, so Divergent is right and the operators no longer offer those sites; the
/// sites they still offer verify.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PreservingSeedDivergenceTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    /// <summary>The shape of the WinForms controls and tree nodes those methods touch, compiled as a separate assembly.</summary>
    private static readonly MetadataReference Library = Compile("Library", """
        public sealed class Box { private bool on; public bool Enabled { get => on; set => on = value; } public bool Checked { get => on; set => on = value; } }
        public sealed class Node { public string Name { get; set; } = ""; public string Branch { get; set; } = ""; }
        """, []).ToMetadataReference();

    [Fact]
    public void CommutingAStringConcatenationOfPropertiesIsDivergent() =>
        Assert.IsType<Divergent>(Verify("string M(Node n) { return n.Name + n.Branch; }", "string M(Node n) { return n.Branch + n.Name; }"));

    [Fact]
    public void CommutingAStringConcatenationOfParametersIsDivergent() =>
        Assert.IsType<Divergent>(Verify("string M(string c, string a) { return c + \" \" + a; }", "string M(string c, string a) { return a + c + \" \"; }"));

    [Fact]
    public void ATemporaryBeforeAReceiverFieldReadIsDivergent() =>
        Assert.IsType<Divergent>(Verify("void M() { var t = G.Checked; F.Enabled = t; }", "void M() { F.Enabled = G.Checked; }"));

    [Fact]
    public void CommutingAnIntegerSumVerifies() =>
        Assert.IsType<Equivalent>(Verify("int M(int a, int b) { return a + b; }", "int M(int a, int b) { return b + a; }"));

    [Fact]
    public void ATemporaryForABareTargetVerifies() =>
        Assert.IsType<Equivalent>(Verify("void M() { var t = G.Checked; H = t; }", "void M() { H = G.Checked; }"));

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower(legacy, isLegacy: true), Lower(modern, isLegacy: false), Options);

    private static CSharpCompilation Compile(string name, string source, ImmutableArray<MetadataReference> extra)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [.. References, .. extra],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    private static IrProcedure Lower(string method, bool isLegacy)
    {
        CSharpCompilation compilation = Compile("Snippet", "class C { private Box F = new(); private Box G = new(); private bool H; " + method + " }", [Library]);
        IMethodSymbol symbol = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(symbol, compilation, EquivConfig.Default, isLegacy).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
