using CheckCoverage;

string testResultsDir = "TestResults";
string srcDir = "src";

for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "--test-results", StringComparison.Ordinal) && i + 1 < args.Length)
    {
        testResultsDir = args[++i];
    }
    else if (string.Equals(args[i], "--src", StringComparison.Ordinal) && i + 1 < args.Length)
    {
        srcDir = args[++i];
    }
}

if (!Directory.Exists(testResultsDir))
{
    Console.Error.WriteLine($"check-coverage: test results directory '{testResultsDir}' does not exist.");
    return 1;
}

List<string> xmlContents = CoberturaReportDiscovery.FindReportFiles(testResultsDir)
    .Select(File.ReadAllText)
    .ToList();

IReadOnlyDictionary<string, AssemblyCoverage> assemblies = CoberturaCoverageReader.Merge(xmlContents);
IReadOnlySet<string> srcAssemblyNames = SrcAssemblyDiscovery.DiscoverAssemblyNames(srcDir);

IReadOnlyList<CoverageExclusionViolation> violations = Directory.Exists(srcDir)
    ? ExcludeFromCoverageAuditor.AuditDirectory(srcDir)
    : [];

CoverageGateResult result = CoverageGate.Evaluate(assemblies, srcAssemblyNames, violations);
Console.WriteLine(result.Report);

return result.Success ? 0 : 1;