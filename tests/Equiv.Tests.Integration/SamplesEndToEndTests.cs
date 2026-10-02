using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

using Equiv.Cli;
using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Progress;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

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

    /// <summary>Ticket P1-001 criterion 1: rung 4 proves both unaligned-loop samples, with Spacer's invariant and the arithmetic it holds in.</summary>
    [Theory]
    [InlineData("loop-to-linq", "::CountPositive(", "bitvector")]
    [InlineData("loop-fusion", "::CountOutside(", "int")]
    public void UnalignedLoopSamplesAreEquivalentByChcWithTheirInvariant(string sample, string member, string chcMode)
    {
        Result result = Single(sample, member);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("chc", result.GetProperty<string>("proofMethod"));
        Assert.Equal(chcMode, result.GetProperty<string>("chcMode"));
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty<string>("invariant")));
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

    /// <summary>
    /// Ticket P2-055 criterion 2 (ADR 0040 decision 2): both sides run on .NET 10, so no runtime rule applies. The
    /// byte-identical method that calls <c>double.ToString()</c> and <c>string.StartsWith(string)</c> and casts a
    /// <c>double</c> to <c>int</c> is Equivalent by congruence, and the <c>if</c> chain made a <c>switch</c> expression is
    /// Equivalent by the solver. No call counts as a runtime-change call.
    /// </summary>
    [Fact]
    public void SameRuntimeCleanup_NoRuntimeRuleApplies()
    {
        Run run = RunSample("same-runtime-cleanup").Log.Runs[0];
        Result describe = Single("same-runtime-cleanup", "::Describe(");
        Result rank = Single("same-runtime-cleanup", "::Rank(");

        Assert.Equal("EQ001", describe.RuleId);
        Assert.Equal("congruence", describe.GetProperty<string>("proofMethod"));
        Assert.Equal("EQ001", rank.RuleId);
        Assert.Equal("bounded", rank.GetProperty<string>("proofMethod"));
        Assert.Equal(2, run.Results.Count);
        Assert.Null(run.Invocations);
        Assert.Contains("\"callSites\":{\"legacy\":0,\"modern\":0}", Newtonsoft.Json.JsonConvert.SerializeObject(run.GetProperty<Dictionary<string, object>>("loweringCensus")["runtimeChangeCalls"]), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-055 criterion 3: .NET 8 against .NET 10 crosses only the rows changed in .NET 9 or .NET 10. The
    /// byte-identical method that calls <c>BinaryReader.ReadString</c> (changed in .NET 9) is not congruent and is EQ006,
    /// its message naming both runtimes; the one that calls <c>double.ToString()</c> (changed in .NET Core 3.0) is
    /// Equivalent by congruence.
    /// </summary>
    [Fact]
    public void VersionBump_OnlyRowsInsideTheIntervalApply()
    {
        Result name = Single("version-bump", "::Name(");
        Result format = Single("version-bump", "::Format(");

        Assert.Equal("EQ006", name.RuleId);
        Assert.Contains("diverges via a runtime-changed API between net8.0 and net10.0 (", name.Message.Text, StringComparison.Ordinal);
        Assert.Contains("/compatibility/core-libraries/9.0/", name.GetProperty<string>("helpUri"), StringComparison.Ordinal);
        Assert.Equal("EQ001", format.RuleId);
        Assert.Equal("congruence", format.GetProperty<string>("proofMethod"));
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

    /// <summary>
    /// Ticket P1-010 criterion 5 (ADR 0036 decision 2): the caller sees only <c>Score(a) &gt; 0</c>, so it is Equivalent
    /// under an admitted contract for the Divergent <c>Score</c>, which leaves no unproven assumption. The checked-in
    /// snapshot is the whole run.
    /// </summary>
    [Fact]
    public async Task CalleeChangedInvisible_EquivalentPlusContract()
    {
        SampleRun run = RunSample("callee-changed-invisible");
        Result score = Single("callee-changed-invisible", "::Score(int) diverges");
        Result classify = Single("callee-changed-invisible", "::Classify(int) is equivalent");

        Assert.Equal(await Snapshot("callee-changed-invisible"), run.NormalizedSarif);
        Assert.Equal("EQ002", score.RuleId);
        Assert.Equal("EQ001", classify.RuleId);
        Assert.Equal("bounded+contract", classify.GetProperty<string>("proofMethod"));
        Dictionary<string, string> contract = Assert.Single(classify.GetProperty<List<Dictionary<string, string>>>("contractsUsed"));
        Assert.EndsWith("::Score(int)", contract["callee"], StringComparison.Ordinal);
        Assert.Equal("observed-predicates", contract["proposedBy"]);
        Assert.False(classify.TryGetProperty("unprovenAssumptions", out List<string>? _));
    }

    /// <summary>
    /// Ticket P1-010 criterion 6: <c>Total</c> returns what <c>Tax</c> returned, so it observes the change, no contract is
    /// used, and both verdicts and the snapshot stay as M3-015 left them.
    /// </summary>
    [Fact]
    public async Task CalleeChanged_Unaffected()
    {
        SampleRun run = RunSample("callee-changed");
        Result total = Single("callee-changed", "::Total(int) is equivalent");

        Assert.Equal(await Snapshot("callee-changed"), run.NormalizedSarif);
        Assert.Equal("congruence", total.GetProperty<string>("proofMethod"));
        Assert.False(total.TryGetProperty("contractsUsed", out List<Dictionary<string, string>>? _));
        Assert.Contains(total.GetProperty<List<string>>("unprovenAssumptions"), static a => a.Contains("::Tax(", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ticket P1-013 criterion 5 (ADR 0037), with the ticket's recorded deviation: the modern side computes an unshared
    /// opaque value and then adds a guard that throws, so the pair is Unknown; the new throw lies past the opaque node, so
    /// <c>newFailures</c> is <c>unknown</c>, and the legacy side never throws, so <c>removedFailures</c> is <c>none-proved</c>.
    /// </summary>
    [Fact]
    public async Task UnknownNewThrow_CarriesItsFailureRefinement()
    {
        SampleRun run = RunSample("unknown-new-throw");
        Result width = Single("unknown-new-throw", "::Width(int) is unknown");

        Assert.Equal(await Snapshot("unknown-new-throw"), run.NormalizedSarif);
        Assert.Equal("EQ003", width.RuleId);
        Dictionary<string, Dictionary<string, string>> refinement = width.GetProperty<Dictionary<string, Dictionary<string, string>>>("failureRefinement");
        Assert.Equal("unknown", refinement["newFailures"]["outcome"]);
        Assert.Equal("none-proved", refinement["removedFailures"]["outcome"]);
    }

    /// <summary>
    /// Ticket P2-085 criterion 2 (ADR 0029 as clarified): the modern project does not compile, and it is still loaded. The
    /// method that does not bind is Unknown(unbound) and points at its first error, the other three are verified, the one
    /// that calls it with that callee as an unproven assumption, the method of the file the tool did not carry over is
    /// Removed, and no project is skipped. The checked-in snapshot is the whole run.
    /// </summary>
    [Fact]
    public async Task PartlyCompilingModern_TheMethodThatDoesNotBindIsUnknownAndTheRestAreVerified()
    {
        SampleRun run = RunSample("partly-compiling-modern");
        Result currency = Single("partly-compiling-modern", "Pricing::Currency() is unknown");

        Assert.Equal(await Snapshot("partly-compiling-modern"), run.NormalizedSarif);
        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal("EQ003", currency.RuleId);
        Assert.Equal("unbound", currency.GetProperty<string>("unknownReason"));
        Assert.Equal("method", currency.GetProperty<string>("scope"));
        Assert.EndsWith("Pricing::Currency() is unknown (Unbound): the modern body does not bind", currency.Message.Text, StringComparison.Ordinal);
        PhysicalLocation error = Assert.Single(currency.RelatedLocations).PhysicalLocation;
        Assert.EndsWith("modern/Pricing.cs", error.ArtifactLocation.Uri.OriginalString, StringComparison.Ordinal);
        Assert.Equal((16, 20), (error.Region.StartLine, error.Region.StartColumn));
        Assert.Equal((16, 20), (currency.Locations[0].PhysicalLocation.Region.StartLine, currency.Locations[0].PhysicalLocation.Region.StartColumn));

        Assert.Equal("congruence", Single("partly-compiling-modern", "::Net(int) is equivalent").GetProperty<string>("proofMethod"));
        Assert.Equal("bounded", Single("partly-compiling-modern", "::Discount(int) is equivalent").GetProperty<string>("proofMethod"));
        Result caller = Single("partly-compiling-modern", "::HasCurrency() is equivalent");
        Assert.Equal("EQ001", caller.RuleId);
        Assert.Equal(["Equiv.Samples.PartlyCompilingModern.Pricing::Currency()"], caller.GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
        Assert.Equal("EQ005", Single("partly-compiling-modern", "Regional::Currency()").RuleId);

        Run sarif = run.Log.Runs[0];
        Assert.Equal(5, sarif.Results.Count);
        Assert.Null(sarif.Invocations);
        Assert.True(sarif.TryGetSerializedPropertyValue("loweringCensus", out string? census));
        Assert.Contains("\"projectsSkipped\":{\"legacy\":0,\"modern\":0}", census, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-069 criteria 2 and 3 (ADR 0042): <c>Has</c> is the same source on both sides, and the library upgrade rebinds
    /// its call from <c>FileBase::Exists</c> to <c>IFile::Exists</c>. It is Unknown, not Divergent: line-scoped, pointing at the
    /// call site on each side, and naming the two callees it treated as possibly the same. <c>Clear</c> calls <c>Delete</c>
    /// on the modern side, another member, and stays Divergent with no rebound pair.
    /// </summary>
    [Fact]
    public async Task DependencyRebinding_TheReboundCallIsUnknownAndNamedAndAnotherMemberStaysDivergent()
    {
        SampleRun run = RunSample("dependency-rebinding");
        Result has = Single("dependency-rebinding", "::Has(");
        Result clear = Single("dependency-rebinding", "::Clear(");

        Assert.Equal(await Snapshot("dependency-rebinding"), run.NormalizedSarif);
        Assert.Equal("EQ003", has.RuleId);
        Assert.Equal("opaque", has.GetProperty<string>("unknownReason"));
        Assert.Equal("line", has.GetProperty<string>("scope"));
        Dictionary<string, string> rebound = Assert.Single(has.GetProperty<List<Dictionary<string, string>>>("reboundCalls"));
        Assert.Equal("Equiv.Samples.DependencyRebinding.Files.FileBase::Exists(string)", rebound["legacy"]);
        Assert.Equal("Equiv.Samples.DependencyRebinding.Files.IFile::Exists(string)", rebound["modern"]);
        Assert.Equal(["rebound-call", "rebound-call"], has.RelatedLocations.Select(static l => l.Message.Text), StringComparer.Ordinal);
        Assert.Equal("EQ002", clear.RuleId);
        Assert.False(clear.TryGetProperty("reboundCalls", out List<Dictionary<string, string>>? _));
    }

    /// <summary>
    /// Ticket P2-064 criterion 5: the three methods the same API swap was made in share one review group, which ranks above
    /// the group of the method that changed on its own. The checked-in snapshot is the whole run.
    /// </summary>
    [Fact]
    public async Task RepeatedEdit_TheThreeSwapsAreOneGroupRankedAboveTheOther()
    {
        const string Swap = "calls:System.Convert::ToInt32(string)|System.Int32::Parse(string)";
        SampleRun run = RunSample("repeated-edit");
        Result[] results = [.. run.Log.Runs[0].Results];
        Result discount = Single("repeated-edit", "::Discount(int) diverges");

        Assert.Equal(await Snapshot("repeated-edit"), run.NormalizedSarif);
        Assert.Equal(4, results.Length);
        Assert.All(results, static r => Assert.Equal("EQ002", r.RuleId));
        Assert.Equal("proofMethod:none", discount.GetProperty<string>("reviewGroup"));
        Assert.Equal(60.002, discount.Rank);
        Assert.All(results.Except([discount]), static r =>
        {
            Assert.Equal(Swap, r.GetProperty<string>("reviewGroup"));
            Assert.Equal(60.006, r.Rank);
        });
        List<Dictionary<string, object>> list = run.Log.Runs[0].GetProperty<List<Dictionary<string, object>>>("reviewList");
        Assert.Equal([Swap, "proofMethod:none"], list.Select(static e => (string)e["group"]), StringComparer.Ordinal);
        Assert.Equal([3L, 1L], list.Select(static e => (long)e["count"]));
    }

    /// <summary>
    /// Ticket P1-013 criterion 4 (ADR 0037): on every sample, a run whose backend drops the failure refinement has the same
    /// exit code, and every result the same rule id and result fingerprint, as the real run.
    /// </summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public void Refinement_NeverChangesVerdictOrFingerprint(string sample)
    {
        SampleRun refined = RunSample(sample);
        (string legacy, string modern) = Solutions(sample);
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P1-013-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = RunProgramSilently(() => CompareCommand.Run(
                new Equiv.Cli.CompareOptions(legacy, modern, outPath, BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
                [new CSharpFrontend()],
                new WithoutRefinement(new Z3Backend()),
                new FileReportSink(outPath),
                NullRunLog.Instance));
            SarifLog plain = SarifLog.Load(outPath);

            Assert.Equal(refined.ExitCode, exitCode);
            Assert.Equal(Verdicts(refined.Log), Verdicts(plain));
            Assert.DoesNotContain(plain.Runs[0].Results, static r => r.TryGetProperty("failureRefinement", out Dictionary<string, object>? _));
        }
        finally
        {
            File.Delete(outPath);
        }

        static string[] Verdicts(SarifLog log) =>
            [.. log.Runs[0].Results.Select(static r => $"{r.PartialFingerprints["procedureIdentity/v1"]} {r.RuleId} {r.PartialFingerprints["resultFingerprint/v1"]}").Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Ticket P2-050 criterion 5: with a tiny <c>--resource-limit</c> and a ten-minute <c>timeoutMs</c>, the pair of
    /// <c>added-branch</c> that needs the solver is Unknown(timeout) on the resource limit, long before the wall-clock
    /// backstop, and a second run writes the same bytes: nothing in the result depends on how fast the machine was.
    /// </summary>
    [Fact]
    public void RepeatedRunsAreByteIdentical()
    {
        (string legacy, string modern) = Solutions("added-branch");
        string configPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-050-{Guid.NewGuid():N}.json");
        string[] outPaths = [.. Enumerable.Range(0, 2).Select(static _ => Path.Combine(Path.GetTempPath(), $"equiv-P2-050-{Guid.NewGuid():N}.sarif"))];
        try
        {
            File.WriteAllText(configPath, """{ "timeoutMs": 600000 }""");

            int[] exitCodes = [.. outPaths.Select(outPath => RunProgramSilently(() => Program.Main(
                ["compare", "--legacy", legacy, "--modern", modern, "--out", outPath, "--config", configPath, "--resource-limit", "1"])))];

            Assert.Equal([ExitCodes.Success, ExitCodes.Success], exitCodes);
            Result unknown = SarifLog.Load(outPaths[0]).Runs[0].Results.Single(static r => r.Message.Text.Contains("::Double(", StringComparison.Ordinal));
            Assert.Equal("EQ003", unknown.RuleId);
            Assert.Equal("timeout", unknown.GetProperty<string>("unknownReason"));
            Assert.Contains("solver returned unknown (canceled): resource limit 1 hit", unknown.Message.Text, StringComparison.Ordinal);
            Assert.Equal(File.ReadAllBytes(outPaths[0]), File.ReadAllBytes(outPaths[1]));
        }
        finally
        {
            File.Delete(configPath);
            Array.ForEach(outPaths, File.Delete);
        }
    }

    private static Task<string> Snapshot(string sample) =>
        File.ReadAllTextAsync(Path.Combine(SamplesRoot, sample, "expected.sarif.json"), TestContext.Current.CancellationToken);

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

    /// <summary><paramref name="backend"/> with every Unknown's failure refinement dropped, as the backend was before ticket P1-013.</summary>
    private sealed class WithoutRefinement(IVerificationBackend backend) : IVerificationBackend
    {
        public Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options)
        {
            Verdict verdict = backend.Verify(oldBody, newBody, options);
            return verdict is Unknown unknown ? unknown with { FailureRefinement = null } : verdict;
        }

        public Equivalent? VerifyUnderContracts(IrProcedure oldBody, IrProcedure newBody, ImmutableArray<CalleePair> callees, VerificationOptions options) =>
            backend.VerifyUnderContracts(oldBody, newBody, callees, options);
    }
}
