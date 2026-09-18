using CheckCoverage;

using Xunit;

namespace CheckCoverage.Tests;

public sealed class SrcAssemblyDiscoveryTests
{
    [Fact]
    public void DiscoversCsprojBaseNamesRecursively()
    {
        string root = Directory.CreateTempSubdirectory("check-coverage-src-discovery").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Equiv.Core"));
            File.WriteAllText(Path.Combine(root, "Equiv.Core", "Equiv.Core.csproj"), "<Project />");
            Directory.CreateDirectory(Path.Combine(root, "Equiv.Cli"));
            File.WriteAllText(Path.Combine(root, "Equiv.Cli", "Equiv.Cli.csproj"), "<Project />");

            IReadOnlySet<string> names = SrcAssemblyDiscovery.DiscoverAssemblyNames(root);

            Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "Equiv.Core", "Equiv.Cli" }, names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingDirectoryReturnsEmptySet()
    {
        IReadOnlySet<string> names = SrcAssemblyDiscovery.DiscoverAssemblyNames(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Empty(names);
    }
}