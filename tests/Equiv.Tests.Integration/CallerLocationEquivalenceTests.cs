using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-098 end to end (ADR 0046): the file path and line number the compiler supplies for a caller-information
/// parameter are one input both sides share, so the same body in two directories or on another line is proved Equivalent,
/// while a path or line the source writes out is compared like any other value.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CallerLocationEquivalenceTests
{
    private const string Members =
        "static void File(string text, [System.Runtime.CompilerServices.CallerFilePath] string file = \"\") { } "
        + "static void Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { } ";

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    [Theory]
    [InlineData("static void M(int a) { File(\"t\"); Line(); }", "static void M(int a) { File(\"t\"); Line(); }")]
    [InlineData("static void M(int a) { File(\"t\"); Line(); }", "static void M(int a)\n{\n    var text = \"t\";\n    File(text);\n\n    Line();\n}")]
    public void TheSameCallsInTwoDirectoriesAreEquivalent(string legacy, string modern) =>
        Assert.IsType<Equivalent>(Verify(legacy, modern));

    /// <summary>Acceptance criterion 3: a caller that passes a different explicit string for the parameter stays Divergent.</summary>
    [Theory]
    [InlineData("static void M(int a) { File(\"t\", \"a.cs\"); }", "static void M(int a) { File(\"t\", \"b.cs\"); }")]
    [InlineData("static void M(int a) { File(\"t\"); }", "static void M(int a) { File(\"t\", \"b.cs\"); }")]
    [InlineData("static void M(int a) { Line(); }", "static void M(int a) { Line(1); }")]
    public void AnExplicitDifferentPathArgumentStaysDivergent(string legacy, string modern) =>
        Assert.IsType<Divergent>(Verify(legacy, modern));

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower(legacy, "legacy", isLegacy: true), Lower(modern, "modern", isLegacy: false), Options);

    private static IrProcedure Lower(string member, string directory, bool isLegacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [
                CSharpSyntaxTree.ParseText(
                    SourceText.From($"class C {{ {Members}{member} }}"),
                    path: Path.Combine(Path.GetTempPath(), directory, "C.cs"),
                    cancellationToken: TestContext.Current.CancellationToken),
            ],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, Runtimes.Migration).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
