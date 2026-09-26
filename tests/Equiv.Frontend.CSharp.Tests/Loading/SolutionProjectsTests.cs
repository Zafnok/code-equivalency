using System.Text.Json;

using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class SolutionProjectsTests : IDisposable
{
    private readonly BareFixture _fixture = new();

    [Fact]
    public void ASlnListsItsBuiltProjectsAsFullPathsAndNamesTheRest()
    {
        string solution = _fixture.Write(Path.Combine("sln", "side.sln"), SolutionBuildConfigurationTests.Solution(built: ["Lib", "Core"], notBuilt: ["Site"]));

        SolutionProjects projects = SolutionProjects.Read(solution);

        string directory = Path.GetDirectoryName(solution)!;
        Assert.Equal([Path.Combine(directory, "Lib", "Lib.csproj"), Path.Combine(directory, "Core", "Core.csproj")], projects.Built, StringComparer.Ordinal);
        Assert.Equal(["Site"], projects.NotBuilt, StringComparer.Ordinal);
    }

    [Fact]
    public void ASlnThatBuildsNothingOpensEverything()
    {
        string solution = _fixture.Write(Path.Combine("sln", "side.sln"), SolutionBuildConfigurationTests.Solution(built: [], notBuilt: ["Site"]));

        SolutionProjects projects = SolutionProjects.Read(solution);

        Assert.Equal([Path.Combine(Path.GetDirectoryName(solution)!, "Site", "Site.csproj")], projects.Built, StringComparer.Ordinal);
        Assert.Empty(projects.NotBuilt);
    }

    [Fact]
    public void ASlnxListsEveryProjectIncludingThoseInFolders()
    {
        string solution = _fixture.Write(Path.Combine("sln", "side.slnx"), """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src\Lib\Lib.csproj" />
              </Folder>
              <Project Path="App/App.csproj" />
              <Project />
            </Solution>
            """);

        SolutionProjects projects = SolutionProjects.Read(solution);

        string directory = Path.GetDirectoryName(solution)!;
        Assert.Equal([Path.Combine(directory, "src", "Lib", "Lib.csproj"), Path.Combine(directory, "App", "App.csproj")], projects.Built, StringComparer.Ordinal);
        Assert.Empty(projects.NotBuilt);
    }

    [Fact]
    public void TheFilterJsonNamesTheSolutionAndTheGivenProjects()
    {
        string solution = Path.Combine(_fixture.Root, "side.sln");

        using JsonDocument json = JsonDocument.Parse(SolutionBuildConfiguration.FilterJson(solution, ["/a/A.csproj", "/b/B.csproj"]));

        Assert.Equal(solution, json.RootElement.GetProperty("solution").GetProperty("path").GetString());
        Assert.Equal(["/a/A.csproj", "/b/B.csproj"], json.RootElement.GetProperty("solution").GetProperty("projects").EnumerateArray().Select(static p => p.GetString()!), StringComparer.Ordinal);
    }

    public void Dispose() => _fixture.Dispose();
}
