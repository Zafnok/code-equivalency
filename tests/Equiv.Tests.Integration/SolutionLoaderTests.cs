using System.Diagnostics;
using System.Globalization;

using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// M2-001: <see cref="MsBuildSolutionLoader"/> against the real MSBuild build hosts (VS Build Tools
/// for the legacy side, the .NET SDK for the modern side).
/// </summary>
[Trait("Category", "Integration")]
public sealed class SolutionLoaderTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string, string> SampleSides => new()
    {
        { "identical", "legacy" },
        { "identical", "modern" },
        { "renamed-locals", "legacy" },
        { "renamed-locals", "modern" },
        { "added-branch", "legacy" },
        { "added-branch", "modern" },
        { "removed-null-check", "legacy" },
        { "removed-null-check", "modern" },
        { "loop-bound-change", "legacy" },
        { "loop-bound-change", "modern" },
        { "added-removed", "legacy" },
        { "added-removed", "modern" },
    };

    [Theory]
    [MemberData(nameof(SampleSides))]
    public async Task EverySampleSideLoadsWithoutFailures(string sample, string side)
    {
        string sideDir = Path.Combine(SamplesRoot, sample, side);
        string solutionPath = Directory.GetFiles(sideDir, string.Equals(side, "legacy", StringComparison.Ordinal) ? "*.sln" : "*.slnx").Single();

        LoadedSolution loaded = await new MsBuildSolutionLoader().LoadAsync(solutionPath, TestContext.Current.CancellationToken);

        Assert.Single(loaded.Compilations);
        Assert.DoesNotContain(loaded.Diagnostics, static d => d.Kind is not LoadDiagnosticKind.WorkspaceWarning);
    }

    [Fact]
    public async Task LegacyCopyWithARemovedReferenceFailsAsUnresolvedReference()
    {
        string copyDir = Path.Combine(Path.GetTempPath(), "equiv-M2-001-" + Guid.NewGuid().ToString("N"));
        try
        {
            string solutionPath = CreateBrokenLegacyCopy(copyDir);

            SolutionLoadException ex = await Assert.ThrowsAsync<SolutionLoadException>(
                () => new MsBuildSolutionLoader().LoadAsync(solutionPath, TestContext.Current.CancellationToken));

            Assert.Contains(ex.Diagnostics, static d => d is { Id: "CS0246", Kind: LoadDiagnosticKind.UnresolvedReference });
        }
        finally
        {
            DeleteBestEffort(copyDir);
        }
    }

    /// <summary>
    /// P2-012: a net10.0 project referencing a .NET Framework-only package restores with NU1701, which
    /// MSBuildWorkspace reports as a <c>WorkspaceDiagnosticKind.Failure</c>; the project must still load.
    /// </summary>
    [Fact]
    public async Task ModernCopyReferencingAFrameworkOnlyPackageLoadsWithNoSkippedProject()
    {
        string copyDir = Path.Combine(Path.GetTempPath(), "equiv-P2-012-" + Guid.NewGuid().ToString("N"));
        try
        {
            string solutionPath = CreateModernCopyWithAFrameworkOnlyPackage(copyDir);
            Restore(Path.Combine(copyDir, "Equiv.Samples.Identical.Modern.csproj"));

            LoadedSolution loaded = await new MsBuildSolutionLoader().LoadAsync(solutionPath, TestContext.Current.CancellationToken);

            Assert.Single(loaded.Compilations);
            Assert.Empty(loaded.Skipped);
            Assert.Contains(
                loaded.Diagnostics,
                static d => d.Kind == LoadDiagnosticKind.WorkspaceWarning && d.Message.Contains("was restored using", StringComparison.Ordinal));
            Assert.DoesNotContain(loaded.Diagnostics, static d => d.Kind is not LoadDiagnosticKind.WorkspaceWarning);
        }
        finally
        {
            DeleteBestEffort(copyDir);
        }
    }

    /// <summary>
    /// The build host can still hold files under the copy's <c>obj/</c>; a failed cleanup must not
    /// replace the test's own result, and the OS temp folder is reclaimed anyway.
    /// </summary>
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

    /// <summary>
    /// Copies <c>samples/identical/legacy</c> and removes its <c>System</c> reference. The sample
    /// itself only uses mscorlib, so the copy also gains one file that uses <see cref="Uri"/>
    /// (System.dll on net48); otherwise the missing reference would go unnoticed.
    /// </summary>
    private static string CreateBrokenLegacyCopy(string copyDir)
    {
        string sourceDir = Path.Combine(SamplesRoot, "identical", "legacy");
        Directory.CreateDirectory(Path.Combine(copyDir, "Properties"));

        foreach (string file in new[] { "Calculator.cs", "Properties/AssemblyInfo.cs", "Equiv.Samples.Identical.Legacy.sln" })
        {
            File.Copy(Path.Combine(sourceDir, file), Path.Combine(copyDir, file));
        }

        string csproj = File.ReadAllText(Path.Combine(sourceDir, "Equiv.Samples.Identical.Legacy.csproj"));
        const string SystemReference = "<Reference Include=\"System\" />";
        const string CalculatorCompile = "<Compile Include=\"Calculator.cs\" />";
        Assert.Contains(SystemReference, csproj, StringComparison.Ordinal);

        csproj = csproj
            .Replace(SystemReference, string.Empty, StringComparison.Ordinal)
            .Replace(CalculatorCompile, CalculatorCompile + "<Compile Include=\"UsesSystem.cs\" />", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(copyDir, "Equiv.Samples.Identical.Legacy.csproj"), csproj);
        File.WriteAllText(
            Path.Combine(copyDir, "UsesSystem.cs"),
            "using System; namespace Equiv.Samples.Identical { public class UsesSystem { public Uri Address; } }");

        return Path.Combine(copyDir, "Equiv.Samples.Identical.Legacy.sln");
    }

    /// <summary>
    /// Copies <c>samples/identical/modern</c> and adds a <c>PackageReference</c> to a .NET Framework-only package
    /// (already restored by <c>samples/webapi-basic/legacy</c>, so it is known to be available to this repo's feeds).
    /// </summary>
    private static string CreateModernCopyWithAFrameworkOnlyPackage(string copyDir)
    {
        string sourceDir = Path.Combine(SamplesRoot, "identical", "modern");
        Directory.CreateDirectory(copyDir);

        foreach (string file in new[] { "Calculator.cs", "Equiv.Samples.Identical.Modern.slnx" })
        {
            File.Copy(Path.Combine(sourceDir, file), Path.Combine(copyDir, file));
        }

        string csproj = File.ReadAllText(Path.Combine(sourceDir, "Equiv.Samples.Identical.Modern.csproj"));
        const string CloseProject = "</Project>";
        Assert.Contains(CloseProject, csproj, StringComparison.Ordinal);
        csproj = csproj.Replace(
            CloseProject,
            "  <ItemGroup>\r\n    <PackageReference Include=\"Microsoft.AspNet.WebApi.Core\" Version=\"5.3.0\" />\r\n  </ItemGroup>\r\n\r\n" + CloseProject,
            StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(copyDir, "Equiv.Samples.Identical.Modern.csproj"), csproj);

        return Path.Combine(copyDir, "Equiv.Samples.Identical.Modern.slnx");
    }

    /// <summary>
    /// MSBuildWorkspace's design-time build does not itself restore an SDK-style project (build.ps1's
    /// "restore samples" step does this for <c>samples/**</c>; a copy under the OS temp folder needs its own).
    /// </summary>
    private static void Restore(string csprojPath)
    {
        using Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", csprojPath])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        string output = restore.StandardOutput.ReadToEnd();
        string error = restore.StandardError.ReadToEnd();
        restore.WaitForExit();
        int exitCode = restore.ExitCode;
        Assert.True(exitCode == 0, string.Create(CultureInfo.InvariantCulture, $"dotnet restore failed ({exitCode}):\n{output}\n{error}"));
    }
}
