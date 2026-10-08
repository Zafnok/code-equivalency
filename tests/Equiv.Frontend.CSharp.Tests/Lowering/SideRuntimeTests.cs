using Equiv.Core;
using Equiv.Core.RuntimeChanges;
using Equiv.Frontend.CSharp.Loading;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// The runtime facts a pair's two bodies are lowered with (ADR 0040 decision 2; ticket P2-055): one interval for both
/// sides, from the runtimes of the two projects, and each side's own x87 flag.
/// </summary>
public sealed class SideRuntimeTests
{
    private static readonly RuntimeChangeTable Table = RuntimeChangeTable.Load();

    private static readonly Compilation AnyCpu = RoslynTestCompilations.Compile("class C { }");

    private static readonly Compilation X86 = AnyCpu.WithOptions(AnyCpu.Options.WithPlatform(Platform.X86));

    [Theory]
    [InlineData("net48", "net10.0", "net48", "net10.0")]
    [InlineData("net10.0", "net8.0", "net8.0", "net10.0")]
    [InlineData("net48,net8.0", "net6.0", "net48", "net8.0")]
    [InlineData("net6.0", "net48,net8.0", "net48", "net8.0")]
    [InlineData("net6.0,net8.0", "net7.0,net10.0", "net6.0", "net10.0")]
    public void TheIntervalRunsBetweenTheOldestAndTheNewestRuntimeOfEitherProject(string legacy, string modern, string older, string newer)
    {
        (SideRuntime legacySide, SideRuntime modernSide) = SideRuntime.Of(On(legacy), AnyCpu, On(modern), AnyCpu, Table);

        Assert.Equal(Runtimes.Interval(older, newer), legacySide.Interval);
        Assert.Same(legacySide.Interval, modernSide.Interval);
    }

    [Fact]
    public void ASameRuntimePairCrossesNothing()
    {
        (SideRuntime legacy, SideRuntime modern) = SideRuntime.Of(On("net10.0"), AnyCpu, On("net10.0"), AnyCpu, Table);

        Assert.True(legacy.Interval.IsEmpty);
        Assert.False(legacy.FloatToIntegerChanged);
        Assert.Equal(legacy, modern);
    }

    /// <summary>
    /// ADR 0040 decision 1: two unhosted projects with the same <c>netstandard</c> target framework are one runtime; any other
    /// pair with an unhosted project crosses every change the table covers.
    /// </summary>
    [Theory]
    [InlineData("netstandard2.0", "netstandard2.0", true)]
    [InlineData("netstandard2.0", "netstandard2.1", false)]
    [InlineData("unknown", "unknown", false)]
    [InlineData(".NETPortable,Version=v4.5", ".NETPortable,Version=v4.5", false)]
    public void TwoUnhostedProjectsAreOneRuntimeOnlyOnTheSameNetStandard(string legacy, string modern, bool same)
    {
        RuntimeInterval interval = SideRuntime.Of(Unhosted(legacy), AnyCpu, Unhosted(modern), AnyCpu, Table).Legacy.Interval;

        Assert.Equal(same, interval.IsEmpty);
        Assert.Equal(!same, interval == Table.Coverage);
    }

    [Fact]
    public void AnUnhostedProjectAgainstAHostedOneCrossesTheWholeCoverage()
    {
        Assert.Equal(Table.Coverage, SideRuntime.Of(Unhosted("netstandard2.0"), AnyCpu, On("net10.0"), AnyCpu, Table).Legacy.Interval);
        Assert.Equal(Table.Coverage, SideRuntime.Of(On("net48"), AnyCpu, Unhosted("netstandard2.0"), AnyCpu, Table).Modern.Interval);
        Assert.True(SideRuntime.Of(Unhosted("netstandard2.0"), AnyCpu, On("net10.0"), AnyCpu, Table).Legacy.FloatToIntegerChanged);
    }

