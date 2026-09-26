using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class NuGetSettingsTests : IDisposable
{
    private readonly BareFixture _fixture = new();

    [Fact]
    public void WithoutAConfigTheSourceIsNuGetOrgAndPackagesGoBesideTheSolution()
    {
        string solution = Path.Combine(_fixture.Root, "sln");

        NuGetSettings settings = NuGetSettings.Read(solution);

        Assert.Equal([NuGetSettings.NuGetOrg], settings.Sources);
        Assert.Equal(Path.Combine(solution, "packages"), settings.RepositoryPath);
    }

    [Fact]
    public void NuGetConfigSourcesAreHonoured()
    {
        _fixture.Write("NuGet.Config", """
            <configuration>
              <packageSources>
                <add key="outer" value="https://outer.example/v3/index.json" />
                <add key="mirror" value="https://old.example/v3/index.json" />
                <add key="gone" value="https://gone.example/v3/index.json" />
                <add key="reenabled" value="https://again.example/v3/index.json" />
              </packageSources>
              <disabledPackageSources><add key="reenabled" value="true" /></disabledPackageSources>
              <config><add key="repositoryPath" value="outer-packages" /></config>
            </configuration>
            """);
        _fixture.Write(Path.Combine("sln", "nuget.config"), """
            <configuration>
              <packageSources>
                <add key="mirror" value="https://mirror.example/v3/index.json" />
                <remove key="gone" />
                <add key="local" value="..\feed" />
                <add key="off" value="https://off.example/v3/index.json" />
                <add key="valueless" />
              </packageSources>
              <disabledPackageSources>
                <add key="off" value="true" />
                <add key="reenabled" value="false" />
                <clear />
              </disabledPackageSources>
              <config><add key="repositoryPath" value="..\lib" /><add key="other" value="x" /></config>
            </configuration>
            """);

        NuGetSettings settings = NuGetSettings.Read(Path.Combine(_fixture.Root, "sln"));

        Assert.Equal(
            ["https://outer.example/v3/index.json", "https://again.example/v3/index.json", "https://mirror.example/v3/index.json", Path.Combine(_fixture.Root, "feed")],
            settings.Sources,
            StringComparer.Ordinal);
        Assert.Equal(Path.Combine(_fixture.Root, "lib"), settings.RepositoryPath);
    }

    [Fact]
    public void ClearDropsTheSourcesAboveAndAnEmptyListFallsBackToNuGetOrg()
    {
        _fixture.Write("nuget.config", """<configuration><packageSources><add key="outer" value="https://outer.example/v3/index.json" /></packageSources></configuration>""");
        _fixture.Write(Path.Combine("sln", "nuget.config"), """<configuration><packageSources><clear /></packageSources><config><add key="repositoryPath" /></config></configuration>""");

        NuGetSettings settings = NuGetSettings.Read(Path.Combine(_fixture.Root, "sln"));

        Assert.Equal([NuGetSettings.NuGetOrg], settings.Sources);
        Assert.Equal(Path.Combine(_fixture.Root, "sln"), settings.RepositoryPath);
    }

    [Theory]
    [InlineData("https://api.nuget.org/v3/index.json", true)]
    [InlineData("HTTP://feed.example/index.json", true)]
    [InlineData(@"C:\feed", false)]
    [InlineData("/srv/feed", false)]
    public void IsRemote(string source, bool expected) => Assert.Equal(expected, NuGetSettings.IsRemote(source));

    public void Dispose() => _fixture.Dispose();
}
