using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class ProjectPathTests : IDisposable
{
    private readonly BareFixture _fixture = new();

    [Fact]
    public void FullConvertsWindowsSeparatorsAndResolvesRelativePaths()
    {
        Assert.Equal(Path.Combine(_fixture.Root, "a", "b.cs"), ProjectPath.Full(_fixture.Root, @" a\x\..\b.cs "));
        Assert.Equal(Path.Combine(_fixture.Root, "b.cs"), ProjectPath.Full(Path.Combine(_fixture.Root, "other"), Path.Combine(_fixture.Root, "b.cs")));
    }

    [Fact]
    public void ResolveFindsAPathWrittenWithBackslashesAndTheWrongCase()
    {
        string file = _fixture.Write(Path.Combine("Lib", "Sub", "Thing.dll"), string.Empty);

        // Where the file system ignores case, the path comes back as written; elsewhere, as it is on disk.
        Assert.Equal(file, ProjectPath.Resolve(_fixture.Root, @"lib\SUB\thing.DLL"), ignoreCase: true);
        Assert.Equal(Path.Combine(_fixture.Root, "Lib"), ProjectPath.Resolve(_fixture.Root, "LIB"), ignoreCase: true);
    }

    [Fact]
    public void MatchIgnoringCaseReturnsThePathAsItIsOnDisk()
    {
        string file = _fixture.Write(Path.Combine("Lib", "Sub", "Thing.dll"), string.Empty);

        string? matched = ProjectPath.MatchIgnoringCase(Path.Combine(_fixture.Root, "LIB", "sub", "THING.dll"));

        Assert.Equal(file, matched, ignoreCase: true);
        Assert.EndsWith(Path.Combine("Lib", "Sub", "Thing.dll"), matched, StringComparison.Ordinal);
        Assert.Null(ProjectPath.MatchIgnoringCase(Path.Combine(_fixture.Root, "LIB", "sub", "Other.dll")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(@"Lib\missing.dll")]
    [InlineData(@"missing\Thing.dll")]
    [InlineData(@"Lib\Thing.dll\deeper")]
    public void ResolveIsNullForAPathThatDoesNotExist(string path)
    {
        _fixture.Write(Path.Combine("Lib", "Thing.dll"), string.Empty);

        Assert.Null(ProjectPath.Resolve(_fixture.Root, path));
    }

    [Theory]
    [InlineData("*.cs", false)]
    [InlineData("a?.cs", false)]
    [InlineData("a.cs", true)]
    public void IsWildcard(string spec, bool literal) => Assert.Equal(!literal, ProjectPath.IsWildcard(spec));

    [Theory]
    [InlineData("*.cs", "a.cs|b.cs")]
    [InlineData("**/*.cs", "a.cs|b.cs|sub/c.cs|sub/deep/d.cs")]
    [InlineData(@"sub\**\*.cs", "sub/c.cs|sub/deep/d.cs")]
    [InlineData(@"SUB\*.CS", "sub/c.cs")]
    [InlineData("?.cs", "a.cs|b.cs")]
    [InlineData("sub/**", "sub/c.cs|sub/deep/d.cs|sub/e.txt")]
    [InlineData("missing/*.cs", "")]
    [InlineData("a.cs/*.cs", "")]
    public void GlobMatchesMsBuildWildcards(string spec, string expected)
    {
        foreach (string file in (string[])["a.cs", "b.cs", "ab.cs.txt", "sub/c.cs", "sub/deep/d.cs", "sub/e.txt"])
        {
            _fixture.Write(Path.Combine("src", file), string.Empty);
        }

        string root = Path.Combine(_fixture.Root, "src");
        string[] matched = [.. ProjectPath.Glob(root, spec).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))];

        Assert.Equal(expected?.Split('|', StringSplitOptions.RemoveEmptyEntries) ?? [], matched, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose() => _fixture.Dispose();
}
