using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

namespace Equiv.Core.Reporting;

/// <summary>
/// Builds a SARIF 2.1.0 <see cref="SarifLog"/> from a report (VERIFICATION-MODEL.md section 6;
/// ARCHITECTURE.md's data-flow diagram: "SARIF writer &lt;- baseline diff &lt;- verdicts"). One
/// <see cref="Result"/> per <see cref="VerificationResult"/>, plus any
/// <see cref="BaselineState.Absent"/> carry-overs from <paramref name="baseline"/>
/// (see <see cref="Write"/>). <see cref="Equiv.Core.IReportSink"/> persists the returned log.
/// </summary>
public static class SarifReportWriter
{
    /// <summary>SARIF partial-fingerprint key for the procedure identity a result is about.</summary>
    internal const string ProcedureIdentityFingerprintId = "procedureIdentity/v1";

    /// <summary>SARIF partial-fingerprint key for <see cref="ResultFingerprint.Compute"/>'s output.</summary>
    internal const string ResultFingerprintId = "resultFingerprint/v1";

    private const string ToolName = "Equiv";

    public static SarifLog Write(IReadOnlyList<VerificationResult> results, SarifLog? baseline = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<Result> sarifResults = new(results.Count);
        HashSet<string> currentIdentities = new(StringComparer.Ordinal);
        foreach (VerificationResult result in results)
        {
            currentIdentities.Add(result.Identity.Value);
            sarifResults.Add(ToResult(result, baseline));
        }

        sarifResults.AddRange(BaselineComputer.AbsentResults(currentIdentities, baseline));

        Run run = new()
        {
            Tool = new Tool { Driver = Driver() },
            Results = sarifResults,
        };

        return new SarifLog
        {
            Version = SarifVersion.Current,
            SchemaUri = new Uri(SarifUtilities.SarifSchemaUri),
            Runs = [run],
        };
    }

    private static Result ToResult(VerificationResult result, SarifLog? baseline)
    {
        string fingerprint = ResultFingerprint.Compute(result);
        (string ruleId, FailureLevel level, ResultKind kind) = VerdictRule.Describe(result.Verdict);

        // SARIF 2.1.0 (search "kind" property, ss3.27.9): a result's `level` is only meaningful
        // when `kind` is "fail" ("If kind has any value other than fail, then level SHALL be
        // absent, or SHALL have the value none"); Sarif.Sdk enforces this on write regardless of
        // what Level is set to here. VerdictRule.Describe's level for the other four kinds is not
        // lost: it is EQ001-EQ005's rule-level defaultConfiguration.level in Driver(), which is the
        // spec's own place for a rule's severity absent a per-result override.
        Result sarifResult = new()
        {
            RuleId = ruleId,
            Level = kind == ResultKind.Fail ? level : FailureLevel.None,
            Kind = kind,
            Message = new Message { Text = MessageText(result) },
            BaselineState = BaselineComputer.StateFor(result.Identity.Value, ruleId, fingerprint, baseline),
            PartialFingerprints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ProcedureIdentityFingerprintId] = result.Identity.Value,
                [ResultFingerprintId] = fingerprint,
            },
        };

        if (result.Verdict is Divergent divergent)
        {
            sarifResult.SetProperty("model", CounterexampleText.Dump(divergent.Counterexample));
        }

        return sarifResult;
    }

    /// <summary>
    /// <see cref="Verdict"/> is a closed hierarchy (private protected constructor) with five
    /// members; the final arm covers <see cref="Removed"/>, mirroring <see cref="VerdictRule.Describe"/>.
    /// </summary>
    private static string MessageText(VerificationResult result) => result.Verdict switch
    {
        Equivalent => $"{result.Identity.Value} is equivalent.",
        Divergent divergent => $"{result.Identity.Value} diverges: {CounterexampleText.Dump(divergent.Counterexample)}",
        Unknown unknown => $"{result.Identity.Value} is unknown ({unknown.Reason}): {unknown.Detail}",
        Added => $"{result.Identity.Value} was added.",
        _ => $"{result.Identity.Value} was removed.",
    };

    private static ToolComponent Driver() => new()
    {
        Name = ToolName,
        Rules =
        [
            Rule("EQ001", "Equivalent", "The two procedures are observably equivalent.", FailureLevel.None),
            Rule("EQ002", "Divergent", "The two procedures disagree on some observable.", FailureLevel.Error),
            Rule("EQ003", "Unknown", "Equivalence could not be decided for this procedure pair.", FailureLevel.Warning),
            Rule("EQ004", "Added", "The procedure is present on the modern side only.", FailureLevel.Note),
            Rule("EQ005", "Removed", "The procedure is present on the legacy side only.", FailureLevel.Note),
        ],
    };

    private static ReportingDescriptor Rule(string id, string name, string description, FailureLevel level) => new()
    {
        Id = id,
        Name = name,
        ShortDescription = new MultiformatMessageString { Text = description },
        DefaultConfiguration = new ReportingConfiguration { Level = level },
    };
}
