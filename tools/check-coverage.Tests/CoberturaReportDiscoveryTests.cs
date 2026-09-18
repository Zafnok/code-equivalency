using CheckCoverage;

using Xunit;

namespace CheckCoverage.Tests;

public sealed class CoberturaReportDiscoveryTests
{
    [Fact]
    public void FindsTimestampedCoverletReportFiles()
    {
        string root = Directory.CreateTempSubdirectory("check-coverage-report-discovery").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "Equiv.Core.Tests.coverage.cobertura.180926043257082.xml"), "<coverage />");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "not coverage");

            IReadOnlyList<string> files = CoberturaReportDiscovery.FindReportFiles(root);

            string file = Assert.Single(files);
            Assert.EndsWith(".xml", file, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingDirectoryReturnsEmptyList()
    {
        IReadOnlyList<string> files = CoberturaReportDiscovery.FindReportFiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Empty(files);
    }
}