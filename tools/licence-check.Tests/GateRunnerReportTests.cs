using LicenceCheck;

using Xunit;

namespace LicenceCheck.Tests;

/// <summary>
/// Exercises every branch of <see cref="GateRunner.ReportAsync"/> with a fabricated
/// <see cref="LicenceGateResult"/> and <see cref="PackageInventory"/> against a throwaway temp
/// directory, rather than only through the one real-repo path <see cref="GateRunnerTests"/> covers
/// (which is always the "already passing, notices already up to date" branch in a clean tree).
/// </summary>
public sealed class GateRunnerReportTests : IDisposable
{
    private static readonly ResolvedPackage[] OnePassingPackage =
    [
        new("Widget", "1.0.0", PackageRole.Redistributed, "MIT", IsSpdxExpression: true),
    ];

    private readonly string _repoRoot = Directory.CreateTempSubdirectory("licence-check-report-").FullName;
    private readonly string _noticesPath;

    public GateRunnerReportTests() => _noticesPath = Path.Combine(_repoRoot, "THIRD-PARTY-NOTICES.md");

    public void Dispose() => Directory.Delete(_repoRoot, recursive: true);

    [Fact]
    public async Task ReturnsOneWhenTheGateHasViolations()
    {
        LicenceGateResult failing = new([], [new LicenceViolation("Bad", "1.0.0", "denied")]);
        PackageInventory inventory = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), [], SamplesFullyRestored: true);

        int exitCode = await GateRunner.ReportAsync(_repoRoot, inventory, failing, fix: false);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ReturnsOneWhenNoticesAreOutOfDateAndFixIsNotSet()
    {
        LicenceGateResult passing = new(OnePassingPackage, []);
        PackageInventory inventory = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), [], SamplesFullyRestored: true);

        int exitCode = await GateRunner.ReportAsync(_repoRoot, inventory, passing, fix: false);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(_noticesPath));
    }

    [Fact]
    public async Task RegeneratesNoticesWhenOutOfDateAndFixIsSet()
    {
        LicenceGateResult passing = new(OnePassingPackage, []);
        PackageInventory inventory = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), [], SamplesFullyRestored: true);

        int exitCode = await GateRunner.ReportAsync(_repoRoot, inventory, passing, fix: true);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(_noticesPath));
        Assert.Equal(ThirdPartyNoticesRenderer.Render(OnePassingPackage), await File.ReadAllTextAsync(_noticesPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SkipsTheNoticesCheckWhenSamplesWereNotFullyRestored()
    {
        LicenceGateResult passing = new(OnePassingPackage, []);
        PackageInventory inventory = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), [], SamplesFullyRestored: false);

        int exitCode = await GateRunner.ReportAsync(_repoRoot, inventory, passing, fix: false);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(_noticesPath));
    }

    [Fact]
    public async Task ReturnsZeroWhenNoticesAreAlreadyUpToDate()
    {
        LicenceGateResult passing = new(OnePassingPackage, []);
        PackageInventory inventory = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), [], SamplesFullyRestored: true);
        await File.WriteAllTextAsync(_noticesPath, ThirdPartyNoticesRenderer.Render(OnePassingPackage), TestContext.Current.CancellationToken);

        int exitCode = await GateRunner.ReportAsync(_repoRoot, inventory, passing, fix: false);

        Assert.Equal(0, exitCode);
    }
}
