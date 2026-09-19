using System.Linq;

using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Core.Tests.Reporting;

/// <summary>
/// Pins <see cref="SarifReportWriter.Write"/> output for each verdict kind (VERIFICATION-MODEL.md
/// section 6) and checks it against the SARIF SDK itself (version/schema, save/load round trip)
/// rather than a hand-rolled JSON Schema check.
/// </summary>
public sealed class SarifReportWriterTests
{
    [Fact]
    public Task Equivalent() => VerifyJson(Serialize(Fixtures.Result(new Equivalent())));

    [Fact]
    public Task Divergent() => VerifyJson(Serialize(Fixtures.Result(new Divergent(Fixtures.Counterexample()))));

    [Fact]
    public Task Unknown() => VerifyJson(Serialize(Fixtures.Result(new Unknown(UnknownReason.Timeout, "solver gave up after 5000ms"))));

    [Fact]
    public Task Added() => VerifyJson(Serialize(Fixtures.Result(new Added())));

    [Fact]
    public Task Removed() => VerifyJson(Serialize(Fixtures.Result(new Removed())));

    [Fact]
    public void ResultWithALocationCarriesAPhysicalLocation()
    {
        ProcedureIdentity identity = new("Samples.Math::Add(int32,int32)", new SourceSpan(@"C:\src\Math.cs", 10, 5, 10, 8));
        SarifLog log = SarifReportWriter.Write([new VerificationResult(identity, new Added())]);
        Location location = Assert.Single(log.Runs[0].Results[0].Locations);

        Assert.Equal("C:/src/Math.cs", location.PhysicalLocation.ArtifactLocation.Uri.OriginalString);
        Assert.Equal(10, location.PhysicalLocation.Region.StartLine);
        Assert.Equal(5, location.PhysicalLocation.Region.StartColumn);
        Assert.Equal(10, location.PhysicalLocation.Region.EndLine);
        Assert.Equal(8, location.PhysicalLocation.Region.EndColumn);
    }

    [Fact]
    public void ResultWithoutALocationHasNoLocations()
    {
        SarifLog log = SarifReportWriter.Write([Fixtures.Result(new Equivalent())]);
        Assert.Null(log.Runs[0].Results[0].Locations);
    }

    [Fact]
    public void NullResultsThrow()
    {
        Assert.Throws<ArgumentNullException>(static () => SarifReportWriter.Write(null!));
    }

    [Fact]
    public void ToolDriverListsAllFiveRulesInOrder()
    {
        SarifLog log = SarifReportWriter.Write([]);
        Assert.Equal(["EQ001", "EQ002", "EQ003", "EQ004", "EQ005"], log.Runs[0].Tool.Driver.Rules.Select(static r => r.Id), StringComparer.Ordinal);
    }

    [Fact]
    public void WithoutABaselineEveryResultIsNew()
    {
        SarifLog log = SarifReportWriter.Write([Fixtures.Result(new Equivalent())]);
        Assert.Equal(BaselineState.New, log.Runs[0].Results[0].BaselineState);
    }

    [Fact]
    public void DivergentResultCarriesTheCounterexampleInPropertiesModelAndInTheMessage()
    {
        Counterexample counterexample = Fixtures.Counterexample();
        SarifLog log = SarifReportWriter.Write([Fixtures.Result(new Divergent(counterexample))]);
        Result result = log.Runs[0].Results[0];
        string model = CounterexampleText.Dump(counterexample);

        Assert.Equal(model, result.GetProperty<string>("model"));
        Assert.Contains(model, result.Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryResultCarriesItsProcedureIdentityAndFingerprintAsPartialFingerprints()
    {
        VerificationResult result = Fixtures.Result(new Equivalent());
        SarifLog log = SarifReportWriter.Write([result]);
        Result sarifResult = log.Runs[0].Results[0];

        Assert.Equal(result.Identity.Value, sarifResult.PartialFingerprints[SarifReportWriter.ProcedureIdentityFingerprintId]);
        Assert.Equal(ResultFingerprint.Compute(result), sarifResult.PartialFingerprints[SarifReportWriter.ResultFingerprintId]);
    }

    [Fact]
    public void WrittenLogDeclaresSarif210AndRoundTripsThroughTheSdk()
    {
        SarifLog log = SarifReportWriter.Write(
        [
            Fixtures.Result(new Equivalent(), "A"),
            Fixtures.Result(new Divergent(Fixtures.Counterexample()), "B"),
            Fixtures.Result(new Unknown(UnknownReason.Opaque, "detail"), "C"),
            Fixtures.Result(new Added(), "D"),
            Fixtures.Result(new Removed(), "E"),
        ]);

        Assert.Equal(SarifVersion.Current, log.Version);
        Assert.Equal(SarifUtilities.SarifSchemaUri, log.SchemaUri?.ToString());

        string path = Path.GetTempFileName();
        try
        {
            log.Save(path);
            SarifLog loaded = SarifLog.Load(path);

            // Not a whole-log ValueEquals: SARIF's own default-value omission (e.g. EQ003's
            // defaultConfiguration.level "warning", the spec's own default, is never written)
            // means a rule with only default fields reloads as a *null* defaultConfiguration
            // rather than an equal one. That is round-tripping correctly per the SDK's own
            // reader/writer contract, so this test asserts on the results (the reporting data
            // this ticket owns), not on a byte-for-byte tool/driver comparison.
            Assert.Equal(log.Runs[0].Results.Count, loaded.Runs[0].Results.Count);
            Assert.All(
                Enumerable.Range(0, log.Runs[0].Results.Count),
                i => Assert.True(log.Runs[0].Results[i].ValueEquals(loaded.Runs[0].Results[i])));
            Assert.Equal(
                log.Runs[0].Tool.Driver.Rules.Select(static r => r.Id),
                loaded.Runs[0].Tool.Driver.Rules.Select(static r => r.Id),
                StringComparer.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Serialize(VerificationResult result)
    {
        SarifLog log = SarifReportWriter.Write([result]);
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
