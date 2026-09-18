namespace CheckCoverage;

internal static class CoberturaReportDiscovery
{
    public static IReadOnlyList<string> FindReportFiles(string testResultsDirectory)
    {
        return Directory.Exists(testResultsDirectory)
            ? Directory.EnumerateFiles(testResultsDirectory, "*.cobertura.*.xml", SearchOption.AllDirectories).ToList()
            : [];
    }
}
