using System.Text.Json;

using CsCheck;

using Equiv.Cli;
using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;
using Equiv.Execute;
using Equiv.Execute.Testing;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P1-008: Unknown pairs tested on generated inputs (ADR 0035 decision 3). <see cref="BusinessLayer_Execute_Snapshot"/>
/// runs <c>equiv compare --execute</c> on <c>samples/business-layer</c> on both real runtimes; the generators' seed is fixed
/// (<see cref="DifferentialTester.Seed"/>) and the run stops at the target, not on the clock, so its SARIF is checked in as
/// <c>business-layer.execute.sarif</c>. <see cref="TestingNeverYieldsEquivalent"/> runs the tester over the M0-012
/// generator's pairs, both sides executed in-process on each generated input. Windows only, like the rest of this project.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class TestedUnknownTests
{
    private const int Pairs = 30;

    private static readonly TestingOptions Budget = TestingOptions.Default with { Inputs = 200 };

    /// <summary><c>Oracle.M(int a, int b, long c, long d, bool e, string s, int[] u)</c>, as the M3-032 generators see it.</summary>
    private static readonly TestingPlan Plan = TestingPlan.Runnable(
        new ExecutionDrivers("legacy.exe", "modern.dll"),
        [
            Parameter(ExecutionTypeKind.Signed32), Parameter(ExecutionTypeKind.Signed32), Parameter(ExecutionTypeKind.Signed64),
            Parameter(ExecutionTypeKind.Signed64), Parameter(ExecutionTypeKind.Boolean), Parameter(ExecutionTypeKind.Text), Parameter(ExecutionTypeKind.NullOnly),
        ],
        []);

    private static string Sample =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "business-layer"));

    private static string Snapshot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "business-layer.execute.sarif"));

    /// <summary>Criterion 7.</summary>
    [Fact]
    public async Task BusinessLayer_Execute_Snapshot()
    {
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P1-008-{Guid.NewGuid():N}.sarif");
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter output = new();
        using StringWriter error = new();
        Console.SetOut(output);
        Console.SetError(error);
        string json;
        SarifLog log;
        int exitCode;
        try
        {
            exitCode = Program.Main(
            [
                "compare",
                "--legacy", Path.Combine(Sample, "legacy", "Equiv.Samples.BusinessLayer.Legacy.sln"),
                "--modern", Path.Combine(Sample, "modern", "Equiv.Samples.BusinessLayer.Modern.slnx"),
                "--out", outPath,
                "--execute",
                "--mode", "thorough",
            ]);
            json = await File.ReadAllTextAsync(outPath, TestContext.Current.CancellationToken);
            log = SarifLog.Load(outPath);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            File.Delete(outPath);
        }

        Assert.Equal(ExitCodes.Divergent, exitCode);

        // Ticket P1-032 (ADR 0049): only thorough mode tests an Unknown under --execute, so the run asks for it. But the
        // sample's one Unknown, Describe, is Unknown after the first pass only. Thorough's IL pass reads both sides from IL, where string.Format and the interpolated string are different calls, and
        // finds them Divergent, so --execute replays it and no Unknown is left for it to test.
        Assert.DoesNotContain(log.Runs[0].Results, static r => string.Equals(r.RuleId, "EQ003", StringComparison.Ordinal));
        Result describe = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("::Describe(", StringComparison.Ordinal));
        Assert.Equal(("EQ002", "il-pass", "il"), (describe.RuleId, describe.GetProperty<string>("decidedBy"), describe.GetProperty<string>("lowering")));
        Assert.True(describe.TryGetProperty("replay", out string? _));
        Assert.All(
            log.Runs[0].Results.Where(static r => string.Equals(r.RuleId, "EQ001", StringComparison.Ordinal)),
            static r => Assert.False(r.TryGetProperty("differentialTesting", out Dictionary<string, object>? _)));

        string normalized = SarifNormalizer.Normalize(json, Sample);
        if (!File.Exists(Snapshot))
        {
            await File.WriteAllTextAsync(Snapshot, normalized, TestContext.Current.CancellationToken);
            Assert.Fail("business-layer.execute.sarif was missing; wrote today's output. Review it, commit it, and re-run.");
        }

        Assert.Equal(await File.ReadAllTextAsync(Snapshot, TestContext.Current.CancellationToken), normalized);
    }

    /// <summary>
    /// Criterion 4: whatever the pair and whatever the runtimes show, testing leaves an Unknown Unknown or makes it an
    /// observed Divergent, never Equivalent; and a divergence it observes is one the solver did not prove away.
    /// </summary>
    [Fact]
    public void TestingNeverYieldsEquivalent() =>
        PairGen.Pair.Sample(
            static pair =>
            {
                PairRuntime.Analysis analysis = PairRuntime.Analyse(pair.LegacySource, pair.ModernSource);
                using PairRuntime.Loaded loaded = new(analysis);
                TestingOutcome outcome = new DifferentialTester(new InProcessHost(loaded), Budget, TimeProvider.System).Test(Plan, analysis.Old, analysis.New);
                VerificationResult tested = outcome.Apply(new VerificationResult(new ProcedureIdentity("Oracle::M"), new Unknown(UnknownReason.Opaque, "generated")));
                return tested.Verdict is Unknown or Divergent { Observed: not null } && (outcome.Observed is null || analysis.Verdict is not Equivalent);
            },
            seed: DifferentialSoundnessTests.Budget.Seed,
            iter: Pairs);

    private static ExecutionParameter Parameter(ExecutionTypeKind kind) => new(kind.ToString(), kind, []);

    /// <summary>A driver host that runs each case on the pair's two sides in this process, as <see cref="PairRuntime.Loaded"/> observes them.</summary>
    private sealed class InProcessHost(PairRuntime.Loaded loaded) : IDriverHost
    {
        public IDriverSession Start(string driver) => new Session(loaded, driver.EndsWith(".exe", StringComparison.Ordinal));

        public IDriverHost Within(string directory) => this;

        private sealed class Session(PairRuntime.Loaded loaded, bool legacy) : IDriverSession
        {
            public string? Exchange(string line, TimeSpan timeout)
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement[] a = [.. document.RootElement.EnumerateArray()];
                PairInput input = new(a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt64(), a[4].GetInt64(), a[5].GetBoolean(), a[6].ValueKind == JsonValueKind.Null, 0, U: null);
                (string legacyObserved, string modernObserved) = loaded.Observe(input);
                return $"[\"Returned\",{JsonSerializer.Serialize(legacy ? legacyObserved : modernObserved)}]";
            }

            public void Dispose()
            {
            }
        }
    }
}
