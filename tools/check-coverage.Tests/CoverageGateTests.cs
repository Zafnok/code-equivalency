using CheckCoverage;

using Xunit;

namespace CheckCoverage.Tests;

public sealed class CoverageGateTests
{
    private static readonly IReadOnlySet<string> OnlyEquivCore = new HashSet<string>(StringComparer.Ordinal) { "Equiv.Core" };

    [Fact]
    public void FullyCoveredAssemblyNoViolationsSucceeds()
    {
        Dictionary<string, AssemblyCoverage> assemblies = new(StringComparer.Ordinal)
        {
            ["Equiv.Core"] = new AssemblyCoverage("Equiv.Core", LinesValid: 10, LinesCovered: 10, BranchesValid: 4, BranchesCovered: 4),
        };

        CoverageGateResult result = CoverageGate.Evaluate(assemblies, OnlyEquivCore, []);

        Assert.True(result.Success);
        Assert.Contains("Equiv.Core", result.Report, StringComparison.Ordinal);
        Assert.Contains("PASS", result.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void PartiallyCoveredAssemblyFails()
    {
        Dictionary<string, AssemblyCoverage> assemblies = new(StringComparer.Ordinal)
        {
            ["Equiv.Core"] = new AssemblyCoverage("Equiv.Core", LinesValid: 10, LinesCovered: 9, BranchesValid: 4, BranchesCovered: 4),
        };

        CoverageGateResult result = CoverageGate.Evaluate(assemblies, OnlyEquivCore, []);

        Assert.False(result.Success);
        Assert.Contains("FAIL", result.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void PartialBranchCoverageFails()
    {
        Dictionary<string, AssemblyCoverage> assemblies = new(StringComparer.Ordinal)
        {
            ["Equiv.Core"] = new AssemblyCoverage("Equiv.Core", LinesValid: 10, LinesCovered: 10, BranchesValid: 4, BranchesCovered: 3),
        };

        CoverageGateResult result = CoverageGate.Evaluate(assemblies, OnlyEquivCore, []);

        Assert.False(result.Success);
    }

    [Fact]
    public void NoAssembliesFails()
    {
        CoverageGateResult result = CoverageGate.Evaluate(new Dictionary<string, AssemblyCoverage>(StringComparer.Ordinal), OnlyEquivCore, []);

        Assert.False(result.Success);
        Assert.Contains("No coverage data", result.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void AssemblyNotInSrcIsIgnoredEvenWhenUncovered()
    {
        Dictionary<string, AssemblyCoverage> assemblies = new(StringComparer.Ordinal)
        {
            ["Equiv.Core"] = new AssemblyCoverage("Equiv.Core", LinesValid: 10, LinesCovered: 10, BranchesValid: 0, BranchesCovered: 0),
            ["CheckCoverage"] = new AssemblyCoverage("CheckCoverage", LinesValid: 10, LinesCovered: 1, BranchesValid: 0, BranchesCovered: 0),
        };

        CoverageGateResult result = CoverageGate.Evaluate(assemblies, OnlyEquivCore, []);

        Assert.True(result.Success);
        Assert.DoesNotContain("CheckCoverage", result.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void SrcAssemblyWithNoCoverageDataFailsAndNamesIt()
    {
        Dictionary<string, AssemblyCoverage> assemblies = new(StringComparer.Ordinal)
        {
            ["Equiv.Core"] = new AssemblyCoverage("Equiv.Core", LinesValid: 10, LinesCovered: 10, BranchesValid: 4, BranchesCovered: 4),
            ["Equiv.Frontend.CSharp"] = new AssemblyCoverage("Equiv.Frontend.CSharp", LinesValid: 10, LinesCovered: 10, BranchesValid: 4, BranchesCovered: 4),
            ["Equiv.Verify.Z3"] = new AssemblyCoverage("Equiv.Verify.Z3", LinesValid: 10, LinesCovered: 10, BranchesValid: 4, BranchesCovered: 4),
        };
        IReadOnlySet<string> fourSrcNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Equiv.Core", "Equiv.Frontend.CSharp", "Equiv.Verify.Z3", "Equiv.Cli",
        };

        CoverageGateResult result = CoverageGate.Evaluate(assemblies, fourSrcNames, []);

        Assert.False(result.Success);
        Assert.Contains("Equiv.Cli", result.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void ExclusionViolationFailsEvenWhenFullyCovered()
    {
        Dictionary<string, AssemblyCoverage> assemblies = new(StringComparer.Ordinal)
        {
            ["Equiv.Core"] = new AssemblyCoverage("Equiv.Core", LinesValid: 10, LinesCovered: 10, BranchesValid: 0, BranchesCovered: 0),
        };
        CoverageExclusionViolation[] violations = [new CoverageExclusionViolation("Foo.cs", 1, "no Justification")];

        CoverageGateResult result = CoverageGate.Evaluate(assemblies, OnlyEquivCore, violations);

        Assert.False(result.Success);
        Assert.Contains("Foo.cs:1", result.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void NullAssembliesThrows()
    {
        Assert.Throws<ArgumentNullException>(() => CoverageGate.Evaluate(null!, OnlyEquivCore, []));
    }

    [Fact]
    public void NullSrcAssemblyNamesThrows()
    {
        Assert.Throws<ArgumentNullException>(() => CoverageGate.Evaluate(new Dictionary<string, AssemblyCoverage>(StringComparer.Ordinal), null!, []));
    }

    [Fact]
    public void NullViolationsThrows()
    {
        Assert.Throws<ArgumentNullException>(() => CoverageGate.Evaluate(new Dictionary<string, AssemblyCoverage>(StringComparer.Ordinal), OnlyEquivCore, null!));
    }
}
