using LicenceCheck;

using Xunit;

namespace LicenceCheck.Tests;

public sealed class NuGetLicenseInvokerTests
{
    [Fact]
    public void OriginZeroIsTreatedAsAnSpdxExpression()
    {
        const string json = """
            [
              { "PackageId": "Sarif.Sdk", "PackageVersion": "5.7.0", "License": "MIT", "LicenseInformationOrigin": 0 }
            ]
            """;

        IReadOnlyList<ResolvedPackage> packages = NuGetLicenseInvoker.Parse(json, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sarif.Sdk" });

        ResolvedPackage package = Assert.Single(packages);
        Assert.Equal("Sarif.Sdk", package.Id);
        Assert.Equal("5.7.0", package.Version);
        Assert.Equal("MIT", package.DeclaredLicence);
        Assert.True(package.IsSpdxExpression);
        Assert.Equal(PackageRole.Redistributed, package.Role);
    }

    [Fact]
    public void NonZeroOriginIsNotTreatedAsAnSpdxExpression()
    {
        const string json = """
            [
              { "PackageId": "Verify", "PackageVersion": "33.0.2", "License": "OsmfEula.txt", "LicenseInformationOrigin": 5 }
            ]
            """;

        IReadOnlyList<ResolvedPackage> packages = NuGetLicenseInvoker.Parse(json, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        ResolvedPackage package = Assert.Single(packages);
        Assert.False(package.IsSpdxExpression);
        Assert.Equal(PackageRole.BuildAndTestOnly, package.Role);
    }

    [Fact]
    public void ResolveDotnetHostPathReturnsAnExistingAbsolutePath()
    {
        string path = NuGetLicenseInvoker.ResolveDotnetHostPath();

        Assert.True(Path.IsPathRooted(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void TryResolveDotnetHostPathReturnsCandidateWhenItExists()
    {
        string tempRoot = Path.GetTempPath();
        string runtimeDirectory = Path.Combine(tempRoot, "a", "b", "c");

        string? result = NuGetLicenseInvoker.TryResolveDotnetHostPath(runtimeDirectory, "dotnet", static _ => true);

        Assert.Equal(Path.Combine(tempRoot.TrimEnd(Path.DirectorySeparatorChar), "dotnet"), result);
    }

    [Fact]
    public void TryResolveDotnetHostPathReturnsNullWhenCandidateDoesNotExist()
    {
        string runtimeDirectory = Path.Combine(Path.GetTempPath(), "a", "b", "c");

        string? result = NuGetLicenseInvoker.TryResolveDotnetHostPath(runtimeDirectory, "dotnet", static _ => false);

        Assert.Null(result);
    }

    [Fact]
    public void TryResolveDotnetHostPathReturnsNullWhenRuntimeDirectoryHasNoGrandparent()
    {
        string runtimeDirectory = Path.GetPathRoot(Path.GetTempPath())!;

        string? result = NuGetLicenseInvoker.TryResolveDotnetHostPath(runtimeDirectory, "dotnet", static _ => true);

        Assert.Null(result);
    }

    [Fact]
    public void PackagesNotInRedistributedIdsAreBuildAndTestOnly()
    {
        const string json = """
            [
              { "PackageId": "xunit.v3", "PackageVersion": "4.0.1", "License": "Apache-2.0", "LicenseInformationOrigin": 0 }
            ]
            """;

        IReadOnlyList<ResolvedPackage> packages = NuGetLicenseInvoker.Parse(json, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Sarif.Sdk" });

        Assert.Equal(PackageRole.BuildAndTestOnly, Assert.Single(packages).Role);
    }
}
