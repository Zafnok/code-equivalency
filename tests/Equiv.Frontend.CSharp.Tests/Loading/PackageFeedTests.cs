using System.Text;

using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class PackageFeedTests : IDisposable
{
    private const string Index = "https://feed.example/v3/index.json";

    private static readonly byte[] ServiceIndex = Encoding.UTF8.GetBytes("""
        { "resources": [
            { "@id": "https://feed.example/search", "@type": "SearchQueryService" },
            { "@id": "https://feed.example/flat", "@type": "PackageBaseAddress/3.0.0" } ] }
        """);

    private readonly BareFixture _fixture = new();

    [Fact]
    public async Task AnHttpSourceIsReadThroughItsServiceIndexAndFlatContainer()
    {
        List<Uri> requested = [];
        byte[] package = [1, 2, 3];
        PackageFeed feed = new([Index], (uri, _) =>
        {
            requested.Add(uri);
            return Task.FromResult<byte[]?>(string.Equals(uri.AbsoluteUri, Index, StringComparison.Ordinal) ? ServiceIndex : package);
        });

        Assert.Same(package, await feed.DownloadAsync("Newtonsoft.Json", "12.0.1", TestContext.Current.CancellationToken));
        Assert.Same(package, await feed.DownloadAsync("Other", "1.0", TestContext.Current.CancellationToken));

        Assert.Equal(
            [Index, "https://feed.example/flat/newtonsoft.json/12.0.1/newtonsoft.json.12.0.1.nupkg", "https://feed.example/flat/other/1.0.0/other.1.0.0.nupkg"],
            requested.Select(static u => u.AbsoluteUri),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task SourcesAreTriedInOrderUntilOneHasThePackage()
    {
        string folder = Path.Combine(_fixture.Root, "feed");
        byte[] fromFolder = [4];
        _fixture.WriteBytes(Path.Combine("feed", "Some.Package.1.0.nupkg"), fromFolder);
        _fixture.WriteBytes(Path.Combine("feed", "other", "2.0.0", "other.2.0.0.nupkg"), [5]);
        List<string> requested = [];
        PackageFeed feed = new(
            ["https://down.example/index.json", "https://v2.example/api/v2", "https://broken.example/index.json", "https://missing.example/index.json", folder],
            (uri, _) =>
            {
                requested.Add(uri.Host);
                return uri.Host switch
                {
                    "down.example" => throw new HttpRequestException("down"),
                    "v2.example" => Task.FromResult<byte[]?>("<feed />"u8.ToArray()),
                    "broken.example" => Task.FromResult<byte[]?>("{ \"resources\": 1 }"u8.ToArray()),
                    _ => Task.FromResult<byte[]?>(null),
                };
            });

        Assert.Equal(fromFolder, await feed.DownloadAsync("Some.Package", "1.0", TestContext.Current.CancellationToken));
        Assert.Equal([5], await feed.DownloadAsync("Other", "2.0.0.0", TestContext.Current.CancellationToken));
        Assert.Null(await feed.DownloadAsync("Absent", "1.0.0", TestContext.Current.CancellationToken));
        Assert.Equal(["down.example", "v2.example", "broken.example", "missing.example", "down.example"], requested.Take(5), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1.0.0.0", "1.0.0")]
    [InlineData("1.0.0.1", "1.0.0.1")]
    [InlineData("01.002.3", "1.2.3")]
    [InlineData("2.0.0-Beta1+sha.1", "2.0.0-Beta1")]
    [InlineData(" 3 ", "3.0.0")]
    [InlineData("1.x", "1.x.0")]
    public void NormalizeVersionMatchesNuGet(string version, string expected) =>
        Assert.Equal(expected, PackageFeed.NormalizeVersion(version));

    [Fact]
    public void ExtractWritesThePackageFilesAndTheNupkgWithoutTheOpcParts()
    {
        byte[] package = BareFixture.Package(
            ("lib/net45/A.dll", [1]),
            ("content/My%20File.txt", [2]),
            ("package/services/metadata/core-properties/x.psmdcp", [3]),
            ("../escape.txt", [4]),
            ("emptydir/", []));
        string target = Path.Combine(_fixture.Root, "packages", "A.1.0.0");

        PackageFeed.Extract(package, target, "A.1.0.0.nupkg");

        Assert.Equal(
            ["A.1.0.0.nupkg", "content/My File.txt", "lib/net45/A.dll"],
            Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(target, f).Replace('\\', '/')).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.Equal(package, File.ReadAllBytes(Path.Combine(target, "A.1.0.0.nupkg")));
        Assert.False(File.Exists(Path.Combine(_fixture.Root, "packages", "escape.txt")));
    }

    public void Dispose() => _fixture.Dispose();
}
