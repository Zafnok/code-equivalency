using System.Collections.Immutable;
using System.Linq;

using CsCheck;

using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Core.Tests.Reporting;

/// <summary>
/// Unit cases for each <see cref="BaselineState"/> value, plus the property test
/// VERIFICATION-MODEL.md section 6/QUALITY-GATES.md's test taxonomy calls out: baselining a
/// report against itself yields <see cref="BaselineState.Unchanged"/> for every result.
/// </summary>
public sealed class BaselineComputerTests
{
    private static readonly ImmutableArray<Verdict> VerdictPool =
    [
        new Equivalent(),
        new Added(),
        new Removed(),
        new Unknown(UnknownReason.Timeout, "d1"),
        new Unknown(UnknownReason.Opaque, "d2"),
        new Divergent(Fixtures.Counterexample(1, 2)),
        new Divergent(Fixtures.Counterexample(3, 4)),
    ];

    private static readonly Gen<VerificationResult> ResultGen =
        Gen.Select(Gen.OneOfConst("A", "B", "C", "D", "E"), Gen.Int[0, VerdictPool.Length - 1])
            .Select(static t => new VerificationResult(new ProcedureIdentity(t.Item1), VerdictPool[t.Item2]));

    private static readonly Gen<ImmutableArray<VerificationResult>> ResultSet =
        ResultGen.Array[0, 8].Select(static a => DistinctByIdentity(a));

    [Fact]
    public void WithNoBaselineEveryResultIsNew()
    {
        SarifLog log = SarifReportWriter.Write([Fixtures.Result(new Equivalent())]);
        Assert.Equal(BaselineState.New, log.Runs[0].Results[0].BaselineState);
    }

    [Fact]
    public void AnIdentityNotInThePreviousLogIsNew()
    {
        SarifLog previous = SarifReportWriter.Write([Fixtures.Result(new Equivalent(), "A")]);
        SarifLog current = SarifReportWriter.Write([Fixtures.Result(new Equivalent(), "B")], previous);

        Result result = Assert.Single(current.Runs[0].Results, static r => r.BaselineState != BaselineState.Absent);
        Assert.Equal(BaselineState.New, result.BaselineState);
    }

    [Fact]
    public void TheSameIdentityAndVerdictIsUnchanged()
    {
        VerificationResult result = Fixtures.Result(new Equivalent());
        SarifLog previous = SarifReportWriter.Write([result]);
        SarifLog current = SarifReportWriter.Write([result], previous);

        Assert.Equal(BaselineState.Unchanged, current.Runs[0].Results[0].BaselineState);
    }

    [Fact]
    public void TheSameIdentityWithADifferentVerdictKindIsNew()
    {
        // A rule-id (verdict-kind) change for the same identity is a regression such as
        // Equivalent -> Divergent and must never be hidden from the exit code as "updated" —
        // see BaselineComputer's remarks.
        ProcedureIdentity identity = new("A");
        SarifLog previous = SarifReportWriter.Write([new VerificationResult(identity, new Equivalent())]);
        SarifLog current = SarifReportWriter.Write([new VerificationResult(identity, new Added())], previous);

        Assert.Equal(BaselineState.New, current.Runs[0].Results[0].BaselineState);
    }

    [Fact]
    public void TheSameIdentityAndVerdictKindWithADifferentPayloadIsUpdated()
    {
        // Same rule id (both Divergent, EQ002) but a different counterexample: this is the case
        // that stays "updated" rather than "new", and is deliberately not exit-code-significant.
        ProcedureIdentity identity = new("A");
        SarifLog previous = SarifReportWriter.Write([new VerificationResult(identity, new Divergent(Fixtures.Counterexample(1, 2)))]);
        SarifLog current = SarifReportWriter.Write([new VerificationResult(identity, new Divergent(Fixtures.Counterexample(3, 4)))], previous);

        Assert.Equal(BaselineState.Updated, current.Runs[0].Results[0].BaselineState);
    }

    [Fact]
    public void AnIdentityMissingFromTheCurrentRunBecomesAnAbsentCarryOver()
    {
        VerificationResult result = Fixtures.Result(new Equivalent(), "A");
        SarifLog previous = SarifReportWriter.Write([result]);
        SarifLog current = SarifReportWriter.Write([], previous);

        Result absent = Assert.Single(current.Runs[0].Results);
        Assert.Equal(BaselineState.Absent, absent.BaselineState);
        Assert.Equal("EQ001", absent.RuleId);
        Assert.Equal("A", absent.PartialFingerprints[SarifReportWriter.ProcedureIdentityFingerprintId]);
    }

    [Fact]
    public void BaselineAgainstItselfIsAllUnchanged()
    {
        ResultSet.Sample(static results =>
        {
            SarifLog baseline = SarifReportWriter.Write(results);
            SarifLog current = SarifReportWriter.Write(results, baseline);

            Assert.Equal(results.Length, current.Runs[0].Results.Count);
            Assert.All(current.Runs[0].Results, static r => Assert.Equal(BaselineState.Unchanged, r.BaselineState));
        }, iter: 500);
    }

    private static ImmutableArray<VerificationResult> DistinctByIdentity(VerificationResult[] results) =>
        [.. results.GroupBy(static r => r.Identity.Value, StringComparer.Ordinal).Select(static g => g.First())];
}
