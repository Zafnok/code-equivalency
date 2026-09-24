using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Frontend.CSharp.Loading;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M3-007: the frontend keeps the naming rule the product encoding relies on (VERIFICATION-MODEL.md section 2,
/// ADR 0021). A synthesised input is <c>this</c> or has a dot in its name, and a C# parameter never is. These tests need
/// the frontend together with the solver or the MSBuild loader, which is why they live here and not in the frontend's
/// unit tests.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SynthesisedInputNamingTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string> Samples =>
        [.. Directory.GetDirectories(SamplesRoot).Select(Path.GetFileName).OfType<string>().Where(static s => Directory.Exists(Path.Combine(SamplesRoot, s, "legacy"))).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Acceptance criterion 7: the C# argument named <c>@this</c> is shared by position with the renamed side's, and the
    /// receiver by name, so the pair is Equivalent.
    /// </summary>
    [Fact]
    public void AParameterNamedThisIsEquivalentToTheSameParameterRenamed()
    {
        IrProcedure old = Lower("class C { int f; int M(int @this) => f + @this; }");
        IrProcedure @new = Lower("class C { int f; int M(int v) => f + v; }");

        Verdict verdict = new Z3Backend().Verify(old, @new, new VerificationOptions(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []));

        Assert.IsType<Equivalent>(verdict);
    }

    /// <summary>
    /// Acceptance criterion 9: every method of both sides of every sample lowers with its C# parameters first, none of
    /// them synthesised, then its synthesised inputs, all of them synthesised, and every heap map by-ref.
    /// </summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public async Task EveryProcedureOfEverySampleKeepsTheNamingRule(string sample)
    {
        int lowered = 0;
        foreach (string side in (string[])["legacy", "modern"])
        {
            string solution = Directory.GetFiles(Path.Combine(SamplesRoot, sample, side), side is "legacy" ? "*.sln" : "*.slnx").Single();
            LoadedSolution loaded = await new MsBuildSolutionLoader().LoadAsync(solution, TestContext.Current.CancellationToken);
            foreach (Compilation compilation in loaded.Compilations)
            {
                foreach (EnumeratedProcedure procedure in ProcedureEnumerator.Enumerate(compilation))
                {
                    IrProcedure body = IrLowerer.Lower(procedure.Symbol, compilation, RenameMap.Empty, []);
                    int declared = procedure.Symbol.Parameters.Length;
                    string where = $"{sample}/{side} {body.Identity.Value}: {string.Join(", ", body.Parameters.Select(static p => $"{p.Kind} {p.Var.Name}"))}";

                    Assert.True(body.Parameters.Take(declared).All(static p => !IrParameterNames.IsSynthesised(p.Var.Name)), where);
                    Assert.True(body.Parameters.Skip(declared).All(static p => IrParameterNames.IsSynthesised(p.Var.Name)), where);
                    Assert.True(
                        body.Parameters
                            .Where(static p => p.Var.Name.StartsWith("field.", StringComparison.Ordinal) || p.Var.Name.StartsWith("array.", StringComparison.Ordinal))
                            .All(static p => p.Kind == IrParameterKind.Ref),
                        where);
                    lowered++;
                }
            }
        }

        Assert.NotEqual(0, lowered);
    }

    private static IrProcedure Lower(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, []);
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
