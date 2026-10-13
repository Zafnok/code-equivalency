using CsCheck;

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
/// <para>
/// Ticket P2-171: the call is named by the fingerprint of the bound code it runs, so a member whose local function is the
/// same on both sides is decided by the solver beside a change elsewhere in it, and one whose local functions differ, in
/// their own code, in a function they reach or in the value of a variable they capture, is still never Equivalent.
/// </para>
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

    /// <summary>
    /// Ticket P2-171 criterion 3: both sides declare the same local function and differ in the argument they compute for
    /// it, so the pair is not congruent, and the solver proves it with the two calls as one.
    /// </summary>
    [Theory]
    [InlineData("int L(int v) => v + 1")]
    [InlineData("static int L(int v) => v + 1")]
    [InlineData("int L(int v) => v + k + _x")]
    [InlineData("int L(int v) => v <= 0 ? k : L(v - 1)")]
    [InlineData("int Inner(int w) => w * k; int L(int v) => Inner(v) + Inner(k)")]
    public void AMemberWhoseLocalFunctionIsTheSameIsProvedBesideAChangeElsewhere(string local)
    {
        Result bump = Assert.Single(Compare(Caller(local, "_x + 2"), Caller(local, "2 + _x")));

        Assert.Equal("EQ001", bump.RuleId);
        Assert.NotEqual("congruence", bump.GetProperty<string>("proofMethod"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-171 criterion 4, beyond P2-127's repro: the two sides' calls differ in a function the called one reaches,
    /// or in the value of a variable it captures, which the call reads.
    /// </summary>
    [Theory]
    [InlineData("int Inner(int w) => w + 1; int L(int v) => Inner(v)", "int Inner(int w) => w + 2; int L(int v) => Inner(v)", "a")]
    [InlineData("int L(int v) => v + k", "int L(int v) => v + k", "a + 1")]
    [InlineData("int L(int v) => v + k", "int L(int v) => v + a", "a")]
    public void AMemberWhoseLocalFunctionRunsOtherCodeOrReadsAnotherValueIsNotEquivalent(string legacy, string modern, string captured)
    {
        Result bump = Assert.Single(Compare(Caller(legacy, "_x", "a"), Caller(modern, "_x", captured)));

        Assert.NotEqual("EQ001", bump.RuleId, StringComparer.Ordinal);
    }

    /// <summary>
    /// The property of ticket P2-171: over local functions <c>v op c</c>, with and without a captured variable, called
    /// from members that differ elsewhere, a pair is Equivalent only when the two functions compute the same value, and
    /// is Equivalent when they are the same function.
    /// </summary>
    [Fact]
    public void AChangeInsideALocalFunctionIsNeverEquivalentUnlessTheTwoBodiesAre() =>
        Gen.Select(Body, Body, Gen.Bool).Sample(
            static (legacy, modern, captures) =>
            {
                bool equivalent = PairRuntime.Analyse(Oracle(legacy, captures, "a + 1"), Oracle(modern, captures, "1 + a")).Verdict is Equivalent;
                return equivalent ? ComputeTheSame(legacy, modern) : legacy != modern;
            },
            seed: DifferentialSoundnessTests.Seed,
            iter: Bodies,
            print: static c => Oracle(c.Item1, c.Item3, "a + 1") + Oracle(c.Item2, c.Item3, "1 + a"));

    private const int Bodies = 60;

    /// <summary>A local function body <c>v op c</c> over a few operators and small constants, so that two drawn bodies are often one.</summary>
    private static readonly Gen<(char Op, int Constant)> Body = Gen.Select(Gen.OneOfConst('+', '-', '^'), Gen.Int[-2, 2]);

    /// <summary>Whether <c>v op c</c> is one function of <c>v</c> for both bodies: the same body, a zero on both sides, or <c>v + c</c> beside <c>v - -c</c>.</summary>
    private static bool ComputeTheSame((char Op, int Constant) legacy, (char Op, int Constant) modern) =>
        legacy == modern
        || (legacy.Constant == 0 && modern.Constant == 0)
        || (legacy.Op != '^' && modern.Op != '^' && legacy.Constant == -modern.Constant);

    private static string Oracle((char Op, int Constant) body, bool captures, string argument) =>
        "public static class Oracle { public static int F; public static int M(int a, int b) { int k = b; "
        + string.Create(System.Globalization.CultureInfo.InvariantCulture, $"int L(int v) => (v {body.Op} ({body.Constant})){(captures ? " * k" : string.Empty)}; ")
        + "F = L(" + argument + "); return F; } }\n";

    /// <summary><c>Bump</c>, which declares <paramref name="local"/> and a variable <c>k</c> it may capture, and stores <c>L</c> of <paramref name="argument"/>.</summary>
    private static string Caller(string local, string argument, string captured = "a") =>
        "public sealed class Holder { private int _x; public void Bump(int a) { int k = " + captured + "; " + local + "; int y = " + argument + "; _x = L(y) + k; } }\n";

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
