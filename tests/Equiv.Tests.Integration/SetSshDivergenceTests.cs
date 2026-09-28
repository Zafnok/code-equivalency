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
/// Ticket P2-037: Git Extensions' <c>GitSshHelpers.SetSsh</c>, standalone. The legacy side tests its argument through the
/// solution's own <c>Strings.IsNullOrEmpty</c> wrapper and the modern side through <c>string.IsNullOrEmpty</c>. The call
/// traces differ at their first event, which is a real observable (ADR 0018), so the pair is Divergent; the legacy run's
/// <c>threw</c> is only the answer the solver chose for the wrapper's <c>threw</c> edge once the traces had split, which no
/// real run need share. That is why M4-007's replay saw both runtimes return.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SetSshDivergenceTests
{
    private const string Legacy = """
        static class Strings { public static bool IsNullOrEmpty(string s) => string.IsNullOrEmpty(s); }
        public static class C
        {
            public static void M(string path)
            {
                if (!Strings.IsNullOrEmpty(path))
                {
                    Environment.SetEnvironmentVariable("GIT_SSH", path, EnvironmentVariableTarget.Process);
                }
            }
        }
        """;

    private const string Modern = """
        public static class C
        {
            public static void M(string path)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    Environment.SetEnvironmentVariable("GIT_SSH", path, EnvironmentVariableTarget.Process);
                }
            }
        }
        """;

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    [Fact]
    public void SetSsh_IsDivergentInTheCallTrace_AndItsOutcomeRestsOnAnAnswerAfterTheSplit()
    {
        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(Lower(Legacy, isLegacy: true), Lower(Modern, isLegacy: false), Options));

        IrRun legacy = divergent.Counterexample.Old;
        IrRun modern = divergent.Counterexample.New;
        Assert.Equal("Strings::IsNullOrEmpty(string)", legacy.Trace[0].Callee.Value);
        Assert.NotEqual(legacy.Trace.FirstOrDefault(), modern.Trace.FirstOrDefault());
        Assert.Equal(new IrThrew("System.Exception"), legacy.Outcome);
        Assert.IsType<IrReturned>(modern.Outcome);
    }

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
