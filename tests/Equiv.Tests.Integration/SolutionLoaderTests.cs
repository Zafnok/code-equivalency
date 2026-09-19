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
            Directory.Delete(copyDir, recursive: true);
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
}
