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
/// Ticket M3-016 criterion 10 (ADR 0027 decision 4) on a real sample: <c>OrderService.Describe</c> in
/// <c>samples/business-layer</c> is loop-free and stays Unknown because its interpolated string is an expression-level
/// opaque, and its SARIF result points at that string's line in the modern source, not at the method.
/// </summary>
[Trait("Category", "Integration")]
public sealed class UnknownLocationSampleTests
{
    /// <summary>The line of <c>return $"Order {order.Id} for {order.CustomerName}";</c> in <c>modern/OrderService.cs</c>.</summary>
    private const int InterpolatedStringLine = 59;

    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    [Fact]
    public void BusinessLayerUnknownPointsAtItsConstruct()
    {
        MatchResult match = new CSharpFrontend().Analyze(Solution("legacy", "*.sln"), Solution("modern", "*.slnx"), EquivConfig.Default, TestContext.Current.CancellationToken).Match;
        ProcedurePair describe = match.Pairs.Single(static p => p.New.Value.Contains("OrderService::Describe(", StringComparison.Ordinal));
        VerificationOptions options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, EquivConfig.Default.CallIdentityRenames);

        Unknown unknown = Assert.IsType<Unknown>(new Z3Backend().Verify(describe.OldBody!, describe.NewBody!, options));
        Result result = SarifReportWriter.Write([new VerificationResult(describe.New, unknown)]).Runs[0].Results[0];

        Assert.Equal(UnknownReason.Opaque, unknown.Reason);
        Assert.Contains(unknown.Causes, static c => c.Side == Codebase.Legacy);
        PhysicalLocation primary = Assert.Single(result.Locations).PhysicalLocation;
        Assert.Equal(InterpolatedStringLine, primary.Region.StartLine);
        Assert.EndsWith("modern/OrderService.cs", primary.ArtifactLocation.Uri.OriginalString, StringComparison.Ordinal);
        Assert.NotEqual(describe.New.Location!.StartLine, primary.Region.StartLine);
        Assert.Equal(unknown.Causes.Length, result.RelatedLocations.Count);
    }

    private static string Solution(string side, string pattern) =>
        Directory.GetFiles(Path.Combine(SamplesRoot, "business-layer", side), pattern).Single();
}
