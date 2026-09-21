using LicenceCheck;

using Xunit;

namespace LicenceCheck.Tests;

/// <summary>
/// Drives the real orchestration against the actual repository (same convention as
/// Equiv.Tests.Integration's samples/ discovery: walk up from the test binary's own directory).
/// By the time tests run in build.ps1, restore and dotnet tool restore have already happened, so
/// nuget-license is on PATH and every lock-file-tracked package is restored.
/// </summary>
public sealed class GateRunnerTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public async Task TheRealRepositoryPassesTheGate()
    {
        string nugetPackagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        int exitCode = await GateRunner.RunAsync(RepoRoot, nugetPackagesRoot, fix: false);

        Assert.Equal(0, exitCode);
    }
}
