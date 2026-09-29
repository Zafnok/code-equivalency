using Xunit;

using static Equiv.Core.TargetRuntime.RuntimeFamily;

namespace Equiv.Core.Tests;

/// <summary>Ticket P2-053 acceptance criterion 1 (ADR 0040 decisions 1 and 2).</summary>
public sealed class TargetRuntimeTests
{
    private static TargetRuntime Runtime(TargetRuntime.RuntimeFamily family, string version) => new(family, Version.Parse(version));

    [Fact]
    public void FrameworkPrecedesCore()
    {
        TargetRuntime net48 = Runtime(NetFramework, "4.8");
        TargetRuntime net472 = Runtime(NetFramework, "4.7.2");
        TargetRuntime core31 = Runtime(NetCore, "3.1");
        TargetRuntime net8 = Runtime(NetCore, "8.0");
        TargetRuntime net10 = Runtime(NetCore, "10.0");

        TargetRuntime[] sorted = [.. new[] { net10, core31, net48, net8, net472 }.Order()];

        Assert.Equal([net472, net48, core31, net8, net10], sorted);
        Assert.True(net48 < core31);
        Assert.True(net10 > net48);
        Assert.True(net8 <= Runtime(NetCore, "8.0"));
        Assert.True(net8 >= Runtime(NetCore, "8.0"));
        Assert.False(net8 < Runtime(NetCore, "8.0"));
        Assert.Equal(0, net8.CompareTo(Runtime(NetCore, "8.0")));
    }

    [Fact]
    public void NullOrdersFirst()
    {
        TargetRuntime net48 = Runtime(NetFramework, "4.8");
        TargetRuntime? none = null;
        TargetRuntime? alsoNone = null;

        Assert.Equal(1, net48.CompareTo(other: null));
        Assert.True(none < net48);
        Assert.False(net48 < none);
        Assert.True(none <= alsoNone);
        Assert.True(none >= alsoNone);
        Assert.False(none > net48);
    }

    [Theory]
    [InlineData(".NETFramework,Version=v4.8", "net48")]
    [InlineData(".NETFramework,Version=v4.7.2", "net472")]
    [InlineData(".NETFramework,Version=v4.0,Profile=Client", "net40")]
    [InlineData(".NETCoreApp,Version=v8.0", "net8.0")]
    [InlineData(".NETCoreApp,Version=v3.1", "netcoreapp3.1")]
    [InlineData(" .NETCoreApp , Version=v10.0 ", "net10.0")]
    [InlineData("net48", "net48")]
    [InlineData("NET472", "net472")]
    [InlineData("net8.0", "net8.0")]
    [InlineData("net8.0-windows", "net8.0")]
    [InlineData("netcoreapp3.1", "netcoreapp3.1")]
    [InlineData("net10.0", "net10.0")]
    public void ParsesMonikersAndShortNames(string text, string shortName)
    {
        TargetRuntime? runtime = TargetRuntime.Parse(text);

        Assert.NotNull(runtime);
        Assert.Equal(shortName, runtime.ToString());
        Assert.Equal(runtime, TargetRuntime.Parse(shortName));
    }

    [Theory]
    [InlineData(".NETStandard,Version=v2.0")]
    [InlineData(".NETFramework")]
    [InlineData(".NETFramework,Profile=Client")]
    [InlineData(".NETCoreApp,Version=vX")]
    [InlineData("netstandard2.0")]
    [InlineData("netcoreappX")]
    [InlineData("net4.8")]
    [InlineData("net4x.0")]
    [InlineData("net5")]
    [InlineData("net4812")]
    [InlineData("net4a")]
    [InlineData("uap10.0")]
    [InlineData("")]
    public void RejectsWhatIsNeitherFrameworkNorCore(string text) => Assert.Null(TargetRuntime.Parse(text));

    [Fact]
    public void ParseRejectsNull() => Assert.Throws<ArgumentNullException>(static () => TargetRuntime.Parse(null!));
}
