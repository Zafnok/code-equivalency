using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class MsBuildPropertiesTests
{
    [Fact]
    public void AGlobalPropertyCannotBeOverridden()
    {
        MsBuildProperties properties = Properties();

        properties.Set("configuration", new PropertyValue("Release"));
        properties.Set("Own", new PropertyValue("set"));

        Assert.Equal("Debug", properties.Read("Configuration"));
        Assert.Equal("set", properties.Read("OWN"));
    }

    [Fact]
    public void AnUnsetPropertyFallsBackToTheEnvironmentThenToEmpty()
    {
        MsBuildProperties properties = Properties();

        Assert.Equal("from environment", properties.Read("FromEnvironment"));
        Assert.Equal(string.Empty, properties.Read("Unset"));
    }

    [Fact]
    public void DefaultSetsOnlyAnEmptyProperty()
    {
        MsBuildProperties properties = Properties();
        properties.Set("Kept", new PropertyValue("mine"));
        properties.Set("Poisoned", PropertyValue.Poisoned("a construct"));

        properties.Default("Kept", "default");
        properties.Default("Empty", "default");
        properties.Default("Poisoned", "default");

        Assert.Equal("mine", properties.Read("Kept"));
        Assert.Equal("default", properties.Read("Empty"));
        Assert.Equal("a construct", Assert.Throws<UnsupportedConstructException>(() => properties.Read("Poisoned")).Construct);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    [InlineData("yes", false)]
    public void IsTrueReadsOnlyTrue(string value, bool expected)
    {
        MsBuildProperties properties = Properties();
        properties.Set("Flag", new PropertyValue(value));

        Assert.Equal(expected, properties.IsTrue("Flag"));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("$(Configuration)|$(Platform)", "Debug|AnyCPU")]
    [InlineData("$( Configuration )", "Debug")]
    [InlineData("a$(Unset)b", "ab")]
    [InlineData("$(Configuration", "$(Configuration")]
    [InlineData("x$(Configuration)y$(Configuration)z", "xDebugyDebugz")]
    [InlineData("$(FromEnvironment)", "from environment")]
    public void ExpandsProperties(string text, string expected)
    {
        PropertyValue value = Properties().Expand(text);

        Assert.Equal(new PropertyValue(expected), value);
    }

    [Theory]
    [InlineData("$([System.IO.Path]::Combine('a', 'b'))", "the property function $([System.IO.Path]::Combine('a', 'b'))")]
    [InlineData("$(Configuration.ToLower())", "the property function $(Configuration.ToLower())")]
    [InlineData("$(Registry:HKEY_LOCAL_MACHINE\\Software@Value)", "the registry property $(Registry:HKEY_LOCAL_MACHINE\\Software@Value)")]
    [InlineData("@(Compile)", "an item reference or metadata in '@(Compile)'")]
    [InlineData("%(Filename)", "an item reference or metadata in '%(Filename)'")]
    [InlineData("$(Poisoned)x", "a construct")]
    public void AnUnsupportedConstructPoisonsTheValue(string text, string construct)
    {
        MsBuildProperties properties = Properties();
        properties.Set("Poisoned", PropertyValue.Poisoned("a construct"));

        PropertyValue value = properties.Expand(text);

        Assert.Equal(PropertyValue.Poisoned(construct), value);
        Assert.Equal(construct, Assert.Throws<UnsupportedConstructException>(() => value.Exact).Construct);
    }

    [Theory]
    [InlineData("MSBuildToolsPath")]
    [InlineData("MSBuildExtensionsPath32")]
    [InlineData("VSToolsPath")]
    [InlineData("msbuildbinpath")]
    public void MsBuildsOwnToolPathsExpandToAToolPathValue(string name)
    {
        PropertyValue value = Properties().Expand($@"$({name})\Microsoft.CSharp.targets");

        Assert.True(value.ToolPath);
        Assert.Equal($@"MSBuild's tool path in '$({name})\Microsoft.CSharp.targets'", value.Unsupported);
        Assert.Equal(new PropertyValue(string.Empty, $"MSBuild's tool path $({name})", ToolPath: true), Properties()[name]);
    }

    [Fact]
    public void AToolPathThatAProjectSetsIsAnOrdinaryValue()
    {
        MsBuildProperties properties = Properties();
        properties.Set("VSToolsPath", new PropertyValue("custom"));

        Assert.Equal(new PropertyValue("custom"), properties.Expand("$(VSToolsPath)"));
    }

    [Fact]
    public void AnUnsupportedConstructOutranksAToolPath() =>
        Assert.Equal(
            PropertyValue.Poisoned("the property function $([MSBuild]::X())"),
            Properties().Expand("$(MSBuildToolsPath)$([MSBuild]::X())$(MSBuildBinPath)"));

    [Theory]
    [InlineData("a%3Bb", "a;b")]
    [InlineData("%25%24", "%$")]
    [InlineData("100%", "100%")]
    [InlineData("%zz", "%zz")]
    [InlineData("%4", "%4")]
    [InlineData("none", "none")]
    public void UnescapeDecodesMsBuildEscapes(string text, string expected) =>
        Assert.Equal(expected, MsBuildProperties.Unescape(text));

    private static MsBuildProperties Properties() => new(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Configuration"] = "Debug", ["Platform"] = "AnyCPU" },
        static name => string.Equals(name, "FromEnvironment", StringComparison.Ordinal) ? "from environment" : null);
}
