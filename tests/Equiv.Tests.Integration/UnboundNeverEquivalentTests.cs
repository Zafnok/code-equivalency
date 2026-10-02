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
/// Ticket P2-085 acceptance criterion 3 (ADR 0029 decision 2 as clarified): no verdict is Equivalent for a pair where
/// either body does not bind. Each case is one error kind, in the same source on both sides, so the two bodies are the
/// same text and would be congruent if erroneous code counted as evidence. The whole pipeline runs: the real frontend
/// over both compilations, the real backend, and <see cref="CompareCommand.Run"/>.
/// </summary>
public sealed class UnboundNeverEquivalentTests
{
    private const string Clean = "N.C::Clean(int)";

    private static readonly MetadataReference[] References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path))];

    /// <summary>A binding error in the method's own span: only that method is unbound, and its neighbour is still proved.</summary>
    [Theory]
    [InlineData("CS0246", "", "public int M(int a) { Missing m = null; return a; }")] // type or namespace not found
    [InlineData("CS0234", "", "public int M(int a) { System.Missing.Thing t = null; return a; }")] // not in the namespace
    [InlineData("CS0400", "", "public int M(int a) { global::Missing m = null; return a; }")] // not in the global namespace
    [InlineData("CS0103", "", "public int M(int a) { return a + Undefined(); }")] // name does not exist
    [InlineData("CS1061", "", "public int M(string s) { return s.Missing(); }")] // member does not exist
    [InlineData("CS0246", "", "public int M(Missing m) { return 1; }")] // in the signature
    [InlineData("CS0246", "", "[Missing] public int M(int a) { return a; }")] // in an attribute
    [InlineData("CS0246", "", "public Missing P { get; set; }")] // on the property, outside its accessors
    [InlineData("CS0246", " : Missing", "public C(int a) { }")] // on the base type, outside the constructor
    public void AMethodWithABindingErrorIsUnknownAndItsNeighbourIsStillProved(string id, string bases, string member)
    {
        Result[] results = Compare(id, bases, member);

        Assert.Equal("EQ001", Assert.Single(results, static r => IsClean(r)).RuleId);
        Result[] erroneous = [.. results.Where(static r => !IsClean(r))];
        Assert.NotEmpty(erroneous);
        Assert.All(erroneous, AssertUnbound);
    }

    /// <summary>A syntax error: every method declared in the file is unbound, the one that holds it and its neighbour.</summary>
    [Theory]
    [InlineData("CS1002", "public int M(int a) { return a }")] // ; expected
    [InlineData("CS1513", "public int M(int a) { if (a > 0) { return a; }")] // } expected
    public void EveryMethodInAFileWithASyntaxErrorIsUnknown(string id, string member)
    {
        Result[] results = Compare(id, bases: string.Empty, member);

        Assert.Contains(results, static r => IsClean(r));
        Assert.All(results, AssertUnbound);
    }

    private static void AssertUnbound(Result result)
    {
        Assert.Equal("EQ003", result.RuleId);
        Assert.Equal("unbound", result.GetProperty<string>("unknownReason"));
    }

    private static bool IsClean(Result result) => string.Equals(result.PartialFingerprints["procedureIdentity/v1"], Clean, StringComparison.Ordinal);

    /// <summary>The run's results for one source, holding <paramref name="member"/> and a clean method, on both sides.</summary>
    private static Result[] Compare(string id, string bases, string member)
    {
        string source = $"namespace N {{ public class C{bases} {{\n{member}\npublic int Clean(int a) {{ return a + 1; }}\n}} }}\n";
        CSharpCompilation legacy = Compile(source, "legacy.cs");
        CSharpCompilation modern = Compile(source, "modern.cs");
        Assert.Contains(modern.GetDiagnostics(TestContext.Current.CancellationToken), d => d.Severity == DiagnosticSeverity.Error && string.Equals(d.Id, id, StringComparison.Ordinal));

        string legacyPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-085-{Guid.NewGuid():N}.sln");
        string modernPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-085-{Guid.NewGuid():N}.sln");
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

            // No project is skipped and no pair crashes: an Unknown fails nothing unless --fail-on unknown asks.
            Assert.Equal(ExitCodes.Success, exitCode);
            return [.. sink.Log!.Runs[0].Results];
        }
        finally
        {
            File.Delete(legacyPath);
            File.Delete(modernPath);
        }
    }

    private static CSharpCompilation Compile(string source, string path) => CSharpCompilation.Create(
        "App",
        [CSharpSyntaxTree.ParseText(source, path: path, cancellationToken: TestContext.Current.CancellationToken)],
        References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

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
