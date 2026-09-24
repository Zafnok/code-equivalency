using System.Text;
using System.Text.Json;

using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class SolutionBuildConfigurationTests
{
    private const string SolutionFolderType = "2150E333-8FDC-42A3-9474-1A3956D46DE8";
    private const string CSharpType = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";

    private static string SolutionPath => Path.Combine(Path.GetTempPath(), "side", "side.sln");

    [Fact]
    public void Filter_ListsTheBuiltProjectsAsAbsolutePathsAndNamesTheRest()
    {
        SolutionFilter filter = SolutionBuildConfiguration.Filter(SolutionPath, Solution(built: ["Lib", "Core"], notBuilt: ["Site", "_build"]))!;

        Assert.Equal(["Site", "_build"], filter.NotBuilt);
        using JsonDocument json = JsonDocument.Parse(filter.Json);
        JsonElement solution = json.RootElement.GetProperty("solution");
        Assert.Equal(SolutionPath, solution.GetProperty("path").GetString());
        string directory = Path.GetDirectoryName(SolutionPath)!;
        Assert.Equal(
            [Path.Combine(directory, "Lib", "Lib.csproj"), Path.Combine(directory, "Core", "Core.csproj")],
            solution.GetProperty("projects").EnumerateArray().Select(static p => p.GetString()!),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Filter_IsNullWhenEveryProjectIsBuilt() =>
        Assert.Null(SolutionBuildConfiguration.Filter(SolutionPath, Solution(built: ["Lib", "Core"], notBuilt: [])));

    [Fact]
    public void Filter_IsNullWhenNoProjectIsBuilt() =>
        Assert.Null(SolutionBuildConfiguration.Filter(SolutionPath, Solution(built: [], notBuilt: ["Lib"])));

    [Fact]
    public void Filter_IsNullWithoutSolutionText() =>
        Assert.Null(SolutionBuildConfiguration.Filter(SolutionPath, solutionText: null));

    [Fact]
    public void Filter_CountsEveryProjectAsBuiltWhenTheSolutionListsNoConfiguration() =>
        Assert.Null(SolutionBuildConfiguration.Filter(SolutionPath, Projects("Lib", "Site") + "Global\r\nEndGlobal\r\n"));

    [Fact]
    public void Filter_IgnoresSolutionFolders()
    {
        string text = Solution(built: ["Lib"], notBuilt: [])
            .Replace("Global\r\n", $"Project(\"{{{SolutionFolderType}}}\") = \"docs\", \"docs\", \"{{{ProjectGuid("docs")}}}\"\r\nEndProject\r\nGlobal\r\n", StringComparison.Ordinal);

        Assert.Null(SolutionBuildConfiguration.Filter(SolutionPath, text));
    }

    [Fact]
    public void Filter_PrefersDebugAnyCpuOverTheFirstListedConfiguration()
    {
        string text = Projects("Lib", "Site") + Global(
            ["Release|Any CPU", "Debug|Any CPU"],
            [(ProjectGuid("Lib"), "Debug|Any CPU"), (ProjectGuid("Site"), "Release|Any CPU")]);

        Assert.Equal(["Site"], SolutionBuildConfiguration.Filter(SolutionPath, text)!.NotBuilt);
    }

    [Fact]
    public void Filter_UsesTheFirstListedConfigurationWithoutDebugAnyCpu()
    {
        string text = Projects("Lib", "Site") + Global(
            ["Release|x64", "Debug|x64"],
            [(ProjectGuid("Lib"), "Release|x64"), (ProjectGuid("Site"), "Debug|x64")]);

        Assert.Equal(["Site"], SolutionBuildConfiguration.Filter(SolutionPath, text)!.NotBuilt);
    }

    /// <summary>
    /// A <c>.sln</c> whose projects <c>&lt;Name&gt;\&lt;Name&gt;.csproj</c> are all active in <c>Debug|Any CPU</c> and
    /// <c>Release|Any CPU</c>, with a <c>Build.0</c> entry for both only for the <paramref name="built"/> ones.
    /// </summary>
    internal static string Solution(string[] built, string[] notBuilt)
    {
        string[] configurations = ["Debug|Any CPU", "Release|Any CPU"];
        return Projects([.. built, .. notBuilt]) + Global(
            configurations,
            [.. built.SelectMany(p => configurations.Select(c => (ProjectGuid(p), c)))],
            [.. notBuilt.Select(ProjectGuid)]);
    }

    private static string Projects(params string[] names)
    {
        StringBuilder text = new("Microsoft Visual Studio Solution File, Format Version 12.00\r\n");
        foreach (string name in names)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"Project(\"{{{CSharpType}}}\") = \"{name}\", \"{name}\\{name}.csproj\", \"{{{ProjectGuid(name)}}}\"\r\nEndProject\r\n");
        }

        return text.ToString();
    }

    private static string Global(string[] configurations, (string Guid, string Configuration)[] builds, string[]? activeOnly = null)
    {
        StringBuilder text = new("Global\r\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n");
        foreach (string configuration in configurations)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\t\t{configuration} = {configuration}\r\n");
        }

        text.Append("\tEndGlobalSection\r\n\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n");
        foreach ((string guid, string configuration) in builds)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\t\t{{{guid}}}.{configuration}.ActiveCfg = {configuration}\r\n");
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\t\t{{{guid}}}.{configuration}.Build.0 = {configuration}\r\n");
        }

        foreach (string guid in activeOnly ?? [])
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\t\t{{{guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n");
        }

        return text.Append("\tEndGlobalSection\r\nEndGlobal\r\n").ToString();
    }

    /// <summary>A stable GUID per project name, upper-case as Visual Studio writes it.</summary>
    private static string ProjectGuid(string name) =>
        new Guid(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name))[..16]).ToString("D").ToUpperInvariant();
}
