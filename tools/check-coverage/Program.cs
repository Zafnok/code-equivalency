using CheckCoverage;

string testResultsDir = "TestResults";
string srcDir = "src";

int i = 0;
while (i < args.Length)
{
    if (string.Equals(args[i], "--test-results", StringComparison.Ordinal) && i + 1 < args.Length)
    {
        testResultsDir = args[i + 1];
        i += 2;
    }
    else if (string.Equals(args[i], "--src", StringComparison.Ordinal) && i + 1 < args.Length)
    {
        srcDir = args[i + 1];
        i += 2;
    }
    else
    {
        i++;
    }
}

if (!Directory.Exists(testResultsDir))
{
    await Console.Error.WriteLineAsync($"check-coverage: test results directory '{testResultsDir}' does not exist.").ConfigureAwait(false);
    return 1;
}

List<string> xmlContents =
[
    .. CoberturaReportDiscovery.FindReportFiles(testResultsDir).Select(File.ReadAllText)
];

IReadOnlyDictionary<string, AssemblyCoverage> assemblies = CoberturaCoverageReader.Merge(xmlContents);
IReadOnlySet<string> srcAssemblyNames = SrcAssemblyDiscovery.DiscoverAssemblyNames(srcDir);

IReadOnlyList<CoverageExclusionViolation> violations = Directory.Exists(srcDir)
    ? ExcludeFromCoverageAuditor.AuditDirectory(srcDir)
    : [];

CoverageGateResult result = CoverageGate.Evaluate(assemblies, srcAssemblyNames, violations);
Console.WriteLine(result.Report);

return result.Success ? 0 : 1;
