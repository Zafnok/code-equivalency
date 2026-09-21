using LicenceCheck;

using Xunit;

namespace LicenceCheck.Tests;

public sealed class ArgsParserTests
{
    [Fact]
    public void DefaultsToCurrentDirectoryAndNoFix()
    {
        CliArgs args = ArgsParser.Parse([]);

        Assert.Equal(".", args.RepoRoot);
        Assert.False(args.Fix);
    }

    [Fact]
    public void ParsesRepoRoot()
    {
        CliArgs args = ArgsParser.Parse(["--repo-root", "/some/path"]);

        Assert.Equal("/some/path", args.RepoRoot);
        Assert.False(args.Fix);
    }

    [Fact]
    public void ParsesFix()
    {
        CliArgs args = ArgsParser.Parse(["--fix"]);

        Assert.True(args.Fix);
    }

    [Fact]
    public void ParsesBothInEitherOrder()
    {
        CliArgs args = ArgsParser.Parse(["--fix", "--repo-root", "/x"]);

        Assert.Equal("/x", args.RepoRoot);
        Assert.True(args.Fix);
    }

    [Fact]
    public void IgnoresARepoRootFlagWithNoFollowingValue()
    {
        CliArgs args = ArgsParser.Parse(["--repo-root"]);

        Assert.Equal(".", args.RepoRoot);
    }

    [Fact]
    public void IgnoresUnknownArguments()
    {
        CliArgs args = ArgsParser.Parse(["--bogus", "--fix"]);

        Assert.True(args.Fix);
    }
}
