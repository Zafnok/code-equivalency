using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Execution;
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

    /// <summary>What an Equivalent's <c>proofMethod</c> ends with when its proof used a callee contract (ADR 0036 decision 2; ticket P1-010).</summary>
    private const string ContractSuffix = "+contract";

    /// <summary>The <c>proofMethod</c> of a Divergent the real runtimes showed (ADR 0035 decision 3); never an Equivalent's.</summary>
    private const string ObservedProofMethod = "observed";

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

        // ADR 0040: a result whose pair's runtimes are not known crosses every change the table covers.
        RuntimeInterval runtimes = result.Runtimes ?? RuntimeChangeTable.Load().Coverage;
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(result.Verdict, runtimes);

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
            Message = new Message { Text = MessageText(result, runtimeChange, runtimes) },
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
        SetReplayProperties(sarifResult, result.Replay);
        SetTestingProperty(sarifResult, result.Testing);

        // ADR 0039: under --il-fallback, which lowering the pair's bodies came from.
        if (result.Lowering is { } lowering)
        {
            sarifResult.SetProperty("lowering", lowering);
        }

        SetLocations(sarifResult, result);
        return sarifResult;
    }

    /// <summary>
    /// The result's location, with the route it was matched on for an endpoint (M2-005), and an Unknown's causes as
    /// related locations (ADR 0027 decision 4).
    /// </summary>
    private static void SetLocations(Result sarifResult, VerificationResult result)
    {
        bool isEndpoint = ProcedureIdentityNormalizer.IsEndpoint(result.Identity.Value);
        if (PrimaryLocation(result) is { } location)
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

        if (result.Verdict is Unknown { Causes.IsEmpty: false } unknown)
        {
            sarifResult.RelatedLocations = [.. unknown.Causes.Select(static c => ToSarifLocation(c.Span, c.Reason))];
        }
    }

    /// <summary>
    /// Where the result points (ADR 0027 decision 4): an Unknown's first modern-side cause, so a reviewer reads the line
    /// that caused it, else the procedure. The fingerprints do not depend on it.
    /// </summary>
    private static SourceSpan? PrimaryLocation(VerificationResult result) =>
        (result.Verdict as Unknown)?.Causes.FirstOrDefault(static c => c.Side == Codebase.Modern)?.Span ?? result.Identity.Location;

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
    /// <c>proofMethod</c> and, for a bounded proof over a loop, <c>boundedBy</c>, for a rung 4 or 5 proof the coupling
    /// <c>invariant</c> and for rung 5 its <c>proposedBy</c> (ticket P1-002; ADR 0036), the <c>+contract</c> suffix and
    /// <c>contractsUsed</c> of a proof that used callee contracts (ticket P1-010), an Unknown's <c>unknownReason</c> and
    /// <c>failureRefinement</c> (ADR 0037; ticket P1-013), the <c>ladderTrace</c> of every rung the backend attempted
    /// (VERIFICATION-MODEL.md sections 1 and 5.1; ticket M3-002), and the <c>chcMode</c> rung 4 ran in when it ran
    /// (ticket P1-001).
    /// </summary>
    private static void SetVerdictProperties(Result sarifResult, Verdict verdict)
    {
        switch (verdict)
        {
            case Divergent { Observed: { } observed }:
                sarifResult.SetProperty("proofMethod", ObservedProofMethod);
                sarifResult.SetProperty("model", ObservationText.Model(observed));
                break;
            case Divergent divergent:
                sarifResult.SetProperty("model", CounterexampleText.Dump(divergent.Counterexample));
                break;
            case Equivalent equivalent:
                // Ticket P1-010 (ADR 0036 decision 2): a proof that used callee contracts says so in its method and lists them.
                sarifResult.SetProperty("proofMethod", Name(equivalent.Method) + (equivalent.ContractsUsed.IsEmpty ? string.Empty : ContractSuffix));
                if (!equivalent.ContractsUsed.IsEmpty)
                {
                    sarifResult.SetProperty("contractsUsed", equivalent.ContractsUsed.Select(ContractProperty).ToList());
                }

                if (equivalent.BoundedBy is { } bound)
                {
                    sarifResult.SetProperty("boundedBy", bound);
                }

                if (equivalent.Invariant is { } invariant)
                {
                    sarifResult.SetProperty("invariant", invariant);
                }

                if (equivalent.ProposedBy is { } proposer)
                {
                    sarifResult.SetProperty("proposedBy", proposer);
                }

                break;
            case Unknown unknown:
                SetUnknownProperties(sarifResult, unknown);
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

        if (verdict.Ladder.FirstOrDefault(static s => s.Mode is not null)?.Mode is { } mode)
        {
            sarifResult.SetProperty("chcMode", Name(mode));
        }
    }

    /// <summary>
    /// An Unknown's <c>unknownReason</c> and <c>scope</c>, a line-scoped one's <c>residualClaim</c> (ADR 0029), its
    /// abstraction properties (ADR 0026) and, when the backend ran ADR 0037's queries, <c>failureRefinement</c> (ticket P1-013).
    /// </summary>
    private static void SetUnknownProperties(Result sarifResult, Unknown unknown)
    {
        sarifResult.SetProperty("unknownReason", Name(unknown.Reason));
        sarifResult.SetProperty("scope", Name(unknown.Scope));
        if (unknown.Scope == UnknownScope.Line)
        {
            sarifResult.SetProperty("residualClaim", Unknown.ResidualClaim);
        }

        SetAbstractionProperties(sarifResult, unknown);
        if (unknown.FailureRefinement is { } refinement)
        {
            sarifResult.SetProperty("failureRefinement", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["newFailures"] = RefinementProperty(refinement.NewFailures),
                ["removedFailures"] = RefinementProperty(refinement.RemovedFailures),
            });
        }
    }

    /// <summary>
    /// An <see cref="UnknownReason.Abstraction"/> result's candidate counterexample, rendered as a Divergent's
    /// <c>model</c> is, and the abstractions it depends on, each with its identity, side, for an opaque fragment its
    /// <c>reason</c> (ticket P2-062) and, when known, source span (ADR 0026). Both are left out when absent.
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

    /// <summary>
    /// One of ADR 0037's queries (ticket P1-013): its <c>outcome</c>, <c>none-proved</c>, <c>found</c> or <c>unknown</c>, and for
    /// <c>found</c> the input and both runs as <c>model</c>, rendered as a Divergent's is.
    /// </summary>
    private static Dictionary<string, string> RefinementProperty(RefinementResult result)
    {
        Dictionary<string, string> described = new(StringComparer.Ordinal) { ["outcome"] = Name(result.Outcome) };
        if (result.Model is { } model)
        {
            described["model"] = CounterexampleText.Dump(model);
        }

        return described;
    }

    private static Dictionary<string, object> ContractProperty(ContractUse contract) => new(StringComparer.Ordinal)
    {
        ["callee"] = contract.Callee,
        ["contract"] = contract.Contract,
        ["proposedBy"] = contract.ProposedBy,
    };

    private static Dictionary<string, object> Describe(Abstraction abstraction)
    {
        Dictionary<string, object> described = new(StringComparer.Ordinal)
        {
            ["identity"] = abstraction.Identity.Value,
            ["side"] = Name(abstraction.Side),
        };
        if (abstraction.Reason is { } reason)
        {
            described["reason"] = reason;
        }

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

    /// <summary>An outcome's kind and its canonical JSON text, as the driver wrote it.</summary>
    private static Dictionary<string, string> Describe(ExecutionOutcome outcome) => new(StringComparer.Ordinal)
    {
        ["kind"] = outcome.Kind == OutcomeKind.Threw ? "threw" : "returned",
        ["value"] = outcome.Canonical,
    };

    /// <summary>
    /// A Divergent's replay on both real runtimes (ADR 0035 decision 2; ticket M4-009): <c>replay</c> is <c>reproduced</c>,
    /// <c>not-reproduced</c> or (an EQ006 Divergent's, ticket P2-038) <c>not-applicable</c> with both canonical outcomes as
    /// <c>replayOutcomes</c>, or <c>not-constructible</c> with <c>replayReason</c>. Left out when the run did not replay.
    /// </summary>
    private static void SetReplayProperties(Result sarifResult, ReplayResult? replay)
    {
        switch (replay)
        {
            case null:
                return;
            case { Status: ReplayStatus.Reproduced }:
                sarifResult.SetProperty("replay", "reproduced");
                break;
            case { Legacy: { } legacy, Modern: { } modern }:
                sarifResult.SetProperty("replay", replay.Status == ReplayStatus.NotApplicable ? "not-applicable" : "not-reproduced");
                sarifResult.SetProperty("replayOutcomes", new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["legacy"] = Describe(legacy),
                    ["modern"] = Describe(modern),
                });
                break;
            default:
                sarifResult.SetProperty("replay", "not-constructible");
                sarifResult.SetProperty("replayReason", replay.Reason);
                break;
        }
    }

    /// <summary>
    /// An Unknown's run on generated inputs (ADR 0035 decision 3; ticket P1-008): <c>differentialTesting</c> is the input
    /// count, the species seen and seen once, the Good-Turing discovery probability, the species definition, why it
    /// stopped and the input distribution, or only <c>notConstructible</c> with the reason. Left out when the run did not test.
    /// </summary>
    private static void SetTestingProperty(Result sarifResult, DifferentialTesting? testing)
    {
        switch (testing)
        {
            case null:
                return;
            case { NotConstructible: { } reason }:
                sarifResult.SetProperty("differentialTesting", new Dictionary<string, string>(StringComparer.Ordinal) { ["notConstructible"] = reason });
                break;
            default:
                sarifResult.SetProperty("differentialTesting", new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["inputs"] = testing.Inputs,
                    ["species"] = testing.Species,
                    ["singletons"] = testing.Singletons,
                    ["discoveryProbability"] = testing.DiscoveryProbability,
                    ["speciesDefinition"] = DifferentialTesting.SpeciesDefinition,
                    ["stoppedBy"] = testing.StoppedBy == TestingStop.Target ? "target" : "budget",
                    ["distribution"] = DifferentialTesting.Distribution,
                });
                break;
        }
    }

    /// <summary>The spelling the census uses for a side: <c>legacy</c>, <c>modern</c>.</summary>
    /// <summary>
    /// One side's <c>run.properties.runtimes</c> list (ADR 0040; ticket P2-053): an object per loaded project, with its
    /// <c>project</c>, <c>runtime</c> and <c>source</c>.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> RuntimesProperty(IEnumerable<(string Project, string Runtime, string Source)> runtimes) =>
        [.. runtimes.Select(static r => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["project"] = r.Project,
            ["runtime"] = r.Runtime,
            ["source"] = r.Source,
        })];

    internal static string Name(Codebase side) => side == Codebase.Legacy ? "legacy" : "modern";

    /// <summary>
    /// The spelling VERIFICATION-MODEL.md sections 1 and 5.1 use for a proof: <c>bounded</c>, <c>lockstep-induction</c>,
    /// <c>k-induction</c>, <c>chc</c>, <c>llm-invariant</c>, <c>trace-invariant</c>, <c>congruence</c>.
    /// </summary>
    internal static string Name(ProofMethod method) => method switch
    {
        ProofMethod.Bounded => "bounded",
        ProofMethod.LockstepInduction => "lockstep-induction",
        ProofMethod.KInduction => "k-induction",
        ProofMethod.Chc => "chc",
        ProofMethod.LlmInvariant => "llm-invariant",
        ProofMethod.TraceInvariant => "trace-invariant",
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
        UnknownReason.ChcTimeout => "chc-timeout",
        UnknownReason.ChcSpurious => "chc-spurious",
        UnknownReason.NoInvariant => "no-invariant",
        _ => "unbound",
    };

    /// <summary>The spelling VERIFICATION-MODEL.md section 5.1 uses for rung 4's theory: <c>int</c>, <c>bitvector</c>.</summary>
    internal static string Name(ChcMode mode) => mode == ChcMode.Integers ? "int" : "bitvector";

    /// <summary>The spelling VERIFICATION-MODEL.md section 6 uses for a failure-refinement outcome (ADR 0037).</summary>
    internal static string Name(RefinementOutcome outcome) => outcome switch
    {
        RefinementOutcome.NoneProved => "none-proved",
        RefinementOutcome.Found => "found",
        _ => "unknown",
    };

    /// <summary>The spelling VERIFICATION-MODEL.md section 6 uses for a scope: <c>line</c>, <c>method</c>.</summary>
    internal static string Name(UnknownScope scope) => scope == UnknownScope.Line ? "line" : "method";

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
    /// declaring file plus the identifier's line/column region, and for a related location the <paramref name="message"/>
    /// saying why it is related.
    /// </summary>
    private static Location ToSarifLocation(SourceSpan span, string? message = null) => new()
    {
        Message = message is null ? null : new Message { Text = message },
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
    /// <paramref name="runtimeChange"/> is non-null only for an EQ006 <see cref="Divergent"/>, whose message names both ends of
    /// <paramref name="runtimes"/> (ADR 0040; ticket P2-055). An Equivalent that assumed a
    /// callee pair this run did not prove says so in one more sentence (ADR 0019).
    /// </summary>
    private static string MessageText(VerificationResult result, RuntimeChange? runtimeChange, RuntimeInterval runtimes) =>
        result.Testing is { NotConstructible: null } testing ? WithTestedSentence(VerdictText(result, runtimeChange, runtimes), testing) : VerdictText(result, runtimeChange, runtimes);

    /// <summary>
    /// Ticket P1-008 criterion 3: a tested Unknown's message ends with one sentence stating the input count and the
    /// discovery probability, labelled as a likelihood under the generators' distribution.
    /// </summary>
    private static string WithTestedSentence(string text, DifferentialTesting testing) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{text}{(text.EndsWith('.') ? string.Empty : ".")} Tested on {testing.Inputs} inputs; estimated chance the next input shows new behaviour: {testing.DiscoveryProbability:0.######} (equiv generators, not a proof).");

    private static string VerdictText(VerificationResult result, RuntimeChange? runtimeChange, RuntimeInterval runtimes) => result.Verdict switch
    {
        Equivalent when !result.UnprovenAssumptions.IsEmpty =>
            $"{result.Identity.Value} is equivalent. Assumes callees equivalent; not proved for: {string.Join(", ", result.UnprovenAssumptions)}.",
        Equivalent => $"{result.Identity.Value} is equivalent.",
        Divergent { Observed: { } observed } => $"{result.Identity.Value} diverges on the real runtimes: {ObservationText.Dump(observed)}",
        Divergent divergent when runtimeChange is not null =>
            $"{result.Identity.Value} diverges via a runtime-changed API between {runtimes.Older} and {runtimes.Newer} ({runtimeChange.Reason} {runtimeChange.Url.OriginalString}): {CounterexampleText.Dump(divergent.Counterexample)}",
        Divergent divergent => $"{result.Identity.Value} diverges: {CounterexampleText.Dump(divergent.Counterexample)}",
        Unknown { Scope: UnknownScope.Line } unknown =>
            $"{result.Identity.Value} is unknown ({unknown.Reason}): {unknown.Detail}. It is equivalent on every input that reaches none of the related locations.",
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
            Rule("EQ006", "RuntimeChangedDivergence", "The two procedures disagree because a call uses a BCL member whose behaviour differs between the two sides' runtimes.", FailureLevel.Error),
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
