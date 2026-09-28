using Equiv.Core.Execution;

using Xunit;

namespace Equiv.Execute.Tests;

public sealed class ReplayerTests
{
    private static readonly ReplayPlan Plan = ReplayPlan.Runnable(
        new ExecutionDrivers("legacy.exe", "modern.dll"), new ExecutionInput(["null"]), new ExecutionInput(["null", "1"]));

    private const string TraceSplit = "the call traces differ";

    private static ReplayResult Replay(string legacy, string modern, FakeHost? observed = null, ReplayPlan? plan = null)
    {
        FakeHost host = observed ?? new FakeHost((driver, _, _) => driver.EndsWith(".exe", StringComparison.Ordinal) ? legacy : modern);
        return new Replayer(host).Replay(plan ?? Plan);
    }

    [Fact]
    public void Replay_RunsEachSideOnceUnderTheInvariantCulture()
    {
        FakeHost host = new(static (_, _, _) => "[\"Returned\",1]");

        _ = Replay(string.Empty, string.Empty, host);

        Assert.Equal(["legacy.exe", "modern.dll"], host.Starts);
        Assert.Equal(
            [("legacy.exe", "[\"invariant\",null]"), ("modern.dll", "[\"invariant\",null,1]")],
            host.Exchanges.Select(static e => (e.Driver, e.Line)));
    }

    [Fact]
    public void DifferingOutcomes_Reproduce() =>
        Assert.Equal(
            ReplayResult.Reproduced,
            Replay("[\"Threw\",\"System.ArgumentNullException\"]", "[\"Threw\",\"System.NullReferenceException\"]"));

    [Fact]
    public void EqualOutcomes_DoNotReproduceAndCarryBoth()
    {
        ReplayResult result = Replay("[\"Returned\",\"a\"]", "[\"Returned\",\"a\"]");

        Assert.Equal(ReplayStatus.NotReproduced, result.Status);
        Assert.Equal((OutcomeKind.Returned, "\"a\""), (result.Legacy!.Kind, result.Legacy.Canonical));
        Assert.Equal((OutcomeKind.Returned, "\"a\""), (result.Modern!.Kind, result.Modern.Canonical));
        Assert.Equal("invariant", result.Legacy.Culture);
    }

    /// <summary>Ticket P2-037: equal outcomes are no evidence against a model whose call traces split before its outcomes differ.</summary>
    [Fact]
    public void EqualOutcomes_OfAModelWhoseTracesSplit_AreNotConstructible() =>
        Assert.Equal(
            ReplayResult.NotConstructible(TraceSplit),
            Replay("[\"Returned\",null]", "[\"Returned\",null]", plan: Plan with { AlikeReason = TraceSplit }));

    [Fact]
    public void DifferingOutcomes_OfAModelWhoseTracesSplit_StillReproduce() =>
        Assert.Equal(
            ReplayResult.Reproduced,
            Replay("[\"Threw\",\"System.Exception\"]", "[\"Returned\",null]", plan: Plan with { AlikeReason = TraceSplit }));

    [Theory]
    [InlineData("[\"NotComparable\",\"Odd.Holder\"]", "[\"Returned\",1]", "the legacy side gave NotComparable \"Odd.Holder\"")]
    [InlineData("[\"Returned\",1]", "[\"NotConstructible\",\"System.FormatException\"]", "the modern side gave NotConstructible \"System.FormatException\"")]
    public void ASideWithNoComparableOutcome_IsNotConstructible(string legacy, string modern, string reason) =>
        Assert.Equal(ReplayResult.NotConstructible(reason), Replay(legacy, modern));

    [Fact]
    public void BothSidesWithNoComparableOutcome_GiveTheLegacyReason() =>
        Assert.Equal(
            ReplayResult.NotConstructible("the legacy side gave NotComparable \"Odd.Holder\""),
            Replay("[\"NotComparable\",\"Odd.Holder\"]", "[\"NotConstructible\",\"System.FormatException\"]"));

    [Fact]
    public void Replay_RejectsANullPlan() =>
        Assert.Throws<ArgumentNullException>(() => new Replayer(new FakeHost(static (_, _, _) => null)).Replay(null!));

    [Fact]
    public void APlanWithoutDrivers_IsNotConstructibleWithItsReasonAndRunsNothing()
    {
        FakeHost host = new(static (_, _, _) => null);

        ReplayResult result = new Replayer(host).Replay(ReplayPlan.NotConstructible("emit-failed"));

        Assert.Equal(ReplayResult.NotConstructible("emit-failed"), result);
        Assert.Empty(host.Starts);
    }
}
