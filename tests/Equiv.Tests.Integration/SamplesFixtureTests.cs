using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Pins the M1-001 fixture shape: no MSBuild/Roslyn invocation here (that starts in
/// M2-001), just the folder layout and expected-verdict documentation each sample must have.
/// </summary>
public sealed class SamplesFixtureTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ExpectedVerdictsBySample =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["identical"] = ["Equivalent"],
            ["renamed-locals"] = ["Equivalent"],
            ["added-branch"] = ["Divergent"],
            ["removed-null-check"] = ["Divergent"],
            ["loop-bound-change"] = ["Divergent"],
            ["added-removed"] = ["Added", "Removed"],
            ["business-layer"] = ["Equivalent", "Divergent", "Unknown"],
            ["api-drift"] = ["Equivalent", "Divergent"],
            ["callee-changed"] = ["Divergent", "Equivalent"],
            ["loop-to-linq"] = ["Equivalent"],
            ["loop-fusion"] = ["Equivalent"],
        };

    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string> Samples =>
        [.. ExpectedVerdictsBySample.Keys];

    [Theory]
    [MemberData(nameof(Samples))]
    public void LegacySideIsAnOldStyleSolutionAndProject(string sample)
    {
        string legacyDir = Path.Combine(SamplesRoot, sample, "legacy");

        Assert.True(Directory.Exists(legacyDir), $"missing {legacyDir}");
        Assert.Single(Directory.GetFiles(legacyDir, "*.sln"));
        Assert.Single(Directory.GetFiles(legacyDir, "*.csproj"));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ModernSideIsAnSdkStyleSlnxAndProject(string sample)
    {
        string modernDir = Path.Combine(SamplesRoot, sample, "modern");

        Assert.True(Directory.Exists(modernDir), $"missing {modernDir}");
        Assert.Single(Directory.GetFiles(modernDir, "*.slnx"));
        Assert.Single(Directory.GetFiles(modernDir, "*.csproj"));
        Assert.Empty(Directory.GetFiles(modernDir, "*.sln"));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ReadmeDocumentsEveryExpectedVerdict(string sample)
    {
        string readmePath = Path.Combine(SamplesRoot, sample, "README.md");
        Assert.True(File.Exists(readmePath), $"missing {readmePath}");

        string readme = File.ReadAllText(readmePath);
        foreach (string verdict in ExpectedVerdictsBySample[sample])
        {
            Assert.Contains(verdict, readme, StringComparison.Ordinal);
        }
    }
}
