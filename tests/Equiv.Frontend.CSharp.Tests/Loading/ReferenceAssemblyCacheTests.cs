using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class ReferenceAssemblyCacheTests : IDisposable
{
    private readonly BareFixture _fixture = new();

    [Fact]
    public async Task ReferenceAssembliesAreFetchedOnceAndCached()
    {
        List<Uri> requested = [];
        PackageFeed feed = new(["https://feed.example/v3/index.json"], (uri, _) =>
        {
            requested.Add(uri);
            return Task.FromResult<byte[]?>(uri.AbsolutePath.EndsWith("index.json", StringComparison.Ordinal)
                ? """{ "resources": [ { "@id": "https://feed.example/flat/", "@type": "PackageBaseAddress/3.0.0" } ] }"""u8.ToArray()
                : BareFixture.ReferenceAssemblyPackage("v4.7.2"));
        });
        ReferenceAssemblyCache cache = new(ReferenceAssemblyCache.DefaultRoot(_fixture.Variable));

        string? first = await cache.DirectoryAsync("v4.7.2", feed, TestContext.Current.CancellationToken);
        string? second = await cache.DirectoryAsync("v4.7.2", feed, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_fixture.ReferenceAssemblies, ".NETFramework", "v4.7.2"), first);
        Assert.Equal(first, second);
        Assert.True(File.Exists(Path.Combine(first!, "mscorlib.dll")));
        Assert.Equal(
            ["https://feed.example/v3/index.json", "https://feed.example/flat/microsoft.netframework.referenceassemblies.net472/1.0.3/microsoft.netframework.referenceassemblies.net472.1.0.3.nupkg"],
            requested.Select(static u => u.AbsoluteUri),
            StringComparer.Ordinal);
        Assert.Equal([".NETFramework"], Directory.EnumerateFileSystemEntries(_fixture.ReferenceAssemblies).Select(Path.GetFileName), StringComparer.Ordinal);
    }

    [Fact]
    public async Task NothingIsFetchedWhenTheCacheAlreadyHoldsTheVersion()
    {
        string held = _fixture.Framework("v4.8");
        ReferenceAssemblyCache cache = new(_fixture.ReferenceAssemblies);
        PackageFeed feed = new(["https://feed.example/v3/index.json"], static (uri, _) => throw new InvalidOperationException($"fetched {uri}"));

        Assert.Equal(held, await cache.DirectoryAsync("v4.8", feed, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AVersionNoSourceHasIsNull()
    {
        ReferenceAssemblyCache cache = new(_fixture.ReferenceAssemblies);

        Assert.Null(await cache.DirectoryAsync("v3.0", new PackageFeed([Path.Combine(_fixture.Root, "feed")], BareFixture.NoNetwork), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APackageWithoutTheVersionsFolderIsNullAndLeavesNothingBehind()
    {
        _fixture.WriteBytes(Path.Combine("feed", "Microsoft.NETFramework.ReferenceAssemblies.net48.1.0.3.nupkg"), BareFixture.ReferenceAssemblyPackage("v4.7.2"));
        ReferenceAssemblyCache cache = new(_fixture.ReferenceAssemblies);

        Assert.Null(await cache.DirectoryAsync("v4.8", new PackageFeed([Path.Combine(_fixture.Root, "feed")], BareFixture.NoNetwork), TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_fixture.ReferenceAssemblies));
    }

    [Fact]
    public void TheCacheRootIsTheEnvironmentVariableElseUnderLocalApplicationData()
    {
        Assert.Equal("/custom/cache", ReferenceAssemblyCache.DefaultRoot(static name => string.Equals(name, ReferenceAssemblyCache.RootVariable, StringComparison.Ordinal) ? "/custom/cache" : null));
        Assert.EndsWith(Path.Combine("equiv", "reference-assemblies"), ReferenceAssemblyCache.DefaultRoot(static name => string.Equals(name, ReferenceAssemblyCache.RootVariable, StringComparison.Ordinal) ? string.Empty : null), StringComparison.Ordinal);
    }

    public void Dispose() => _fixture.Dispose();
}
