using Microsoft.CodeAnalysis.Sarif;

namespace Equiv.Core.Reporting;

/// <summary>
/// Computes SARIF <c>baselineState</c> (VERIFICATION-MODEL.md section 6) for the current run's
/// results against a previous log, by matching <see cref="SarifReportWriter.ProcedureIdentityFingerprintId"/>
/// partial fingerprints. A previous result whose procedure identity has no match in the current
/// run becomes an <see cref="BaselineState.Absent"/> carry-over (a copy of the previous result).
/// </summary>
/// <remarks>
/// <see cref="StateFor"/> matches a previous result by procedure identity <em>and</em> rule id
/// (the verdict's kind: EQ001-EQ005), not by identity alone. VERIFICATION-MODEL.md section 6 says
/// the exit code counts only <c>new</c> results, so a rule-id change for the same identity — for
/// example a procedure going from Equivalent (EQ001) to Divergent (EQ002), a regression — must be
/// <c>new</c>, never <c>updated</c>: <c>updated</c> would make CI silently skip it by default.
/// Only a same-rule-id fingerprint change (e.g. a different counterexample for a procedure that
/// was already Divergent) is <c>updated</c> versus <c>unchanged</c>; that distinction is
/// deliberately not exit-code-significant, so it tolerates a verification backend whose "model
/// hash" (the counterexample/reason text baked into <see cref="ResultFingerprint"/>) is not
/// guaranteed byte-identical across runs of the same divergence.
/// </remarks>
internal static class BaselineComputer
{
    public static BaselineState StateFor(string identity, string ruleId, string fingerprint, SarifLog? baseline)
    {
        return !PreviousEntries(baseline).TryGetValue(identity, out (string RuleId, string Fingerprint) previous)
            || !string.Equals(previous.RuleId, ruleId, StringComparison.Ordinal)
            ? BaselineState.New
            : string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal) ? BaselineState.Unchanged : BaselineState.Updated;
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

    private static Dictionary<string, (string RuleId, string Fingerprint)> PreviousEntries(SarifLog? baseline)
    {
        Dictionary<string, (string RuleId, string Fingerprint)> entries = new(StringComparer.Ordinal);
        foreach (Result previous in PreviousResults(baseline))
        {
            string? identity = IdentityOf(previous);
            string? fingerprint = previous.PartialFingerprints is { } previousFingerprints
                && previousFingerprints.TryGetValue(SarifReportWriter.ResultFingerprintId, out string? value)
                ? value
                : null;
            if (identity is not null && fingerprint is not null && previous.RuleId is { } ruleId)
            {
                entries[identity] = (ruleId, fingerprint);
            }
        }

        return entries;
    }

    private static IEnumerable<Result> PreviousResults(SarifLog? baseline) =>
        baseline?.Runs is [{ Results: { } results }, ..] ? results : [];

    private static string? IdentityOf(Result result) =>
        result.PartialFingerprints is { } fingerprints && fingerprints.TryGetValue(SarifReportWriter.ProcedureIdentityFingerprintId, out string? identity)
            ? identity
            : null;
}
