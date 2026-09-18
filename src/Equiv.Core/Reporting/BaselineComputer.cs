using Microsoft.CodeAnalysis.Sarif;

namespace Equiv.Core.Reporting;

/// <summary>
/// Computes SARIF <c>baselineState</c> (VERIFICATION-MODEL.md section 6) for the current run's
/// results against a previous log, by matching <see cref="SarifReportWriter.ProcedureIdentityFingerprintId"/>
/// partial fingerprints. A previous result whose procedure identity has no match in the current
/// run becomes an <see cref="BaselineState.Absent"/> carry-over (a copy of the previous result).
/// </summary>
internal static class BaselineComputer
{
    public static BaselineState StateFor(string identity, string fingerprint, SarifLog? baseline)
    {
        string? previousFingerprint = PreviousFingerprints(baseline).GetValueOrDefault(identity);
        if (previousFingerprint is null)
        {
            return BaselineState.New;
        }

        return string.Equals(previousFingerprint, fingerprint, StringComparison.Ordinal) ? BaselineState.Unchanged : BaselineState.Updated;
    }

    public static IReadOnlyList<Result> AbsentResults(IReadOnlySet<string> currentIdentities, SarifLog? baseline)
    {
        if (baseline is null)
        {
            return [];
        }

        List<Result> absent = [];
        foreach (Result previous in PreviousResults(baseline))
        {
            string? identity = IdentityOf(previous);
            if (identity is not null && !currentIdentities.Contains(identity))
            {
                Result carryOver = previous.DeepClone();
                carryOver.BaselineState = BaselineState.Absent;
                absent.Add(carryOver);
            }
        }

        return absent;
    }

    private static Dictionary<string, string> PreviousFingerprints(SarifLog? baseline)
    {
        Dictionary<string, string> fingerprints = new(StringComparer.Ordinal);
        foreach (Result previous in PreviousResults(baseline))
        {
            string? identity = IdentityOf(previous);
            string? fingerprint = previous.PartialFingerprints is { } previousFingerprints
                && previousFingerprints.TryGetValue(SarifReportWriter.ResultFingerprintId, out string? value)
                ? value
                : null;
            if (identity is not null && fingerprint is not null)
            {
                fingerprints[identity] = fingerprint;
            }
        }

        return fingerprints;
    }

    private static IEnumerable<Result> PreviousResults(SarifLog? baseline) =>
        baseline?.Runs is [{ Results: { } results }, ..] ? results : [];

    private static string? IdentityOf(Result result) =>
        result.PartialFingerprints is { } fingerprints && fingerprints.TryGetValue(SarifReportWriter.ProcedureIdentityFingerprintId, out string? identity)
            ? identity
            : null;
}
