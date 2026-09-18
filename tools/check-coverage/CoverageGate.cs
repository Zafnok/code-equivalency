using System.Globalization;
using System.Text;

namespace CheckCoverage;

internal static class CoverageGate
{
    private const double Tolerance = 1e-9;

    public static CoverageGateResult Evaluate(
        IReadOnlyDictionary<string, AssemblyCoverage> assemblies,
        IReadOnlySet<string> srcAssemblyNames,
        IReadOnlyList<CoverageExclusionViolation> exclusionViolations)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(srcAssemblyNames);
        ArgumentNullException.ThrowIfNull(exclusionViolations);

        List<AssemblyCoverage> srcAssemblies = assemblies.Values
            .Where(a => srcAssemblyNames.Contains(a.AssemblyName))
            .OrderBy(a => a.AssemblyName, StringComparer.Ordinal)
            .ToList();

        StringBuilder report = new();
        bool success = srcAssemblies.Count > 0;

        if (!success)
        {
            report.AppendLine("No coverage data found for any src/ assembly under TestResults/.");
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"{"Assembly",-35} {"Line",8} {"Branch",8} {"Result",6}");

        foreach (AssemblyCoverage assembly in srcAssemblies)
        {
            bool assemblyOk = assembly.LineRate >= 1.0 - Tolerance && assembly.BranchRate >= 1.0 - Tolerance;
            success &= assemblyOk;

            report.AppendLine(CultureInfo.InvariantCulture, $"{assembly.AssemblyName,-35} {assembly.LineRate * 100,7:0.00}% {assembly.BranchRate * 100,7:0.00}% {(assemblyOk ? "PASS" : "FAIL"),6}");
        }

        if (exclusionViolations.Count > 0)
        {
            success = false;
            report.AppendLine();
            report.AppendLine("ExcludeFromCodeCoverage violations:");

            foreach (CoverageExclusionViolation violation in exclusionViolations)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {violation.FilePath}:{violation.LineNumber}: {violation.Reason}");
            }
        }

        return new CoverageGateResult(success, report.ToString());
    }
}