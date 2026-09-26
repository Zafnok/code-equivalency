using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

using Equiv.Cli;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M3-003: <see cref="Program.Main"/>, wired to the real <c>Equiv.Frontend.CSharp</c>/<c>Equiv.Verify.Z3</c>
/// backend, end to end on every directory under <c>samples/</c>. Criterion 3's snapshot and criterion 4's explicit
/// per-verdict assertions both read the one cached <see cref="RunSample"/> per sample, so each sample runs once no
/// matter how many facts examine it.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed partial class SamplesEndToEndTests
{
    private static readonly ConcurrentDictionary<string, Lazy<SampleRun>> Cache = new(StringComparer.Ordinal);

    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string> Samples =>
        [.. Directory.GetDirectories(SamplesRoot).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task MatchesTheCheckedInSarifSnapshotAndItsReadmesExitCode(string sample)
    {
        SampleRun run = RunSample(sample);

        Assert.Equal(ExpectedExitCode(sample), run.ExitCode);

        string expectedPath = Path.Combine(SamplesRoot, sample, "expected.sarif.json");
        if (!File.Exists(expectedPath))
        {
            // First run for a new sample (or a deleted snapshot): write today's output, but still fail, as Verify does,
            // so CI never passes a sample nobody has reviewed. Commit the file and it is reviewed like code in the PR diff.
            await File.WriteAllTextAsync(expectedPath, run.NormalizedSarif, TestContext.Current.CancellationToken);
            Assert.Fail($"{sample}/expected.sarif.json was missing; wrote today's output. Review it, commit it, and re-run.");
        }

        string expected = await File.ReadAllTextAsync(expectedPath, TestContext.Current.CancellationToken);
        Assert.Equal(expected, run.NormalizedSarif);
    }

    /// <summary>Criterion 5: a second run with the first SARIF as <c>--baseline</c> exits 0 and carries every result as unchanged.</summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public void BaselineRoundTripIsAllUnchanged(string sample)
    {
        SampleRun first = RunSample(sample);
        string baselinePath = Path.Combine(Path.GetTempPath(), $"equiv-M3-003-baseline-{Guid.NewGuid():N}.sarif");
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M3-003-rerun-{Guid.NewGuid():N}.sarif");
        try
        {
            File.WriteAllText(baselinePath, first.Json);
            (string legacy, string modern) = Solutions(sample);

            int exitCode = RunProgramSilently(() => Program.Main(
                ["compare", "--legacy", legacy, "--modern", modern, "--out", outPath, "--baseline", baselinePath]));

            Assert.Equal(ExitCodes.Success, exitCode);
            SarifLog log = SarifLog.Load(outPath);
            Assert.NotEmpty(log.Runs[0].Results);
            Assert.All(log.Runs[0].Results, static r => Assert.Equal(BaselineState.Unchanged, r.BaselineState));
        }
        finally
        {
            File.Delete(baselinePath);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Identical_EveryProcedureIsEquivalentWithAProofMethod()
    {
        Result[] results = [.. RunSample("identical").Log.Runs[0].Results];
        Assert.NotEmpty(results);
        Assert.All(results, static r =>
        {
            Assert.Equal("EQ001", r.RuleId);
            Assert.True(r.TryGetProperty("proofMethod", out string? proofMethod));
            Assert.False(string.IsNullOrEmpty(proofMethod));
        });
    }

    [Fact]
    public void RenamedLocals_EveryProcedureIsEquivalent()
    {
        Result[] results = [.. RunSample("renamed-locals").Log.Runs[0].Results];
        Assert.NotEmpty(results);
        Assert.All(results, static r => Assert.Equal("EQ001", r.RuleId));
    }

    [Fact]
    public void AddedBranch_IsDivergentWithACounterexample()
    {
        Result result = Single("added-branch", "::Double(");
        Assert.Equal("EQ002", result.RuleId);
        Assert.Contains("inputs(bv32 0)", result.Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedNullCheck_IsDivergentOnDifferingExceptionTypes()
    {
        Result result = Single("removed-null-check", "::Greet(");
        Assert.Equal("EQ002", result.RuleId);
        Assert.Contains("ArgumentNullException", result.Message.Text, StringComparison.Ordinal);
        Assert.Contains("NullReferenceException", result.Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void LoopBoundChange_IsDivergentOnRung1()
    {
        Result result = Single("loop-bound-change", "::SumUpTo(");
        Assert.Equal("EQ002", result.RuleId);
        List<Dictionary<string, string>> ladder = result.GetProperty<List<Dictionary<string, string>>>("ladderTrace");
        Assert.Contains(ladder, static step => string.Equals(step["rung"], "bounded", StringComparison.Ordinal) && string.Equals(step["outcome"], "refuted", StringComparison.Ordinal));
    }

    [Fact]
    public void WebApiBasic_MatchesByEndpointAndFindCarriesTheEquivalencesApplied()
    {
        SarifLog log = RunSample("webapi-basic").Log;
        Result get = log.Runs[0].Results.Single(static r => string.Equals(r.Message.Text.Split(' ')[0], "GET", StringComparison.Ordinal) && r.Message.Text.Contains("/api/orders/{id}", StringComparison.Ordinal) && !r.Message.Text.Contains("find", StringComparison.Ordinal));
        Result find = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("/api/orders/find/{id}", StringComparison.Ordinal));

        Assert.Equal("EQ001", get.RuleId);
        Assert.Equal("endpoint", Assert.Single(get.Locations).LogicalLocations.Single().Kind);
        Assert.Equal("EQ001", find.RuleId);
        Assert.Equal("endpoint", Assert.Single(find.Locations).LogicalLocations.Single().Kind);
        Assert.Equal(
            ["webapi.not-found", "webapi.ok-of-int", "webapi.type.action-result", "webapi.type.not-found-result", "webapi.type.ok-content-result"],
            find.GetProperty<List<string>>("equivalencesApplied"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ApiDrift_PartsIsEquivalentAndHasXIsDivergentOnANullString()
    {
        SarifLog log = RunSample("api-drift").Log;
        Result parts = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("::Parts(", StringComparison.Ordinal));
        Result hasX = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("::HasX(", StringComparison.Ordinal));

        Assert.Equal("EQ001", parts.RuleId);
        Assert.Equal("EQ002", hasX.RuleId);
        Assert.Contains("System.NullReferenceException", hasX.Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AddedRemoved_HasAnEQ004AndAnEQ005()
    {
        Result[] results = [.. RunSample("added-removed").Log.Runs[0].Results];
        Assert.Contains(results, static r => string.Equals(r.RuleId, "EQ004", StringComparison.Ordinal));
        Assert.Contains(results, static r => string.Equals(r.RuleId, "EQ005", StringComparison.Ordinal));
    }

    [Fact]
    public void CalleeChanged_TaxIsDivergentAndTotalIsEquivalentWithAnUnprovenAssumption()
    {
        SarifLog log = RunSample("callee-changed").Log;
        Result tax = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("::Tax(int) diverges", StringComparison.Ordinal));
        Result total = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("::Total(int) is equivalent", StringComparison.Ordinal));

        Assert.Equal("EQ002", tax.RuleId);
        Assert.Equal("EQ001", total.RuleId);
        Assert.Contains(total.GetProperty<List<string>>("unprovenAssumptions"), static a => a.Contains("::Tax(", StringComparison.Ordinal));
    }

    private static Result Single(string sample, string messageContains) =>
        RunSample(sample).Log.Runs[0].Results.Single(r => r.Message.Text.Contains(messageContains, StringComparison.Ordinal));

    private static int ExpectedExitCode(string sample)
    {
        string readme = File.ReadAllText(Path.Combine(SamplesRoot, sample, "README.md"));
        Match match = ExitCodePattern.Match(readme);
        Assert.True(match.Success, $"{sample}/README.md does not document its exit code");
        return int.Parse(match.Groups["code"].Value, CultureInfo.InvariantCulture);
    }

    private static (string Legacy, string Modern) Solutions(string sample) => (
        Directory.GetFiles(Path.Combine(SamplesRoot, sample, "legacy"), "*.sln").Single(),
        Directory.GetFiles(Path.Combine(SamplesRoot, sample, "modern"), "*.slnx").Single());

    private static SampleRun RunSample(string sample) =>
        Cache.GetOrAdd(sample, static s => new Lazy<SampleRun>(() => Execute(s))).Value;

    private static SampleRun Execute(string sample)
    {
        string sampleDir = Path.Combine(SamplesRoot, sample);
        (string legacy, string modern) = Solutions(sample);
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M3-003-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = RunProgramSilently(() => Program.Main(["compare", "--legacy", legacy, "--modern", modern, "--out", outPath]));
            string json = File.ReadAllText(outPath);
            return new SampleRun(exitCode, json, SarifNormalizer.Normalize(json, sampleDir), SarifLog.Load(outPath));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    private static int RunProgramSilently(Func<int> action)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter output = new();
        using StringWriter error = new();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            return action();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [GeneratedRegex(@"Exit code:\s*(?<code>\d+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExitCodePattern { get; }

    private sealed record SampleRun(int ExitCode, string Json, string NormalizedSarif, SarifLog Log);
}
