using Equiv.Cli;
using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M3-015 on the real samples: congruence never contradicts the solver on any sample pair (acceptance criterion 7),
/// and <c>samples/callee-changed</c> reports its caller Equivalent by congruence with the changed callee as an unproven
/// assumption (criterion 13; ADR 0019).
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class CongruenceSampleTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string> Samples =>
        [.. Directory.GetDirectories(SamplesRoot).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Samples))]
    public void CongruenceNeverContradictsTheSolverOnASample(string sample)
    {
        MatchResult result = new CSharpFrontend().Analyze(Solution(sample, "legacy"), Solution(sample, "modern"), EquivConfig.Default, TestContext.Current.CancellationToken).Match;
        VerificationOptions options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, EquivConfig.Default.CallIdentityRenames);

        foreach (ProcedurePair pair in result.Pairs.Where(static p => CompareCommand.IsCongruent(p, p.OldBody!, p.NewBody!)))
        {
            Assert.False(new Z3Backend().Verify(pair.OldBody!, pair.NewBody!, options) is Divergent, $"{pair.New.Value} is congruent but Divergent");
        }
    }

    [Fact]
    public void CalleeChangedReportsTheCallerEquivalentWithAnUnprovenAssumption()
    {
        const string Tax = "Equiv.Samples.CalleeChanged.Pricing::Tax(int)";
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M3-015-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = CompareCommand.Run(
                new CompareOptions(Solution("callee-changed", "legacy"), Solution("callee-changed", "modern"), outPath, BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false),
                [new CSharpFrontend()], new Z3Backend(), new FileReportSink(outPath));

            Assert.Equal(ExitCodes.Divergent, exitCode);
            Dictionary<string, Result> results = SarifLog.Load(outPath).Runs[0].Results
                .ToDictionary(static r => r.PartialFingerprints["procedureIdentity/v1"], StringComparer.Ordinal);
            Assert.Equal("EQ002", results[Tax].RuleId);
            Result total = results["Equiv.Samples.CalleeChanged.Pricing::Total(int)"];
            Assert.Equal("EQ001", total.RuleId);
            Assert.Equal("congruence", total.GetProperty<string>("proofMethod"));
            Assert.Equal([Tax], total.GetProperty<List<string>>("assumedCallees"), StringComparer.Ordinal);
            Assert.Equal([Tax], total.GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
            Assert.EndsWith($"Assumes callees equivalent; not proved for: {Tax}.", total.Message.Text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    private static string Solution(string sample, string side) =>
        Directory.GetFiles(Path.Combine(SamplesRoot, sample, side), string.Equals(side, "legacy", StringComparison.Ordinal) ? "*.sln" : "*.slnx").Single();
}
