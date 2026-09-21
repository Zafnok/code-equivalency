using CheckCoverage;

using Xunit;

namespace CheckCoverage.Tests;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void NoArgsUsesDefaults()
    {
        CommandLineOptions options = CommandLineOptions.Parse([]);

        Assert.Equal("TestResults", options.TestResultsDir);
        Assert.Equal("src", options.SrcDir);
    }

    [Fact]
    public void TestResultsFlagOverridesDefault()
    {
        CommandLineOptions options = CommandLineOptions.Parse(["--test-results", "custom-results"]);

        Assert.Equal("custom-results", options.TestResultsDir);
        Assert.Equal("src", options.SrcDir);
    }

    [Fact]
    public void SrcFlagOverridesDefault()
    {
        CommandLineOptions options = CommandLineOptions.Parse(["--src", "custom-src"]);

        Assert.Equal("TestResults", options.TestResultsDir);
        Assert.Equal("custom-src", options.SrcDir);
    }

    [Fact]
    public void BothFlagsCanBeCombinedInEitherOrder()
    {
        CommandLineOptions options = CommandLineOptions.Parse(["--src", "custom-src", "--test-results", "custom-results"]);

        Assert.Equal("custom-results", options.TestResultsDir);
        Assert.Equal("custom-src", options.SrcDir);
    }

    [Fact]
    public void FlagWithoutValueIsIgnored()
    {
        CommandLineOptions options = CommandLineOptions.Parse(["--test-results"]);

        Assert.Equal("TestResults", options.TestResultsDir);
    }

    [Fact]
    public void UnknownArgumentIsSkipped()
    {
        CommandLineOptions options = CommandLineOptions.Parse(["--unknown", "--src", "custom-src"]);

        Assert.Equal("custom-src", options.SrcDir);
    }
}
