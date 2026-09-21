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

        List<AssemblyCoverage> srcAssemblies =
        [
            .. assemblies.Values
                .Where(a => srcAssemblyNames.Contains(a.AssemblyName))
                .OrderBy(a => a.AssemblyName, StringComparer.Ordinal)
        ];

        List<string> missingAssemblyNames =
        [
            .. srcAssemblyNames
                .Where(name => !assemblies.ContainsKey(name))
                .OrderBy(name => name, StringComparer.Ordinal)
        ];

        StringBuilder report = new();
        bool success = srcAssemblies.Count > 0 && missingAssemblyNames.Count == 0;

        if (srcAssemblies.Count == 0)
        {
            report.AppendLine("No coverage data found for any src/ assembly under TestResults/.");
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"{"Assembly",-35} {"Line",17} {"Branch",17} {"Result",6}");

        foreach (AssemblyCoverage assembly in srcAssemblies)
        {
            bool assemblyOk = assembly.LineRate >= 1.0 - Tolerance && assembly.BranchRate >= 1.0 - Tolerance;
            success &= assemblyOk;

            string lineCell = string.Format(CultureInfo.InvariantCulture, "{0,3}/{1,-3} {2,6:0.00}%", assembly.LinesCovered, assembly.LinesValid, assembly.LineRate * 100);
            string branchCell = string.Format(CultureInfo.InvariantCulture, "{0,3}/{1,-3} {2,6:0.00}%", assembly.BranchesCovered, assembly.BranchesValid, assembly.BranchRate * 100);

            report.AppendLine(CultureInfo.InvariantCulture, $"{assembly.AssemblyName,-35} {lineCell,17} {branchCell,17} {(assemblyOk ? "PASS" : "FAIL"),6}");
        }

        foreach (string missingAssemblyName in missingAssemblyNames)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{missingAssemblyName,-35} {"NO COVERAGE DATA",17} {string.Empty,17} {"FAIL",6}");
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
