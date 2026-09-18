namespace CheckCoverage;

internal sealed record CoverageExclusionViolation(string FilePath, int LineNumber, string Reason);
