using System.Text.Json;

using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class ProjectAssetsTests : IDisposable
{
    private const string Framework = ".NETFramework,Version=v4.8";

    private static readonly string[] FrameworkAssemblies = ["System.Net.Http", "System.Numerics"];

    private readonly BareFixture _fixture = new();

    [Fact]
    public void CompileAssetsAndFrameworkAssembliesComeFromTheTargetForTheFramework()
    {
        string first = Path.Combine(_fixture.Root, "first");
        string second = Path.Combine(_fixture.Root, "second");
        _fixture.Write(Path.Combine("second", "web", "5.3.0", "lib", "net45", "Web.dll"), string.Empty);

        ProjectAssets assets = ProjectAssets.Read(Json(first, second), Framework);

        Assert.Equal(
            [Path.Combine(second, "web", "5.3.0", "lib", "net45", "Web.dll"), Path.Combine(first, "json", "12.0.1", "lib", "net45", "Json.dll")],
            assets.CompileAssets,
            StringComparer.Ordinal);
        Assert.Equal(["System.Net.Http", "System.Numerics"], assets.FrameworkAssemblies, StringComparer.Ordinal);
    }

    [Fact]
    public void TheTargetIsMatchedIgnoringCase() =>
        Assert.NotEmpty(ProjectAssets.Read(Json(_fixture.Root, _fixture.Root), ".netframework,version=V4.8").CompileAssets);

    [Theory]
    [InlineData(".NETFramework,Version=v4.7.2")]
    [InlineData("Empty")]
    public void AMissingTargetIsAnError(string framework) =>
        Assert.Equal(
            $"project.assets.json has no target for '{framework}'",
            Assert.Throws<KeyNotFoundException>(() => ProjectAssets.Read(Json(_fixture.Root, _fixture.Root), framework)).Message);

    [Fact]
    public void MalformedJsonIsAnError() =>
        Assert.ThrowsAny<JsonException>(() => ProjectAssets.Read("{", Framework));

    public void Dispose() => _fixture.Dispose();

    private static string Json(string firstFolder, string secondFolder) => JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["version"] = 3,
        ["targets"] = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Empty"] = "not an object",
            [Framework] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["Web/5.3.0"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = "package",
                    ["compile"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["lib/net45/Web.dll"] = new { }, ["lib/net45/_._"] = new { } },
                    ["frameworkAssemblies"] = FrameworkAssemblies,
                },
                ["Json/12.0.1"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = "package",
                    ["compile"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["lib/net45/Json.dll"] = new { } },
                },
                ["Meta/1.0.0"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["type"] = "package" },
                ["Lib/1.0.0"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["type"] = "project", ["compile"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["bin/Lib.dll"] = new { } } },
                ["Untyped/1.0.0"] = new Dictionary<string, object>(StringComparer.Ordinal),
            },
        },
        ["libraries"] = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Web/5.3.0"] = new { path = "web/5.3.0" },
            ["Json/12.0.1"] = new { path = "json/12.0.1" },
            ["Meta/1.0.0"] = new { path = "meta/1.0.0" },
        },
        ["packageFolders"] = new Dictionary<string, object>(StringComparer.Ordinal) { [firstFolder] = new { }, [secondFolder + Path.DirectorySeparatorChar] = new { } },
    });
}
