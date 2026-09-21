using LicenceCheck;

using Xunit;

namespace LicenceCheck.Tests;

/// <summary>
/// Regression coverage for the ubuntu-latest CI break after PR #73: samples/ is only restored
/// under build.ps1 -Integration (the legacy side needs MSBuild.exe, Windows-only), so a plain
/// ./build.ps1 run never restores it and a samples package's nuspec is legitimately absent.
/// </summary>
public sealed class PackageInventoryBuilderTests : IDisposable
{
    private readonly string _repoRoot = Directory.CreateTempSubdirectory("licence-check-inventory-repo-").FullName;
    private readonly string _nugetCache = Directory.CreateTempSubdirectory("licence-check-inventory-cache-").FullName;

    public void Dispose()
    {
        Directory.Delete(_repoRoot, recursive: true);
        Directory.Delete(_nugetCache, recursive: true);
    }

    [Fact]
    public void ASamplesPackageNotRestoredInThisRunIsSkippedNotThrown()
    {
        WriteMinimalRepoScaffold();
        WriteSamplesProject("Widget.NeverRestored", "1.0.0");

        PackageInventory inventory = PackageInventoryBuilder.Build(_repoRoot, _nugetCache);

        Assert.Empty(inventory.ExtraPackages);
        Assert.False(inventory.SamplesFullyRestored);
    }

    [Fact]
    public void ASamplesPackageThatIsRestoredIsResolved()
    {
        WriteMinimalRepoScaffold();
        WriteSamplesProject("Widget.Restored", "2.0.0");
        WriteNuspec("Widget.Restored", "2.0.0", """<license type="expression">Apache-2.0</license>""");

        PackageInventory inventory = PackageInventoryBuilder.Build(_repoRoot, _nugetCache);

        ResolvedPackage package = Assert.Single(inventory.ExtraPackages);
        Assert.Equal("Widget.Restored", package.Id);
        Assert.Equal(PackageRole.SamplesOnly, package.Role);
        Assert.Equal("Apache-2.0", package.DeclaredLicence);
        Assert.True(inventory.SamplesFullyRestored);
    }

    private void WriteMinimalRepoScaffold()
    {
        File.WriteAllText(Path.Combine(_repoRoot, "Directory.Build.props"), "<Project />");

        Directory.CreateDirectory(Path.Combine(_repoRoot, "src"));

        Directory.CreateDirectory(Path.Combine(_repoRoot, ".config"));
        File.WriteAllText(Path.Combine(_repoRoot, ".config", "dotnet-tools.json"), """{ "version": 1, "isRoot": true, "tools": {} }""");
    }

    private void WriteSamplesProject(string packageId, string version)
    {
        string projectDir = Path.Combine(_repoRoot, "samples", "widget-basic");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(
            Path.Combine(projectDir, "Widget.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="{packageId}" Version="{version}" />
              </ItemGroup>
            </Project>
            """);
    }

    private void WriteNuspec(string id, string version, string licenseElement)
    {
        string idLower = id.ToLowerInvariant();
        string directory = Path.Combine(_nugetCache, idLower, version);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{idLower}.nuspec"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                {licenseElement}
              </metadata>
            </package>
            """);
    }
}
