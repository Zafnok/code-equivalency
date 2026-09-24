using Equiv.Cli;
using Equiv.Core.Reporting;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// P2-013 against the real MSBuild build host: a legacy <c>.sln</c> lists a second project that its build
/// configuration does not build. That project is never opened, so although it could not load it does not make the run
/// incomplete; it is only named in <c>run.properties.projectsNotBuilt</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NotBuiltProjectTests
{
    private const string SiteProject = "Equiv.Samples.Identical.Site";

    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    [Fact]
    public void AProjectOutsideTheBuildConfigurationIsNotLoadedAndIsListed()
    {
        string copyDir = Path.Combine(Path.GetTempPath(), "equiv-P2-013-" + Guid.NewGuid().ToString("N"));
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-013-{Guid.NewGuid():N}.sarif");
        try
        {
            string legacy = CreateLegacyCopyWithAnUnbuiltProject(copyDir);
            string modern = Directory.GetFiles(Path.Combine(SamplesRoot, "identical", "modern"), "*.slnx").Single();

            int exitCode = CompareCommand.Run(
                new CompareOptions(legacy, modern, outPath, BaselinePath: null, ConfigPath: null, FailOn: "divergent", DryRun: false),
                [new CSharpFrontend()], new Z3Backend(), new FileReportSink(outPath));

            Assert.Equal(ExitCodes.Success, exitCode);
            Run run = SarifLog.Load(outPath).Runs[0];
            Assert.NotEmpty(run.Results);
            Assert.All(run.Results, static r => Assert.Equal("EQ001", r.RuleId));
            Assert.DoesNotContain(run.Invocations?.SelectMany(static i => i.ToolExecutionNotifications ?? []) ?? [], static n => n.Message.Text.Contains(SiteProject, StringComparison.Ordinal));
            Assert.True(run.TryGetSerializedPropertyValue("projectsNotBuilt", out string? notBuilt));
            Assert.Equal($$"""{"legacy":["{{SiteProject}}"],"modern":[]}""", notBuilt);
        }
        finally
        {
            File.Delete(outPath);
            DeleteBestEffort(copyDir);
        }
    }

    /// <summary>
    /// Copies <c>samples/identical/legacy</c> and adds a second net48 project to the <c>.sln</c> with no
    /// <c>ProjectConfigurationPlatforms</c> entry, as SignalR.Extras.Autofac's example sites have. Its only file uses a
    /// type no reference provides, so opening it would skip it and exit 4.
    /// </summary>
    private static string CreateLegacyCopyWithAnUnbuiltProject(string copyDir)
    {
        string sourceDir = Path.Combine(SamplesRoot, "identical", "legacy");
        Directory.CreateDirectory(Path.Combine(copyDir, "Properties"));
        Directory.CreateDirectory(Path.Combine(copyDir, "Site"));

        foreach (string file in new[] { "Calculator.cs", "Properties/AssemblyInfo.cs", "Equiv.Samples.Identical.Legacy.csproj" })
        {
            File.Copy(Path.Combine(sourceDir, file), Path.Combine(copyDir, file));
        }

        string csproj = File.ReadAllText(Path.Combine(sourceDir, "Equiv.Samples.Identical.Legacy.csproj"));
        const string Guid = "{A1000001-0000-0000-0000-000000000001}";
        const string SiteGuid = "{A1000001-0000-0000-0000-0000000000D1}";
        const string AssemblyName = "<AssemblyName>Equiv.Samples.Identical</AssemblyName>";
        Assert.Contains(AssemblyName, csproj, StringComparison.Ordinal);
        string siteCsproj = csproj
            .Replace(Guid, SiteGuid, StringComparison.Ordinal)
            .Replace(AssemblyName, $"<AssemblyName>{SiteProject}</AssemblyName>", StringComparison.Ordinal)
            .Replace("<Compile Include=\"Properties\\AssemblyInfo.cs\" />", string.Empty, StringComparison.Ordinal)
            .Replace("<Compile Include=\"Calculator.cs\" />", "<Compile Include=\"Uses.cs\" />", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(copyDir, "Site", $"{SiteProject}.csproj"), siteCsproj);
        File.WriteAllText(
            Path.Combine(copyDir, "Site", "Uses.cs"),
            "namespace Equiv.Samples.Identical.Site { public class Uses { public int Address() { Missing.Thing t = null; return 1; } } }");

        string sln = File.ReadAllText(Path.Combine(sourceDir, "Equiv.Samples.Identical.Legacy.sln"));
        const string Global = "Global";
        string siteEntry =
            $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{SiteProject}\", \"Site\\{SiteProject}.csproj\", \"{SiteGuid}\"\r\nEndProject\r\n";
        int global = sln.IndexOf(Global, StringComparison.Ordinal);
        string solutionPath = Path.Combine(copyDir, "Equiv.Samples.Identical.Legacy.sln");
        File.WriteAllText(solutionPath, sln[..global] + siteEntry + sln[global..]);
        return solutionPath;
    }

    /// <summary>The build host can still hold files under the copy's <c>obj/</c>; the OS temp folder is reclaimed anyway.</summary>
    private static void DeleteBestEffort(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Locked file: leave the folder behind.
        }
        catch (UnauthorizedAccessException)
        {
            // Locked file reported as access denied: leave the folder behind.
        }
    }
}
