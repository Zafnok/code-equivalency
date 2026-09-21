using CheckCoverage;

CommandLineOptions options = CommandLineOptions.Parse(args);

if (!Directory.Exists(options.TestResultsDir))
{
    await Console.Error.WriteLineAsync($"check-coverage: test results directory '{options.TestResultsDir}' does not exist.").ConfigureAwait(false);
    return 1;
}

List<string> xmlContents =
[
    .. CoberturaReportDiscovery.FindReportFiles(options.TestResultsDir).Select(File.ReadAllText),
];

IReadOnlyDictionary<string, AssemblyCoverage> assemblies = CoberturaCoverageReader.Merge(xmlContents);
IReadOnlySet<string> srcAssemblyNames = SrcAssemblyDiscovery.DiscoverAssemblyNames(options.SrcDir);

IReadOnlyList<CoverageExclusionViolation> violations = Directory.Exists(options.SrcDir)
    ? ExcludeFromCoverageAuditor.AuditDirectory(options.SrcDir)
    : [];

CoverageGateResult result = CoverageGate.Evaluate(assemblies, srcAssemblyNames, violations);
Console.WriteLine(result.Report);

return result.Success ? 0 : 1;
