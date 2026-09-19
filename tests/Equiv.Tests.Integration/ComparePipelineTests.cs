using Equiv.Cli;
using Equiv.Core.Reporting;
using Equiv.Frontend.CSharp;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// <c>equiv compare</c> end to end on real samples (ticket M2-002; ADR 0012): <see cref="CSharpFrontend"/>
/// enumerates and matches both sides, a matched pair gets no result (no backend before M3-001),
/// and an Added/Removed identity carries a <c>physicalLocation</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ComparePipelineTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    [Fact]
    public void IdenticalYieldsNoResults()
    {
        string legacy = Directory.GetFiles(Path.Combine(SamplesRoot, "identical", "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(SamplesRoot, "identical", "modern"), "*.slnx").Single();
        string outPath = TempSarifPath();

        try
        {
            int exitCode = CompareCommand.Run(
                legacy, modern, outPath, baselinePath: null, configPath: null, failOn: "divergent", dryRun: false,
                [new CSharpFrontend()], backend: null, new FileReportSink(outPath));

            Assert.Equal(ExitCodes.Success, exitCode);
            SarifLog log = SarifLog.Load(outPath);
            Assert.Empty(log.Runs[0].Results);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public async Task AddedAndRemovedHaveLocations()
    {
        string legacy = Directory.GetFiles(Path.Combine(SamplesRoot, "added-removed", "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(SamplesRoot, "added-removed", "modern"), "*.slnx").Single();
        string outPath = TempSarifPath();

        try
        {
            int exitCode = CompareCommand.Run(
                legacy, modern, outPath, baselinePath: null, configPath: null, failOn: "divergent", dryRun: false,
                [new CSharpFrontend()], backend: null, new FileReportSink(outPath));

            Assert.Equal(ExitCodes.Success, exitCode);
            SarifLog log = SarifLog.Load(outPath);

            Assert.Equal(2, log.Runs[0].Results.Count);
            Assert.Contains(log.Runs[0].Results, static r => string.Equals(r.RuleId, "EQ004", StringComparison.Ordinal)
                && r.Locations[0].PhysicalLocation.ArtifactLocation.Uri.OriginalString.EndsWith("modern/Calculator.cs", StringComparison.Ordinal));
            Assert.Contains(log.Runs[0].Results, static r => string.Equals(r.RuleId, "EQ005", StringComparison.Ordinal)
                && r.Locations[0].PhysicalLocation.ArtifactLocation.Uri.OriginalString.EndsWith("legacy/Calculator.cs", StringComparison.Ordinal));

            // The physicalLocation.artifactLocation.uri is an absolute path rooted at this checkout;
            // scrub it to the fixed placeholder below so the snapshot is portable across machines/CI.
            string samplesRoot = SamplesRoot.Replace('\\', '/');
            string json = (await File.ReadAllTextAsync(outPath, TestContext.Current.CancellationToken)).Replace(samplesRoot, "<samples>", StringComparison.Ordinal);

            await VerifyJson(json);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    private static string TempSarifPath() => Path.Combine(Path.GetTempPath(), $"equiv-M2-002-{Guid.NewGuid():N}.sarif");
}