    [Theory]
    [InlineData("net48", "net10.0", true)]
    [InlineData("net8.0", "net9.0", true)]
    [InlineData("net9.0", "net10.0", false)]
    [InlineData("net48", "net8.0", false)]
    public void FloatToIntegerChangesWhereThePairCrossesNet9(string legacy, string modern, bool expected) =>
        Assert.Equal(expected, Runtimes.Between(legacy, modern).FloatToIntegerChanged);

    /// <summary>
    /// A project whose runtime is not known, or that is hosted on .NET Framework and .NET both, may run on x87 but is not
    /// known to: it is flagged against a side that is not x87, and does not clear the other side's flag.
    /// </summary>
    [Fact]
    public void AProjectThatOnlyMayRunOnX87IsFlaggedAndClearsNothing()
    {
        Assert.Equal((true, true), Flags(SideRuntime.Of(Unhosted("unknown"), X86, Unhosted("unknown"), X86, Table)));
        Assert.Equal((false, false), Flags(SideRuntime.Of(Unhosted("unknown"), AnyCpu, Unhosted("unknown"), AnyCpu, Table)));
        Assert.Equal((true, false), Flags(SideRuntime.Of(On("net48,net8.0"), X86, On("net10.0"), X86, Table)));
        Assert.Equal((false, true), Flags(SideRuntime.Of(On("net48,net8.0"), X86, On("net48"), X86, Table)));
        Assert.Equal((false, true), Flags(SideRuntime.Of(Unhosted("netstandard2.0"), X86, On("net48"), X86, Table)));
        Assert.Equal((false, false), Flags(SideRuntime.Of(On("net8.0,net10.0"), X86, On("net10.0"), AnyCpu, Table)));
    }

    /// <summary>
    /// ADR 0053 decision 4 (ticket P1-030): <see cref="SideRuntime.OnX87"/> is the side's own fact, whatever the other side
    /// is. Two x87 sides are both on x87 and neither is flagged against the other; a side that only may be is on x87 too.
    /// </summary>
    [Fact]
    public void ASideIsOnX87WhateverTheOtherSideIs()
    {
        Assert.Equal((true, true), OnX87(SideRuntime.Of(On("net48"), X86, On("net48"), X86, Table)));
        Assert.Equal((false, false), Flags(SideRuntime.Of(On("net48"), X86, On("net48"), X86, Table)));
        Assert.Equal((true, false), OnX87(SideRuntime.Of(On("net48"), X86, On("net10.0"), X86, Table)));
        Assert.Equal((false, true), OnX87(SideRuntime.Of(On("net10.0"), X86, On("net48,net8.0"), X86, Table)));
        Assert.Equal((true, true), OnX87(SideRuntime.Of(On("net48,net8.0"), X86, On("net48"), X86, Table)));
        Assert.Equal((false, false), OnX87(SideRuntime.Of(On("net48"), AnyCpu, On("net48"), AnyCpu, Table)));
        Assert.True(new SideRuntime(Table.Coverage, X87: true).OnX87);
        Assert.False(new SideRuntime(Table.Coverage, X87: false).OnX87);
    }

    private static (bool Legacy, bool Modern) OnX87((SideRuntime Legacy, SideRuntime Modern) sides) => (sides.Legacy.OnX87, sides.Modern.OnX87);

    private static (bool Legacy, bool Modern) Flags((SideRuntime Legacy, SideRuntime Modern) sides) => (sides.Legacy.X87, sides.Modern.X87);

    /// <summary>A project that runs on <paramref name="runtimes"/>, comma-separated: its own, or its hosts'.</summary>
    internal static ProjectRuntime On(string runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        return new("P", [.. runtimes.Split(',').Select(static r => TargetRuntime.Parse(r)!)], "netstandard2.0", RuntimeDetection.Host);
    }

    internal static ProjectRuntime Unhosted(string declared) => new("P", [], declared, RuntimeDetection.Unhosted);
}
