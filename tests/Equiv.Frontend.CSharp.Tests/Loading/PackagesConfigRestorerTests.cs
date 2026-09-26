using System.Collections.Immutable;

using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class PackagesConfigRestorerTests : IDisposable
{
    private readonly BareFixture _fixture = new();

    /// <summary>
    /// A <c>packages.config</c> copy of a sample in a temp directory, restored through the fake feed seam: each missing
    /// package lands in the folder its HintPath expects, one already there is not fetched, one no source has is
    /// reported, and a project-named <c>packages.&lt;project&gt;.config</c> wins over <c>packages.config</c>.
    /// </summary>
    [Fact]
    public async Task PackagesConfigRestoresThroughTheFeedSeam()
    {
        string web = _fixture.Write(Path.Combine("sln", "Web", "Web.csproj"), BareFixture.Project(string.Empty));
        _fixture.Write(Path.Combine("sln", "Web", "packages.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="Newtonsoft.Json" version="12.0.1" targetFramework="net48" />
              <package id="Present" version="1.0.0" targetFramework="net48" />
              <package id="Absent" version="2.0.0" targetFramework="net48" />
            </packages>
            """);
        string named = _fixture.Write(Path.Combine("sln", "Named", "Named.csproj"), BareFixture.Project(string.Empty));
        _fixture.Write(Path.Combine("sln", "Named", "packages.Named.config"), """<packages><package id="Named.Package" version="1.0.0" /><other /></packages>""");
        _fixture.Write(Path.Combine("sln", "Named", "packages.config"), """<packages><package id="Ignored" version="1.0.0" /></packages>""");
        string bare = _fixture.Write(Path.Combine("sln", "Bare", "Bare.csproj"), BareFixture.Project(string.Empty));
        _fixture.Write(Path.Combine("sln", "packages", "Present.1.0.0", "marker"), string.Empty);
        List<Uri> requested = [];
        PackageFeed feed = new(["https://feed.example/v3/index.json"], (uri, _) =>
        {
            requested.Add(uri);
            return Task.FromResult(uri.AbsolutePath switch
            {
                "/v3/index.json" => """{ "resources": [ { "@id": "https://feed.example/flat/", "@type": "PackageBaseAddress/3.0.0/rc" } ] }"""u8.ToArray(),
                "/flat/newtonsoft.json/12.0.1/newtonsoft.json.12.0.1.nupkg" => BareFixture.Package(("lib/net45/Newtonsoft.Json.dll", [1])),
                "/flat/named.package/1.0.0/named.package.1.0.0.nupkg" => BareFixture.Package(("lib/net45/Named.dll", [2])),
                _ => null,
            });
        });
        NuGetSettings settings = NuGetSettings.Read(Path.Combine(_fixture.Root, "sln"));

        ImmutableArray<LoadDiagnostic> diagnostics = await PackagesConfigRestorer.RestoreAsync([web, named, bare], settings, feed, TestContext.Current.CancellationToken);

        string packages = Path.Combine(_fixture.Root, "sln", "packages");
        Assert.True(File.Exists(Path.Combine(packages, "Newtonsoft.Json.12.0.1", "lib", "net45", "Newtonsoft.Json.dll")));
        Assert.True(File.Exists(Path.Combine(packages, "Newtonsoft.Json.12.0.1", "Newtonsoft.Json.12.0.1.nupkg")));
        Assert.True(File.Exists(Path.Combine(packages, "Named.Package.1.0.0", "lib", "net45", "Named.dll")));
        Assert.False(Directory.Exists(Path.Combine(packages, "Ignored.1.0.0")));
        Assert.DoesNotContain(requested, static u => u.AbsolutePath.Contains("present", StringComparison.Ordinal));
        LoadDiagnostic absent = Assert.Single(diagnostics);
        Assert.Equal(
            new LoadDiagnostic(LoadDiagnosticKind.WorkspaceWarning, string.Empty, "Web", "packages.config restore: package 'Absent' 2.0.0 was not found on any source (https://feed.example/v3/index.json)"),
            absent);
    }

    [Fact]
    public async Task APackageWithoutIdOrVersionIsLookedUpAsWritten()
    {
        string project = _fixture.Write(Path.Combine("sln", "P", "P.csproj"), BareFixture.Project(string.Empty));
        _fixture.Write(Path.Combine("sln", "P", "packages.config"), """<packages><package /></packages>""");
        PackageFeed feed = new([Path.Combine(_fixture.Root, "empty-feed")], BareFixture.NoNetwork);

        ImmutableArray<LoadDiagnostic> diagnostics = await PackagesConfigRestorer.RestoreAsync(
            [project],
            NuGetSettings.Read(Path.Combine(_fixture.Root, "sln")),
            feed,
            TestContext.Current.CancellationToken);

        Assert.Equal("packages.config restore: package '' ", Assert.Single(diagnostics).Message[..36]);
    }

    public void Dispose() => _fixture.Dispose();
}
