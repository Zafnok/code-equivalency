using System.Linq;

using Equiv.Core.Ir;
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
    public Task Equivalent() => VerifyJson(Serialize(Fixtures.Result(new Equivalent(ProofMethod.Bounded))));

    [Fact]
    public Task Divergent() => VerifyJson(Serialize(Fixtures.Result(new Divergent(Fixtures.Counterexample()))));

    [Fact]
    public Task Unknown() => VerifyJson(Serialize(Fixtures.Result(new Unknown(UnknownReason.Timeout, "solver gave up after 5000ms"))));

    [Fact]
    public Task Added() => VerifyJson(Serialize(Fixtures.Result(new Added())));

    [Fact]
    public Task Removed() => VerifyJson(Serialize(Fixtures.Result(new Removed())));

    [Fact]
    public Task RuntimeChangedDivergent() => VerifyJson(Serialize(Fixtures.Result(new Divergent(RuntimeChangedCounterexample()))));

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
        SarifLog log = SarifReportWriter.Write([Fixtures.Result(new Equivalent(ProofMethod.Bounded))]);
        Assert.Null(log.Runs[0].Results[0].Locations);
    }

    [Fact]
    public void EndpointMatchedResultCarriesAnEndpointLogicalLocation()
    {
        ProcedureIdentity identity = new("GET /api/orders/{id}", new SourceSpan(@"C:\src\OrdersController.cs", 10, 5, 10, 8));
        SarifLog log = SarifReportWriter.Write([new VerificationResult(identity, new Equivalent(ProofMethod.Bounded))]);
        Location location = Assert.Single(log.Runs[0].Results[0].Locations);
        LogicalLocation logicalLocation = Assert.Single(location.LogicalLocations);

        Assert.NotNull(location.PhysicalLocation);
        Assert.Equal("endpoint", logicalLocation.Kind);
        Assert.Equal("GET /api/orders/{id}", logicalLocation.FullyQualifiedName);
    }

    [Fact]
    public void EndpointMatchedResultWithoutAPhysicalLocationStillCarriesALogicalLocation()
    {
        ProcedureIdentity identity = new("GET /api/orders/{id}");
        SarifLog log = SarifReportWriter.Write([new VerificationResult(identity, new Equivalent(ProofMethod.Bounded))]);
        Location location = Assert.Single(log.Runs[0].Results[0].Locations);
        LogicalLocation logicalLocation = Assert.Single(location.LogicalLocations);

        Assert.Null(location.PhysicalLocation);
        Assert.Equal("endpoint", logicalLocation.Kind);
    }

    [Fact]
    public void NonEndpointResultLocationHasNoLogicalLocations()
    {
        ProcedureIdentity identity = new("Samples.Math::Add(int32,int32)", new SourceSpan(@"C:\src\Math.cs", 10, 5, 10, 8));
        SarifLog log = SarifReportWriter.Write([new VerificationResult(identity, new Added())]);
        Assert.Null(log.Runs[0].Results[0].Locations[0].LogicalLocations);
    }

    [Fact]
    public void NullResultsThrow()
    {
        Assert.Throws<ArgumentNullException>(static () => SarifReportWriter.Write(null!));
    }

    [Fact]
    public void ToolDriverListsAllSixRulesInOrder()
    {
        SarifLog log = SarifReportWriter.Write([]);
        Assert.Equal(["EQ001", "EQ002", "EQ003", "EQ004", "EQ005", "EQ006"], log.Runs[0].Tool.Driver.Rules.Select(static r => r.Id), StringComparer.Ordinal);
    }

    [Fact]
    public void WithoutABaselineEveryResultIsNew()
    {
        SarifLog log = SarifReportWriter.Write([Fixtures.Result(new Equivalent(ProofMethod.Bounded))]);
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
    public void RuntimeChangedDivergentResultIsEQ006WithHelpUriAndReasonInTheMessage()
    {
        SarifLog log = SarifReportWriter.Write([Fixtures.Result(new Divergent(RuntimeChangedCounterexample()))]);
        Result result = log.Runs[0].Results[0];

        Assert.Equal("EQ006", result.RuleId);
        Assert.Equal(FailureLevel.Error, result.Level);
        Assert.Equal(ResultKind.Fail, result.Kind);
        Assert.Equal("https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0", result.GetProperty<string>("helpUri"));
        Assert.Contains("https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0", result.Message.Text, StringComparison.Ordinal);
        Assert.Contains("randomized", result.Message.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProofMethod.Bounded, "bounded")]
    [InlineData(ProofMethod.LockstepInduction, "lockstep-induction")]
    [InlineData(ProofMethod.KInduction, "k-induction")]
    public void EquivalentResultCarriesItsProofMethodAndNoBoundUnlessBounded(ProofMethod method, string name)
    {
        Result result = SarifReportWriter.Write([Fixtures.Result(new Equivalent(method))]).Runs[0].Results[0];

        Assert.Equal(name, result.GetProperty<string>("proofMethod"));
        Assert.False(result.TryGetProperty("boundedBy", out int _));
    }

    [Fact]
    public void BoundedEquivalentOverALoopCarriesBoundedBy()
    {
        Result result = SarifReportWriter.Write([Fixtures.Result(new Equivalent(ProofMethod.Bounded, BoundedBy: 3))]).Runs[0].Results[0];

        Assert.Equal(3, result.GetProperty<int>("boundedBy"));
    }

    [Theory]
    [InlineData(UnknownReason.Timeout, "timeout")]
    [InlineData(UnknownReason.Opaque, "opaque")]
    [InlineData(UnknownReason.UnmatchedOverload, "unmatched-overload")]
    [InlineData(UnknownReason.UnalignedLoop, "unaligned-loop")]
    [InlineData(UnknownReason.Recursion, "recursion")]
    public void UnknownResultCarriesItsReason(UnknownReason reason, string name)
    {
        Result result = SarifReportWriter.Write([Fixtures.Result(new Unknown(reason, "detail"))]).Runs[0].Results[0];

        Assert.Equal(name, result.GetProperty<string>("unknownReason"));
    }

    [Fact]
    public void ResultListsEveryLadderRungWithItsOutcome()
    {
        Verdict verdict = new Unknown(UnknownReason.UnalignedLoop, "detail")
        {
            Ladder =
            [
                new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, "no divergence up to 3"),
                new LadderStep(ProofMethod.LockstepInduction, RungOutcome.NotApplicable, "loop counts differ"),
                new LadderStep(ProofMethod.KInduction, RungOutcome.Timeout, "solver unknown"),
                new LadderStep(ProofMethod.KInduction, RungOutcome.Proved, "p"),
                new LadderStep(ProofMethod.Bounded, RungOutcome.Refuted, "r"),
            ],
        };

        Result result = SarifReportWriter.Write([Fixtures.Result(verdict)]).Runs[0].Results[0];

        List<Dictionary<string, string>> trace = result.GetProperty<List<Dictionary<string, string>>>("ladderTrace");
        Assert.Equal(["bounded", "lockstep-induction", "k-induction", "k-induction", "bounded"], trace.Select(static s => s["rung"]), StringComparer.Ordinal);
        Assert.Equal(["inconclusive", "not-applicable", "timeout", "proved", "refuted"], trace.Select(static s => s["outcome"]), StringComparer.Ordinal);
        Assert.Equal("loop counts differ", trace[1]["detail"]);
    }

    [Fact]
    public void ResultWithoutALadderHasNoLadderTrace()
    {
        Result result = SarifReportWriter.Write([Fixtures.Result(new Added())]).Runs[0].Results[0];

        Assert.False(result.TryGetProperty("ladderTrace", out List<Dictionary<string, string>> _));
    }

    private static Counterexample RuntimeChangedCounterexample()
    {
        CallIdentity flagged = new("System.String::GetHashCode()", RuntimeChanged: true);
        IrCallRecord record = new(flagged, []);
        return Fixtures.Counterexample() with { Old = Fixtures.Run() with { Trace = [record] } };
    }

    [Fact]
    public void EveryResultCarriesItsProcedureIdentityAndFingerprintAsPartialFingerprints()
    {
        VerificationResult result = Fixtures.Result(new Equivalent(ProofMethod.Bounded));
        SarifLog log = SarifReportWriter.Write([result]);
        Result sarifResult = log.Runs[0].Results[0];

        Assert.Equal(result.Identity.Value, sarifResult.PartialFingerprints[SarifReportWriter.ProcedureIdentityFingerprintId]);
        Assert.Equal(ResultFingerprint.Compute(result), sarifResult.PartialFingerprints[SarifReportWriter.ResultFingerprintId]);
    }

    [Fact]
    public void RunPropertiesGoIntoTheRunPropertyBagInOrder()
    {
        Dictionary<string, object> nested = new(StringComparer.Ordinal) { ["legacy"] = 3, ["modern"] = 4 };
        SarifLog log = SarifReportWriter.Write([], runProperties: new Dictionary<string, object>(StringComparer.Ordinal) { ["b"] = nested, ["a"] = 1 });

        Run run = log.Runs[0];
        Assert.Equal(["b", "a"], run.PropertyNames);
        Assert.True(run.TryGetSerializedPropertyValue("b", out string? serialized));
        Assert.Equal("""{"legacy":3,"modern":4}""", serialized);
        Assert.Equal(1, run.GetProperty<int>("a"));
    }

    [Fact]
    public void WithoutRunPropertiesTheRunHasNoPropertyBag() =>
        Assert.Empty(SarifReportWriter.Write([]).Runs[0].PropertyNames);

    [Fact]
    public void WrittenLogDeclaresSarif210AndRoundTripsThroughTheSdk()
    {
        SarifLog log = SarifReportWriter.Write(
        [
            Fixtures.Result(new Equivalent(ProofMethod.Bounded), "A"),
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
