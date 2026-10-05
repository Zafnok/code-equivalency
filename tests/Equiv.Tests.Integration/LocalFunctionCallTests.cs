using Equiv.Cli;
using Equiv.Core;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Frontend.CSharp.Loading;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-127 end to end: a local function is not a matched procedure, so no pair reads its body, and a member that
/// calls one is never proved Equivalent on the strength of the local function's name. The whole pipeline runs: the real
/// frontend over both compilations, the real backend, and <see cref="CompareCommand.Run"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LocalFunctionCallTests
{
    private static readonly MetadataReference[] References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path))];

    /// <summary>The ticket's repro: the two sides differ only inside the local function <c>Bump</c> calls.</summary>
    [Theory]
    [InlineData("int L(int v) => v + ")]
    [InlineData("static int L(int v) => v + ")]
    public void AMemberWhoseLocalFunctionDiffersIsNotEquivalent(string local)
    {
        Result bump = Assert.Single(Compare(Holder(local + "1"), Holder(local + "2")));

        Assert.Equal("Holder::Bump()", bump.PartialFingerprints["procedureIdentity/v1"]);
        Assert.NotEqual("EQ001", bump.RuleId, StringComparer.Ordinal);
    }

    private static string Holder(string local) =>
        "public sealed class Holder { private int _x; public void Bump() { " + local + "; _x = L(_x); } }\n";

    private static Result[] Compare(string legacySource, string modernSource)
    {
        CSharpCompilation legacy = Compile(legacySource, "legacy.cs");
        CSharpCompilation modern = Compile(modernSource, "modern.cs");
        string legacyPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-127-{Guid.NewGuid():N}.sln");
        string modernPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-127-{Guid.NewGuid():N}.sln");
        File.WriteAllText(legacyPath, string.Empty);
        File.WriteAllText(modernPath, string.Empty);
        try
        {
            CapturingSink sink = new();
            int exitCode = CompareCommand.Run(
                new CompareOptions(legacyPath, modernPath, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false) { Streams = new Streams(TextWriter.Null, TextWriter.Null) },
                [new CSharpFrontend(new StubLoader(legacy, modern), new StableIdentityMatcher())],
                new Z3Backend(),
                sink,
                NullRunLog.Instance);

            Assert.Equal(ExitCodes.Success, exitCode);
            return [.. sink.Log!.Runs[0].Results];
        }
        finally
        {
            File.Delete(legacyPath);
            File.Delete(modernPath);
        }
    }

    private static CSharpCompilation Compile(string source, string path)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "App",
            [CSharpSyntaxTree.ParseText(source, path: path, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    private sealed class StubLoader(Compilation legacy, Compilation modern) : ISolutionLoader
    {
        public Task<LoadedSolution> LoadAsync(string solutionPath, Codebase side, CancellationToken ct) =>
            Task.FromResult(new LoadedSolution(null!, [side == Codebase.Legacy ? legacy : modern], [], []));
    }

    private sealed class CapturingSink : IReportSink
    {
        public SarifLog? Log { get; private set; }

        public void Write(SarifLog log) => Log = log;
    }
}
