using Equiv.Core.Execution;

using Xunit;

namespace Equiv.Execute.Tests;

public sealed class ReplayerTests
{
    private static readonly ReplayPlan Plan = ReplayPlan.Runnable(
        new ExecutionDrivers("legacy.exe", "modern.dll"), new ExecutionInput(["null"]), new ExecutionInput(["null", "1"]));

    private static ReplayResult Replay(string legacy, string modern, FakeHost? observed = null)
    {
        FakeHost host = observed ?? new FakeHost((driver, _, _) => driver.EndsWith(".exe", StringComparison.Ordinal) ? legacy : modern);
        return new Replayer(host).Replay(Plan);
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

    [Theory]
    [InlineData("[\"NotComparable\",\"Odd.Holder\"]", "[\"Returned\",1]", "the legacy side gave NotComparable \"Odd.Holder\"")]
    [InlineData("[\"Returned\",1]", "[\"NotConstructible\",\"System.FormatException\"]", "the modern side gave NotConstructible \"System.FormatException\"")]
    public void ASideWithNoComparableOutcome_IsNotConstructible(string legacy, string modern, string reason) =>
        Assert.Equal(ReplayResult.NotConstructible(reason), Replay(legacy, modern));

    [Fact]
    public void APlanWithoutDrivers_IsNotConstructibleWithItsReasonAndRunsNothing()
    {
        FakeHost host = new(static (_, _, _) => null);

        ReplayResult result = new Replayer(host).Replay(ReplayPlan.NotConstructible("emit-failed"));

        Assert.Equal(ReplayResult.NotConstructible("emit-failed"), result);
        Assert.Empty(host.Starts);
    }

    /// <summary>Ticket M5-002's <c>probe</c>: both outcomes come back even when they differ, under the given culture.</summary>
    [Fact]
    public void Run_ReturnsBothOutcomesUnderTheGivenCulture()
    {
        FakeHost host = new(static (driver, _, _) => driver.EndsWith(".exe", StringComparison.Ordinal) ? "[\"Returned\",1]" : "[\"Returned\",2]");

        (ExecutionOutcome legacy, ExecutionOutcome modern) = new Replayer(host).Run(Plan, "fr-FR");

        Assert.Equal((OutcomeKind.Returned, "1"), (legacy.Kind, legacy.Canonical));
        Assert.Equal((OutcomeKind.Returned, "2"), (modern.Kind, modern.Canonical));
        Assert.Equal(["[\"fr-FR\",null]", "[\"fr-FR\",null,1]"], host.Exchanges.Select(static e => e.Line), StringComparer.Ordinal);
    }

    [Fact]
    public void Run_APlanWithoutDrivers_Throws()
    {
        FakeHost host = new(static (_, _, _) => null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new Replayer(host).Run(ReplayPlan.NotConstructible("emit-failed"), "invariant"));

        Assert.Contains("emit-failed", exception.Message, StringComparison.Ordinal);
        Assert.Empty(host.Starts);
    }

    [Fact]
    public void Run_RejectsNulls()
    {
        Replayer replayer = new(new FakeHost(static (_, _, _) => null));

        Assert.Throws<ArgumentNullException>(() => replayer.Run(null!, "invariant"));
        Assert.Throws<ArgumentNullException>(() => replayer.Run(Plan, null!));
    }
}
