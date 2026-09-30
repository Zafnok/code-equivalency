using Xunit;

namespace Equiv.Core.Tests;

/// <summary>Ticket P2-054 acceptance criterion 4 (ADR 0040 decision 2).</summary>
public sealed class RuntimeIntervalTests
{
    private static TargetRuntime Runtime(string moniker) => TargetRuntime.Parse(moniker)!;

    private static RuntimeInterval Interval(string first, string second) => new(Runtime(first), Runtime(second));

    [Fact]
    public void FrameworkToCoreCrossesTheBoundary()
    {
        RuntimeInterval interval = Interval("net48", "net10.0");

        Assert.False(interval.IsEmpty);
        Assert.True(interval.Crosses(Runtime("netcoreapp1.0")));
        Assert.True(interval.Crosses(Runtime("net10.0")));
        Assert.False(interval.Crosses(Runtime("net48")));
        Assert.False(interval.Crosses(Runtime("net472")));
        Assert.False(Interval("net8.0", "net10.0").Crosses(Runtime("netcoreapp1.0")));
    }

    [Fact]
    public void OrderDoesNotMatter()
    {
        RuntimeInterval forward = Interval("net8.0", "net48");
        RuntimeInterval backward = Interval("net48", "net8.0");

        Assert.Equal(forward, backward);
        Assert.Equal(Runtime("net48"), forward.Older);
        Assert.Equal(Runtime("net8.0"), forward.Newer);
        Assert.True(Interval("net8.0", "net8.0").IsEmpty);
        Assert.False(Interval("net8.0", "net8.0").Crosses(Runtime("net8.0")));
    }

    [Fact]
    public void ReportsTheUncoveredRange()
    {
        TargetRuntime coveredFrom = Runtime("netcoreapp3.0");

        Assert.Equal(Interval("netcoreapp2.1", "netcoreapp3.0"), Interval("net8.0", "netcoreapp2.1").UncoveredRange(coveredFrom));
        Assert.Equal(Interval("netcoreapp2.1", "netcoreapp2.2"), Interval("netcoreapp2.1", "netcoreapp2.2").UncoveredRange(coveredFrom));
        Assert.Null(Interval("net48", "net10.0").UncoveredRange(coveredFrom));
        Assert.Null(Interval("netcoreapp3.0", "net10.0").UncoveredRange(coveredFrom));
        Assert.Null(Interval("netcoreapp2.1", "netcoreapp2.1").UncoveredRange(coveredFrom));
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        TargetRuntime net8 = Runtime("net8.0");
        RuntimeInterval interval = new(net8, net8);

        Assert.Throws<ArgumentNullException>(() => new RuntimeInterval(null!, net8));
        Assert.Throws<ArgumentNullException>(() => new RuntimeInterval(net8, null!));
        Assert.Throws<ArgumentNullException>(() => interval.Crosses(null!));
        Assert.Throws<ArgumentNullException>(() => interval.UncoveredRange(null!));
    }
}
