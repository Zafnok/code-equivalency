using Equiv.Cli;
using Equiv.Core.Reporting;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// <c>equiv compare</c> end to end on real samples (tickets M2-002 and M3-001): <see cref="CSharpFrontend"/>
/// enumerates and matches both sides, <see cref="Z3Backend"/> gives every matched pair a verdict, and an
/// Added/Removed identity carries a <c>physicalLocation</c>.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class ComparePipelineTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    [Fact]
    public void IdenticalYieldsOnlyEquivalentResults()
    {
        string legacy = Directory.GetFiles(Path.Combine(SamplesRoot, "identical", "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(SamplesRoot, "identical", "modern"), "*.slnx").Single();
        string outPath = TempSarifPath();

        try
        {
            int exitCode = CompareCommand.Run(
                new CompareOptions(legacy, modern, outPath, BaselinePath: null, ConfigPath: null, FailOn: "divergent", DryRun: false),
                [new CSharpFrontend()], new Z3Backend(), new FileReportSink(outPath));

            Assert.Equal(ExitCodes.Success, exitCode);
            SarifLog log = SarifLog.Load(outPath);
            Assert.NotEmpty(log.Runs[0].Results);
            Assert.All(log.Runs[0].Results, static r => Assert.Equal("EQ001", r.RuleId));
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
                new CompareOptions(legacy, modern, outPath, BaselinePath: null, ConfigPath: null, FailOn: "divergent", DryRun: false),
                [new CSharpFrontend()], new Z3Backend(), new FileReportSink(outPath));

            Assert.Equal(ExitCodes.Success, exitCode);
            SarifLog log = SarifLog.Load(outPath);

            Assert.Contains(log.Runs[0].Results, static r => string.Equals(r.RuleId, "EQ001", StringComparison.Ordinal));
            Assert.Equal(2, log.Runs[0].Results.Count(static r => !string.Equals(r.RuleId, "EQ001", StringComparison.Ordinal)));
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
