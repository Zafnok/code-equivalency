using System.Collections.Immutable;

using Equiv.Core.Matching;
using Equiv.Core.RuntimeChanges;
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

    /// <param name="results">One SARIF result each, in order, ahead of any baseline carry-overs.</param>
    /// <param name="baseline">The previous log <c>baselineState</c> is computed against.</param>
    /// <param name="runProperties">
    /// Go into the run's property bag in the order given, each value serialised as JSON (ADR 0006: extra data travels
    /// in SARIF <c>properties</c>, never in a parallel schema). The CLI puts the lowering census and the analysed line
    /// counts there (ticket M3-014).
    /// </param>
    /// <param name="notifications">
    /// Tool-execution notifications, such as a skipped project (ADR 0029). Given any, the run has one invocation,
    /// and <c>executionSuccessful</c> is false when one of them is an <c>error</c>.
    /// </param>
    /// <param name="unverified">
    /// Identities the run could not verify, listed in <c>run.properties.unverified</c>. A baseline result for one of
    /// them is carried as <c>unchanged</c> with <c>properties.unverified: true</c>, never <c>absent</c> (ADRs 0023, 0029).
    /// </param>
    public static SarifLog Write(
        IReadOnlyList<VerificationResult> results,
        SarifLog? baseline = null,
        IReadOnlyDictionary<string, object>? runProperties = null,
        IReadOnlyList<Notification>? notifications = null,
        IReadOnlyList<ProcedureIdentity>? unverified = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        notifications ??= [];
        List<string> unverifiedIdentities = [.. (unverified ?? []).Select(static i => i.Value).Distinct(StringComparer.Ordinal)];

        List<Result> sarifResults = new(results.Count);
        HashSet<string> currentIdentities = new(StringComparer.Ordinal);
        foreach (VerificationResult result in results)
        {
            currentIdentities.Add(result.Identity.Value);
            sarifResults.Add(ToResult(result, baseline));
        }

        sarifResults.AddRange(BaselineComputer.AbsentResults(currentIdentities, baseline, new HashSet<string>(unverifiedIdentities, StringComparer.Ordinal)));

        Run run = new()
        {
            Tool = new Tool { Driver = Driver() },
            Results = sarifResults,
        };

        foreach ((string name, object value) in runProperties ?? new Dictionary<string, object>(StringComparer.Ordinal))
        {
            run.SetProperty(name, value);
        }

        if (notifications.Count > 0)
        {
            run.Invocations =
            [
                new Invocation
                {
                    ExecutionSuccessful = notifications.All(static n => n.Level != FailureLevel.Error),
                    ToolExecutionNotifications = [.. notifications],
                },
            ];
        }

        if (unverifiedIdentities.Count > 0)
        {
            run.SetProperty("unverified", unverifiedIdentities);
        }

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
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(result.Verdict);

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
            Message = new Message { Text = MessageText(result, runtimeChange) },
            BaselineState = BaselineComputer.StateFor(result.Identity.Value, ruleId, fingerprint, baseline),
            PartialFingerprints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ProcedureIdentityFingerprintId] = result.Identity.Value,
                [ResultFingerprintId] = fingerprint,
            },
        };

        SetVerdictProperties(sarifResult, result.Verdict);

        // Sarif.Sdk's Result has no per-result help-link property (only ReportingDescriptor.HelpUri,
        // one fixed value per rule); EQ006's url is specific to the matched member, so it travels as
        // a custom property alongside "model", the same way a Divergent's counterexample does.
        if (runtimeChange is not null)
        {
            sarifResult.SetProperty("helpUri", runtimeChange.Url.OriginalString);
        }

        // ADR 0020: an Equivalent says which catalogue entries it rests on. ADR 0019: and which callee pairs it assumed.
        SetListProperty(sarifResult, "equivalencesApplied", result.EquivalencesApplied);
        SetListProperty(sarifResult, "assumedCallees", result.AssumedCallees);
        SetListProperty(sarifResult, "unprovenAssumptions", result.UnprovenAssumptions);

        bool isEndpoint = ProcedureIdentityNormalizer.IsEndpoint(result.Identity.Value);
        if (result.Identity.Location is { } location)
        {
            Location sarifLocation = ToSarifLocation(location);
            if (isEndpoint)
            {
                sarifLocation.LogicalLocations = [EndpointLogicalLocation(result.Identity.Value)];
            }

            sarifResult.Locations = [sarifLocation];
        }
        else if (isEndpoint)
        {
            sarifResult.Locations = [new Location { LogicalLocations = [EndpointLogicalLocation(result.Identity.Value)] }];
        }

        return sarifResult;
    }

    /// <summary>A string-array result property, left out when <paramref name="values"/> is empty.</summary>
    private static void SetListProperty(Result sarifResult, string name, ImmutableArray<string> values)
    {
        if (!values.IsEmpty)
        {
            sarifResult.SetProperty(name, values.ToList());
        }
    }

    /// <summary>
    /// The verdict's payload as result properties: a Divergent's counterexample (<c>model</c>), an Equivalent's
    /// <c>proofMethod</c> and, for a bounded proof over a loop, <c>boundedBy</c>, an Unknown's <c>unknownReason</c>,
    /// and the <c>ladderTrace</c> of every rung the backend attempted (VERIFICATION-MODEL.md sections 1 and 5.1;
    /// ticket M3-002).
    /// </summary>
    private static void SetVerdictProperties(Result sarifResult, Verdict verdict)
    {
        switch (verdict)
        {
            case Divergent divergent:
                sarifResult.SetProperty("model", CounterexampleText.Dump(divergent.Counterexample));
                break;
            case Equivalent equivalent:
                sarifResult.SetProperty("proofMethod", Name(equivalent.Method));
                if (equivalent.BoundedBy is { } bound)
                {
                    sarifResult.SetProperty("boundedBy", bound);
                }

                break;
            case Unknown unknown:
                sarifResult.SetProperty("unknownReason", Name(unknown.Reason));
                SetAbstractionProperties(sarifResult, unknown);
                break;
        }

        if (!verdict.Ladder.IsEmpty)
        {
            sarifResult.SetProperty("ladderTrace", verdict.Ladder.Select(static s => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["rung"] = Name(s.Rung),
                ["outcome"] = Name(s.Outcome),
                ["detail"] = s.Detail,
            }).ToList());
        }
    }

    /// <summary>
    /// An <see cref="UnknownReason.Abstraction"/> result's candidate counterexample, rendered as a Divergent's
    /// <c>model</c> is, and the abstractions it depends on, each with its identity, side and, when known, source span
    /// (ADR 0026). Both are left out when absent.
    /// </summary>
    private static void SetAbstractionProperties(Result sarifResult, Unknown unknown)
    {
        if (unknown.Candidate is { } candidate)
        {
            sarifResult.SetProperty("candidateCounterexample", CounterexampleText.Dump(candidate));
        }

        if (!unknown.Abstractions.IsEmpty)
        {
            sarifResult.SetProperty("abstractions", unknown.Abstractions.Select(static a => Describe(a)).ToList());
        }
    }

    private static Dictionary<string, object> Describe(Abstraction abstraction)
    {
        Dictionary<string, object> described = new(StringComparer.Ordinal)
        {
            ["identity"] = abstraction.Identity.Value,
            ["side"] = Name(abstraction.Side),
        };
        if (abstraction.Span is { } span)
        {
            described["span"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["path"] = span.Path,
                ["startLine"] = span.StartLine,
                ["startColumn"] = span.StartColumn,
                ["endLine"] = span.EndLine,
                ["endColumn"] = span.EndColumn,
            };
        }

        return described;
    }

    /// <summary>The spelling the census uses for a side: <c>legacy</c>, <c>modern</c>.</summary>
    internal static string Name(Codebase side) => side == Codebase.Legacy ? "legacy" : "modern";

    /// <summary>
    /// The spelling VERIFICATION-MODEL.md sections 1 and 5.1 use for a proof: <c>bounded</c>, <c>lockstep-induction</c>,
    /// <c>k-induction</c>, <c>congruence</c>.
    /// </summary>
    internal static string Name(ProofMethod method) => method switch
    {
        ProofMethod.Bounded => "bounded",
        ProofMethod.LockstepInduction => "lockstep-induction",
        ProofMethod.KInduction => "k-induction",
        _ => "congruence",
    };

    internal static string Name(UnknownReason reason) => reason switch
    {
        UnknownReason.Timeout => "timeout",
        UnknownReason.Opaque => "opaque",
        UnknownReason.UnmatchedOverload => "unmatched-overload",
        UnknownReason.UnalignedLoop => "unaligned-loop",
        UnknownReason.Recursion => "recursion",
        UnknownReason.Abstraction => "abstraction",
        _ => "unbound",
    };

    internal static string Name(RungOutcome outcome) => outcome switch
    {
        RungOutcome.Proved => "proved",
        RungOutcome.Refuted => "refuted",
        RungOutcome.Inconclusive => "inconclusive",
        RungOutcome.Timeout => "timeout",
        _ => "not-applicable",
    };

    /// <summary>
    /// M2-005 acceptance criterion 4: an endpoint-matched procedure's result carries a
    /// <c>logicalLocations</c> entry naming the route it was matched on, alongside (or instead of, when
    /// there is no source span) its physical <see cref="Location"/>.
    /// </summary>
    private static LogicalLocation EndpointLogicalLocation(string identityValue) => new() { Kind = "endpoint", FullyQualifiedName = identityValue };

    /// <summary>
    /// A <see cref="SourceSpan"/> (1-based, ticket M2-002) as a SARIF <see cref="Location"/>: the
    /// declaring file plus the identifier's line/column region.
    /// </summary>
    private static Location ToSarifLocation(SourceSpan span) => new()
    {
        PhysicalLocation = new PhysicalLocation
        {
            ArtifactLocation = new ArtifactLocation { Uri = new Uri(span.Path.Replace('\\', '/'), UriKind.RelativeOrAbsolute) },
            Region = new Region
            {
                StartLine = span.StartLine,
                StartColumn = span.StartColumn,
                EndLine = span.EndLine,
                EndColumn = span.EndColumn,
            },
        },
    };

    /// <summary>
    /// <see cref="Verdict"/> is a closed hierarchy (private protected constructor) with five
    /// members; the final arm covers <see cref="Removed"/>, mirroring <see cref="VerdictRule.Describe"/>.
    /// <paramref name="runtimeChange"/> is non-null only for an EQ006 <see cref="Divergent"/>. An Equivalent that assumed a
    /// callee pair this run did not prove says so in one more sentence (ADR 0019).
    /// </summary>
    private static string MessageText(VerificationResult result, RuntimeChange? runtimeChange) => result.Verdict switch
    {
        Equivalent when !result.UnprovenAssumptions.IsEmpty =>
            $"{result.Identity.Value} is equivalent. Assumes callees equivalent; not proved for: {string.Join(", ", result.UnprovenAssumptions)}.",
        Equivalent => $"{result.Identity.Value} is equivalent.",
        Divergent divergent when runtimeChange is not null =>
            $"{result.Identity.Value} diverges via a runtime-changed API ({runtimeChange.Reason} {runtimeChange.Url.OriginalString}): {CounterexampleText.Dump(divergent.Counterexample)}",
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
            Rule("EQ006", "RuntimeChangedDivergence", "The two procedures disagree because a call uses a BCL member whose behaviour differs between .NET Framework and .NET.", FailureLevel.Error),
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
