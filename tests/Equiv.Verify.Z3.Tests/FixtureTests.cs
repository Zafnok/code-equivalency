using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Every fixture under <c>Fixtures/</c> gets the verdict its first comment line names (ticket M3-001
/// criteria 3, 5, 8, 10 and 11), and every Divergent one replays to a divergence that reaches no opaque.
/// </summary>
public sealed class FixtureTests
{
    public static TheoryData<string> Names =>
    [
        "return-value", "out-param", "throw-vs-return", "exception-type", "call-order", "extra-call", "runtime-changed", "equivalent-refactor",
        "opaque-void-effect", "opaque-other-path",
        "heap-write-dropped", "heap-one-sided", "repeated-call",
        "hard-multiplication",
    ];

    [Theory]
    [MemberData(nameof(Names))]
    public void FixtureGetsTheVerdictItsFirstLineNames(string name)
    {
        Fixture fixture = Fixture.Load(name);

        Verdict verdict = Verify(fixture);

        Assert.Equal(fixture.Expected, Describe(verdict));
        if (verdict is Divergent divergent)
        {
            Assert.NotEqual(divergent.Counterexample.Old, divergent.Counterexample.New);
            Assert.IsNotType<IrOpaqueReached>(divergent.Counterexample.Old.Outcome);
            Assert.IsNotType<IrOpaqueReached>(divergent.Counterexample.New.Outcome);
        }
    }

    [Fact]
    public void EveryFixtureFileIsListed()
    {
        IEnumerable<string> files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.ir").Select(Path.GetFileNameWithoutExtension)!;

        Assert.Equal(files.Order(StringComparer.Ordinal), Names.Select(static row => row.Data).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RuntimeChangedCallIsDivergentWithRuleEq006()
    {
        Fixture fixture = Fixture.Load("runtime-changed");

        Divergent divergent = Assert.IsType<Divergent>(Verify(fixture));

        IrReturned oldResult = Assert.IsType<IrReturned>(divergent.Counterexample.Old.Outcome);
        IrReturned newResult = Assert.IsType<IrReturned>(divergent.Counterexample.New.Outcome);
        Assert.NotEqual(oldResult, newResult);
        string ruleId = SarifReportWriter.Write([new VerificationResult(fixture.Old.Identity, divergent)]).Runs[0].Results[0].RuleId;
        Assert.Equal("EQ006", ruleId);
    }

    [Fact]
    public void RepeatedCallReplaysWithDifferentResultsPerPosition()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Fixture.Load("repeated-call")));

        Assert.Equal(new IrReturned(new IrBoolValue(false)), divergent.Counterexample.Old.Outcome);
        Assert.Equal(new IrReturned(new IrBoolValue(true)), divergent.Counterexample.New.Outcome);
    }

    [Fact]
    public void HeapWriteDroppedDivergesOnTheFinalHeapOnly()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Fixture.Load("heap-write-dropped")));

        Assert.Equal(divergent.Counterexample.Old.Outcome, divergent.Counterexample.New.Outcome);
        Assert.NotEqual(divergent.Counterexample.Old.Outs, divergent.Counterexample.New.Outs);
    }

    [Fact]
    public void OpaqueDetailNamesTheReachableOpaque()
    {
        Unknown unknown = Assert.IsType<Unknown>(Verify(Fixture.Load("opaque-void-effect")));

        Assert.Equal(UnknownReason.Opaque, unknown.Reason);
        Assert.Equal("old: CompoundAssignment at T.cs 3:9", unknown.Detail);
    }

    /// <summary>A fixture that expects a timeout gets 50 ms; every other one gets ten seconds.</summary>
    private static Verdict Verify(Fixture fixture) =>
        new Z3Backend().Verify(fixture.Old, fixture.New, new VerificationOptions(3, string.Equals(fixture.Expected, "Unknown(Timeout)", StringComparison.Ordinal) ? 50 : 10_000, []));

    private static string Describe(Verdict verdict) => verdict switch
    {
        Unknown unknown => $"Unknown({unknown.Reason})",
        _ => verdict.GetType().Name,
    };
}
