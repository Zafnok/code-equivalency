namespace LicenceCheck;

/// <summary>Pure: formats gate violations into the lines Program.cs prints, with no I/O.</summary>
internal static class ViolationReport
{
    public static IReadOnlyList<string> FormatLines(IReadOnlyList<LicenceViolation> violations)
    {
        List<string> lines = [$"licence-check: {violations.Count} package(s) failed the dependency licence gate:"];
        lines.AddRange(violations.Select(static violation => $"  - {violation}"));
        return lines;
    }
}
