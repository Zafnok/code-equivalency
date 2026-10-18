using Equiv.Core.Execution;

using Xunit;

namespace Equiv.Execute.Tests;

public sealed class RuntimeComparisonTests
{
    private static readonly ExecutionInput Input = new(["\"i\""]);

    private static ExecutionOutcome Returned(string canonical) => new(Input, "en-US", OutcomeKind.Returned, canonical);

    private static RunOutcomes Runs(params (ExecutionOutcome L1, ExecutionOutcome L2, ExecutionOutcome M1, ExecutionOutcome M2)[] cases) =>
        new([.. cases.Select(static c => c.L1)], [.. cases.Select(static c => c.L2)], [.. cases.Select(static c => c.M1)], [.. cases.Select(static c => c.M2)]);

    [Fact]
    public void AgreeingCasesAreNotFindings()
    {
        OverloadReport report = RuntimeComparison.Compare("M()", Runs((Returned("1"), Returned("1"), Returned("1"), Returned("1"))));

        Assert.Equal(("M()", 1, 0, 0, 0, 0, 0), (report.Member, report.CasesRun, report.Divergent, report.LegacyNondeterministic, report.ModernNondeterministic, report.BothNondeterministic, report.NotComparable));
        Assert.Empty(report.Witnesses);
        Assert.Empty(report.NotConstructible);
    }

    [Fact]
    public void DifferentOutcomesDivergeWithAtMostFiveWitnesses()
    {
        ExecutionOutcome threw = new(Input, "en-US", OutcomeKind.Threw, "\"System.ArgumentException\"");
        OverloadReport report = RuntimeComparison.Compare("M()", Runs([.. Enumerable.Repeat((Returned("1"), Returned("1"), threw, threw), 7)]));

        Assert.Equal(7, report.Divergent);
        Assert.Equal(RuntimeComparison.MaxWitnesses, report.Witnesses.Count);
        Assert.Equal(OutcomeKind.Returned, report.Witnesses[0].Legacy.Kind);
        Assert.Equal(OutcomeKind.Threw, report.Witnesses[0].Modern.Kind);
    }

    [Fact]
    public void SameCanonicalWithADifferentKindDiverges()
    {
        ExecutionOutcome threw = new(Input, "en-US", OutcomeKind.Threw, "1");

        Assert.Equal(1, RuntimeComparison.Compare("M()", Runs((Returned("1"), Returned("1"), threw, threw))).Divergent);
    }

    [Fact]
    public void NondeterminismIsCountedPerSideAndNeverDiverges()
    {
        OverloadReport report = RuntimeComparison.Compare("M()", Runs(
            (Returned("1"), Returned("2"), Returned("3"), Returned("3")),
            (Returned("1"), Returned("1"), Returned("5"), Returned("6")),
            (Returned("1"), Returned("1"), Returned("7"), Returned("8")),
            (Returned("1"), Returned("2"), Returned("3"), Returned("4"))));

        Assert.Equal(0, report.Divergent);
        Assert.Equal(1, report.LegacyNondeterministic);
        Assert.Equal(2, report.ModernNondeterministic);
        Assert.Equal(1, report.BothNondeterministic);
    }

    [Theory]
    [InlineData(OutcomeKind.NotComparable, OutcomeKind.Returned)]
    [InlineData(OutcomeKind.Returned, OutcomeKind.NotConstructible)]
    public void AnOutcomeWithoutACanonicalFormIsNotCompared(OutcomeKind legacy, OutcomeKind modern)
    {
        ExecutionOutcome l = new(Input, "ja-JP", legacy, "\"x\"");
        ExecutionOutcome m = new(Input, "ja-JP", modern, "\"y\"");

        OverloadReport report = RuntimeComparison.Compare("M()", Runs((l, l, m, m)));

        Assert.Equal(0, report.Divergent);
        Assert.Equal(1, report.NotComparable);
    }

    /// <summary>Ticket P2-242: two outcomes with no canonical form are never the same, whatever their text.</summary>
    [Theory]
    [InlineData(OutcomeKind.NotComparable)]
    [InlineData(OutcomeKind.NotConstructible)]
    public void TwoIncomparableOutcomesAreNeverTheSame(OutcomeKind kind)
    {
        ExecutionOutcome outcome = new(Input, "invariant", kind, "\"OpenRA.float2\"");

        Assert.False(RuntimeComparison.Same(outcome, outcome));
        Assert.True(RuntimeComparison.Identical(outcome, outcome));
    }

    /// <summary>Ticket P2-242: a side that gives the same incomparable outcome twice is deterministic, not nondeterministic.</summary>
    [Fact]
    public void RepeatedIncomparableOutcomesAreNotNondeterminism()
    {
        ExecutionOutcome outcome = new(Input, "invariant", OutcomeKind.NotComparable, "\"OpenRA.float2\"");

        OverloadReport report = RuntimeComparison.Compare("M()", Runs((outcome, outcome, outcome, outcome)));

        Assert.Equal((0, 0, 0, 0, 1), (report.Divergent, report.LegacyNondeterministic, report.ModernNondeterministic, report.BothNondeterministic, report.NotComparable));
    }

    [Fact]
    public void IdenticalNeedsTheSameKindAndText()
    {
        ExecutionOutcome threw = new(Input, "invariant", OutcomeKind.Threw, "1");

        Assert.False(RuntimeComparison.Identical(Returned("1"), threw));
        Assert.False(RuntimeComparison.Identical(Returned("1"), Returned("2")));
    }
}
