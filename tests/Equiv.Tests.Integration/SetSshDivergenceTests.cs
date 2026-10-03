using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-037: Git Extensions' <c>GitSshHelpers.SetSsh</c>, standalone. The legacy side tests its argument through the
/// solution's own <c>Strings.IsNullOrEmpty</c> wrapper and the modern side through <c>string.IsNullOrEmpty</c>. The wrapper
/// is a forwarder, so its call is the call to its target and the two bodies are one (ADR 0043; ticket P2-068). Before
/// that, and still when the wrapper is kept as a callee because its two sides do not agree, the call traces differ at
/// their first event, which is a real observable (ADR 0018), so the pair is Divergent; the legacy run's <c>threw</c> is
/// only the answer the solver chose for the wrapper's <c>threw</c> edge once the traces had split, which no real run need
/// share. That is why M4-007's replay saw both runtimes return.
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

    private const string Wrapper = "Strings::IsNullOrEmpty(string)";

    /// <summary>
    /// Ticket P2-068: the wrapper forwards to <c>string.IsNullOrEmpty</c>, so both sides lower to the same body. On one
    /// runtime the pair is Equivalent. Across .NET Framework 4.8 and .NET 10 it still diverges, and only through
    /// <c>Environment.SetEnvironmentVariable</c>, which the runtime-changes table names: the first events of the two traces,
    /// the test of the argument, are now equal.
    /// </summary>
    [Fact]
    public void SetSsh_LowersToOneBodyOnceTheWrapperIsResolved()
    {
        Assert.Equal(IrText.Dump(Lower(Modern, isLegacy: false, new CallSites())), IrText.Dump(Lower(Legacy, isLegacy: true, new CallSites())));
        Assert.IsType<Equivalent>(new Z3Backend().Verify(
            Lower(Legacy, isLegacy: true, new CallSites(), Runtimes.SameRuntime), Lower(Modern, isLegacy: false, new CallSites(), Runtimes.SameRuntime), Options));

        Divergent migration = Assert.IsType<Divergent>(new Z3Backend().Verify(Lower(Legacy, isLegacy: true, new CallSites()), Lower(Modern, isLegacy: false, new CallSites()), Options));

        Assert.Equal("System.String::IsNullOrEmpty(string)", migration.Counterexample.Old.Trace[0].Callee.Value);
        Assert.Equal(migration.Counterexample.Old.Trace[0], migration.Counterexample.New.Trace[0]);
    }

    [Fact]
    public void SetSsh_IsDivergentInTheCallTrace_AndItsOutcomeRestsOnAnAnswerAfterTheSplit()
    {
        Divergent divergent = Assert.IsType<Divergent>(
            new Z3Backend().Verify(Lower(Legacy, isLegacy: true, new CallSites { KeptForwarders = [Wrapper] }), Lower(Modern, isLegacy: false, new CallSites()), Options));

        IrRun legacy = divergent.Counterexample.Old;
        IrRun modern = divergent.Counterexample.New;
        Assert.Equal(Wrapper, legacy.Trace[0].Callee.Value);
        Assert.NotEqual(legacy.Trace.FirstOrDefault(), modern.Trace.FirstOrDefault());
        Assert.Equal(new IrThrew("System.Exception"), legacy.Outcome);
        Assert.IsType<IrReturned>(modern.Outcome);
    }

    private static IrProcedure Lower(string source, bool isLegacy, CallSites sites, SideRuntime? runtime = null)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText("using System;\n" + source, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, runtime ?? Runtimes.Migration, sites).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
