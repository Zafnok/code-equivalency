using System.Collections.Immutable;
using System.CommandLine;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Cli.Tests;

/// <summary>
/// <see cref="CompareCommand.Run"/> against fakes: routing (ARCHITECTURE.md's router bullet),
/// dry-run, the pipeline that turns a <see cref="MatchResult"/> into a SARIF log, and the exit
/// codes VERIFICATION-MODEL.md section 6 documents. Every test in this class either avoids the
/// console entirely or redirects it and restores it in a `finally`; xUnit runs the [Fact]s in one
/// class sequentially, so the ones that do redirect it (dry-run, config warnings) never race
/// each other. The "Console" collection also serializes this class against
/// <see cref="ProgramTests"/>, which redirects the console too.
/// </summary>
[Collection("Console")]
public sealed class CompareCommandTests
{
    private static readonly ProcedureIdentity PairIdentity = new("T::Pair()");
    private static readonly ImmutableDictionary<string, Verdict> NoVerdicts = [];

    [Fact]
    public void Create_RejectsNullFrontends()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Create(null!, new FakeBackend(NoVerdicts)));
    }

    [Fact]
    public void Create_RejectsNullBackend()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Create([], null!));
    }

    [Fact]
    public void Create_RequiresLegacyAndModern()
    {
        ParseResult parseResult = CompareCommand.Create([], new FakeBackend(NoVerdicts)).Parse([]);

        Assert.Contains(parseResult.Errors, e => e.Message.Contains("--legacy", StringComparison.Ordinal));
        Assert.Contains(parseResult.Errors, e => e.Message.Contains("--modern", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_DefaultsOutAndFailOn()
    {
        ParseResult parseResult = CompareCommand.Create([], new FakeBackend(NoVerdicts)).Parse(["--legacy", "a.sln", "--modern", "b.sln"]);

        Assert.Empty(parseResult.Errors);
        Assert.Equal("equiv.sarif", parseResult.GetValue<string>("--out"));
        Assert.Equal("divergent", parseResult.GetValue<string>("--fail-on"));
    }

    [Theory]
    [InlineData("divergent", true)]
    [InlineData("unknown", true)]
    [InlineData("never", false)]
    public void Create_FailOnAcceptsOnlyDivergentOrUnknown(string failOn, bool accepted)
    {
        ParseResult parseResult = CompareCommand.Create([], new FakeBackend(NoVerdicts)).Parse(["--legacy", "a.sln", "--modern", "b.sln", "--fail-on", failOn]);

        Assert.Equal(accepted, parseResult.Errors.Count == 0);
    }

    [Fact]
    public void Run_RejectsNullFrontends()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(
            "a.sln", "b.sln", "out.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            null!, new FakeBackend(NoVerdicts), new InMemoryReportSink()));
    }

    [Fact]
    public void Run_RejectsNullBackend()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(
            "a.sln", "b.sln", "out.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [], null!, new InMemoryReportSink()));
    }

    [Fact]
    public void Run_RejectsNullSink()
    {
        Assert.Throws<ArgumentNullException>(() => CompareCommand.Run(
            "a.sln", "b.sln", "out.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [], new FakeBackend(NoVerdicts), null!));
    }

    [Fact]
    public void Router_PicksFrontendSupportingBothPaths()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend legacyOnly = new("legacy-only", path => string.Equals(path, legacy.Path, StringComparison.Ordinal));
        FakeFrontend both = new("both", _ => true);

        string output = CaptureStdOut(() =>
        {
            int exitCode = CompareCommand.Run(
                legacy.Path, modern.Path, "out.sarif", baselinePath: null, configPath: null, "divergent", dryRun: true,
                [legacyOnly, both], new FakeBackend(NoVerdicts), new InMemoryReportSink());
            Assert.Equal(ExitCodes.Success, exitCode);
        });

        Assert.Equal($"route: both legacy={legacy.Path} modern={modern.Path} out=out.sarif{Environment.NewLine}", output, StringComparer.Ordinal);
    }

    [Fact]
    public void Router_RejectsWhenNoFrontend()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            legacy.Path, modern.Path, "out.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal($"error: no frontend supports both legacy={legacy.Path} and modern={modern.Path}{Environment.NewLine}", errorOutput, StringComparer.Ordinal);
    }

    [Fact]
    public void Router_RejectsWhenFrontendsDiffer()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend legacyOnly = new("legacy-only", path => string.Equals(path, legacy.Path, StringComparison.Ordinal));
        FakeFrontend modernOnly = new("modern-only", path => string.Equals(path, modern.Path, StringComparison.Ordinal));

        int exitCode = CompareCommand.Run(
            legacy.Path, modern.Path, "out.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [legacyOnly, modernOnly], new FakeBackend(NoVerdicts), new InMemoryReportSink());

        Assert.Equal(ExitCodes.UsageError, exitCode);
    }

    [Fact]
    public void Compare_MissingFileExits3()
    {
        string legacy = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sln");
        string modern = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sln");
        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            legacy, modern, "out.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal($"error: file not found (legacy={legacy}, modern={modern}){Environment.NewLine}", errorOutput, StringComparer.Ordinal);
    }

    [Fact]
    public void Compare_MissingModernOnlyExits3()
    {
        using TempFile legacy = new();
        string modern = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sln");
        FakeFrontend frontend = new("csharp", _ => true);

        int exitCode = CompareCommand.Run(
            legacy.Path, modern, "out.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink());

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal(0, frontend.AnalyzeCallCount);
    }

    [Fact]
    public void Compare_DryRunPrintsRouteAndExits0()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true);

        string output = CaptureStdOut(() =>
        {
            int exitCode = CompareCommand.Run(
                legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath: null, "divergent", dryRun: true,
                [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink());
            Assert.Equal(ExitCodes.Success, exitCode);
        });

        Assert.Equal($"route: csharp legacy={legacy.Path} modern={modern.Path} out=equiv.sarif{Environment.NewLine}", output, StringComparer.Ordinal);
        Assert.Equal(0, frontend.AnalyzeCallCount);
    }

    [Fact]
    public async Task Compare_WritesSarifAndExits0WhenAllEquivalent()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity pairA = new("T::A()");
        ProcedureIdentity pairB = new("T::B()");
        ProcedureIdentity added = new("T::Added()");
        ProcedureIdentity removed = new("T::Removed()");
        MatchResult matchResult = new(
            [Pair(pairA), Pair(pairB)],
            [added],
            [removed],
            []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [pairA.Value] = new Equivalent(),
            [pairB.Value] = new Equivalent(),
        });
        InMemoryReportSink sink = new();

        int exitCode = CompareCommand.Run(
            legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [frontend], backend, sink);

        Assert.Equal(ExitCodes.Success, exitCode);
        await VerifyJson(Serialize(sink.Log!));
    }

    [Fact]
    public void Compare_Exits1OnDivergent()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [PairIdentity.Value] = new Divergent(Counterexample()),
        });

        int exitCode = CompareCommand.Run(
            legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [frontend], backend, new InMemoryReportSink());

        Assert.Equal(ExitCodes.Divergent, exitCode);
    }

    [Fact]
    public void Compare_Exits2OnUnknownWhenFailOnUnknown()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [PairIdentity.Value] = new Unknown(UnknownReason.Timeout, "gave up"),
        });

        int exitCode = CompareCommand.Run(
            legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath: null, "unknown", dryRun: false,
            [frontend], backend, new InMemoryReportSink());

        Assert.Equal(ExitCodes.UnknownPresent, exitCode);
    }

    [Fact]
    public void Compare_Exits0OnUnknownByDefault()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
        FakeFrontend frontend = new("csharp", _ => true, matchResult);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [PairIdentity.Value] = new Unknown(UnknownReason.Timeout, "gave up"),
        });

        int exitCode = CompareCommand.Run(
            legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [frontend], backend, new InMemoryReportSink());

        Assert.Equal(ExitCodes.Success, exitCode);
    }

    [Fact]
    public void Compare_PairWithoutBodyIsAFrontendBug()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedurePair lowered = Pair(PairIdentity);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent() });

        foreach (ProcedurePair pair in new[] { lowered with { OldBody = null }, lowered with { NewBody = null } })
        {
            FakeFrontend frontend = new("csharp", _ => true, new MatchResult([pair], [], [], []));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CompareCommand.Run(
                legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
                [frontend], backend, new InMemoryReportSink()));

            Assert.Contains(PairIdentity.Value, exception.Message, StringComparison.Ordinal);
        }

        Assert.Empty(backend.Calls);
    }

    [Fact]
    public void Compare_BackendFailureIsRethrownNamingThePair()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));

        // No canned verdict for the pair, so the fake backend throws KeyNotFoundException.
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CompareCommand.Run(
            legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.StartsWith($"Verifying {PairIdentity.Value} against {PairIdentity.Value} failed: ", exception.Message, StringComparison.Ordinal);
        Assert.IsType<KeyNotFoundException>(exception.InnerException);
    }

    [Fact]
    public void Compare_Exits4OnFrontendLoadException()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FrontendLoadException exception = new(legacy.Path, "workspace failed to open");
        FakeFrontend frontend = new("csharp", _ => true, throwOnAnalyze: exception);

        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath: null, "divergent", dryRun: false,
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.LoadFailure, exitCode);
        Assert.Equal(exception.Message + Environment.NewLine, errorOutput, StringComparer.Ordinal);
    }

    [Fact]
    public void Compare_BaselineSuppressesUnchangedDivergent()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string baselinePath = Path.GetTempFileName();
        try
        {
            Divergent divergent = new(Counterexample());
            SarifLog baseline = SarifReportWriter.Write([new VerificationResult(PairIdentity, divergent)]);
            baseline.Save(baselinePath);

            MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
            FakeFrontend frontend = new("csharp", _ => true, matchResult);
            FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = divergent });
            InMemoryReportSink sink = new();

            int exitCode = CompareCommand.Run(
                legacy.Path, modern.Path, "equiv.sarif", baselinePath, configPath: null, "divergent", dryRun: false,
                [frontend], backend, sink);

            Assert.Equal(ExitCodes.Success, exitCode);
            Result result = Assert.Single(sink.Log!.Runs[0].Results);
            Assert.Equal(BaselineState.Unchanged, result.BaselineState);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void Compare_BaselineFixedDivergenceExits0()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string baselinePath = Path.GetTempFileName();
        try
        {
            SarifLog baseline = SarifReportWriter.Write([new VerificationResult(PairIdentity, new Divergent(Counterexample()))]);
            baseline.Save(baselinePath);

            // A rule-id change (Divergent -> Equivalent) for an identity already in the baseline is
            // always `new` per M1-004's BaselineComputer, but it must not exit 1: only a *new*
            // Divergent counts, and this one is now Equivalent.
            MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
            FakeFrontend frontend = new("csharp", _ => true, matchResult);
            FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent() });

            int exitCode = CompareCommand.Run(
                legacy.Path, modern.Path, "equiv.sarif", baselinePath, configPath: null, "divergent", dryRun: false,
                [frontend], backend, new InMemoryReportSink());

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void Compare_UsesConfigBoundAndTimeout()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string configPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(configPath, """{ "bound": 7, "timeoutMs": 12000, "callIdentityRenames": { "Old::M": "New::M" } }""");
            MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
            FakeFrontend frontend = new("csharp", _ => true, matchResult);
            FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent() });

            int exitCode = CompareCommand.Run(
                legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath, "divergent", dryRun: false,
                [frontend], backend, new InMemoryReportSink());

            Assert.Equal(ExitCodes.Success, exitCode);
            VerificationOptions options = Assert.Single(backend.Calls);
            Assert.Equal(7, options.Bound);
            Assert.Equal(12000, options.TimeoutMs);
            Assert.Equal("New::M", options.CallIdentityMap["Old::M"]);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Compare_MissingConfigFileExits3()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string configPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        FakeFrontend frontend = new("csharp", _ => true);

        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath, "divergent", dryRun: false,
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal($"error: file not found (config={configPath}){Environment.NewLine}", errorOutput, StringComparer.Ordinal);
        Assert.Equal(0, frontend.AnalyzeCallCount);
    }

    [Fact]
    public void Compare_MissingBaselineFileExits3()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string baselinePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sarif");
        FakeFrontend frontend = new("csharp", _ => true);

        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
            legacy.Path, modern.Path, "equiv.sarif", baselinePath, configPath: null, "divergent", dryRun: false,
            [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal($"error: file not found (baseline={baselinePath}){Environment.NewLine}", errorOutput, StringComparer.Ordinal);
        Assert.Equal(0, frontend.AnalyzeCallCount);
    }

    [Fact]
    public void Compare_InvalidConfigJsonExits3()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string configPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(configPath, "{ not json");
            FakeFrontend frontend = new("csharp", _ => true);

            int exitCode = ExitCodes.Success;

            string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
                legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath, "divergent", dryRun: false,
                [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

            Assert.Equal(ExitCodes.UsageError, exitCode);
            Assert.False(string.IsNullOrWhiteSpace(errorOutput));
            Assert.Equal(0, frontend.AnalyzeCallCount);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Compare_InvalidBaselineJsonExits3()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string baselinePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(baselinePath, "{ not json");
            FakeFrontend frontend = new("csharp", _ => true);

            int exitCode = ExitCodes.Success;

            string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
                legacy.Path, modern.Path, "equiv.sarif", baselinePath, configPath: null, "divergent", dryRun: false,
                [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink()));

            Assert.Equal(ExitCodes.UsageError, exitCode);
            Assert.StartsWith($"error: '{baselinePath}' is not a valid SARIF log: ", errorOutput, StringComparison.Ordinal);
            Assert.Equal(0, frontend.AnalyzeCallCount);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void Compare_WarnsOnInvalidConfigValues()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string configPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(configPath, """{ "bound": 0 }""");
            MatchResult matchResult = new([Pair(PairIdentity)], [], [], []);
            FakeFrontend frontend = new("csharp", _ => true, matchResult);
            FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent() });
            int exitCode = ExitCodes.Success;

            string errorOutput = CaptureStdErr(() => exitCode = CompareCommand.Run(
                legacy.Path, modern.Path, "equiv.sarif", baselinePath: null, configPath, "divergent", dryRun: false,
                [frontend], backend, new InMemoryReportSink()));

            Assert.Equal(ExitCodes.Success, exitCode);
            Assert.Contains("CFG002", errorOutput, StringComparison.Ordinal);
            Assert.Equal(EquivConfig.Default.Bound, Assert.Single(backend.Calls).Bound);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    private static ProcedurePair Pair(ProcedureIdentity identity)
    {
        IrProcedure body = IrText.Parse($"proc \"{identity.Value}\" () entry B0 B0: ret");
        return new ProcedurePair(identity, identity, body, body);
    }

    private static Counterexample Counterexample() =>
        new(new IrInputs([new IrBitVecValue(32, 0)]), Run(1), Run(2));

    private static IrRun Run(int returned) => new(new IrReturned(new IrBitVecValue(32, (ulong)returned)), [], []);

    private static string CaptureStdOut(Action action)
    {
        TextWriter original = Console.Out;
        using StringWriter writer = new();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    private static string CaptureStdErr(Action action)
    {
        TextWriter original = Console.Error;
        using StringWriter writer = new();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return writer.ToString();
    }

    private static string Serialize(SarifLog log)
    {
        string path = Path.GetTempFileName();
        try
        {
            log.Save(path);
            return File.ReadAllText(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
