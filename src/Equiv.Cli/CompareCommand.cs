using System.Collections.Immutable;
using System.CommandLine;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Newtonsoft.Json;

namespace Equiv.Cli;

/// <summary>
/// <c>equiv compare</c>: option definitions plus the pipeline (ARCHITECTURE.md's data-flow diagram)
/// that turns them into an exit code. <see cref="Create"/> wires production dependencies (a real
/// <see cref="FileReportSink"/>); <see cref="Run"/> is the testable core, taking already-parsed
/// options and every collaborator as a parameter.
/// </summary>
internal static class CompareCommand
{
    public static Command Create(IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend)
    {
        ArgumentNullException.ThrowIfNull(frontends);
        ArgumentNullException.ThrowIfNull(backend);

        Option<string> legacyOption = new("--legacy") { Required = true };
        Option<string> modernOption = new("--modern") { Required = true };
        Option<string> outOption = new("--out") { DefaultValueFactory = _ => "equiv.sarif" };
        Option<string?> baselineOption = new("--baseline");
        Option<string?> configOption = new("--config");
        Option<string?> failOnOption = new("--fail-on");
        failOnOption.AcceptOnlyFromAmong("divergent", "unknown");
        Option<bool> dryRunOption = new("--dry-run");
        Option<bool> lowerOnlyOption = new("--lower-only");

        Command command = new("compare")
        {
            legacyOption, modernOption, outOption, baselineOption, configOption, failOnOption, dryRunOption, lowerOnlyOption,
        };

        command.SetAction(parseResult => Run(
            new CompareOptions(
                parseResult.GetValue(legacyOption)!,
                parseResult.GetValue(modernOption)!,
                parseResult.GetValue(outOption)!,
                parseResult.GetValue(baselineOption),
                parseResult.GetValue(configOption),
                parseResult.GetValue(failOnOption),
                parseResult.GetValue(dryRunOption),
                parseResult.GetValue(lowerOnlyOption)),
            frontends,
            backend,
            new FileReportSink(parseResult.GetValue(outOption)!)));

        return command;
    }

    public static int Run(
        CompareOptions options,
        IReadOnlyList<ILanguageFrontend> frontends,
        IVerificationBackend backend,
        IReportSink sink)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(frontends);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(sink);

        if (!File.Exists(options.LegacyPath) || !File.Exists(options.ModernPath))
        {
            Console.Error.WriteLine($"error: file not found (legacy={options.LegacyPath}, modern={options.ModernPath})");
            return ExitCodes.UsageError;
        }

        if (options.LowerOnly && (options.BaselinePath is not null || options.FailOn is not null))
        {
            Console.Error.WriteLine("error: --lower-only cannot be combined with --baseline or --fail-on");
            return ExitCodes.UsageError;
        }

        ILanguageFrontend? frontend = FrontendRouter.Route(frontends, options.LegacyPath, options.ModernPath);
        if (frontend is null)
        {
            Console.Error.WriteLine($"error: no frontend supports both legacy={options.LegacyPath} and modern={options.ModernPath}");
            return ExitCodes.UsageError;
        }

        if (options.DryRun)
        {
            Console.WriteLine($"route: {frontend.Language} legacy={options.LegacyPath} modern={options.ModernPath} out={options.OutPath}");
        }

        if (!TryLoadInputs(options.BaselinePath, options.ConfigPath, out EquivConfig config, out SarifLog? baseline, out int inputErrorExitCode))
        {
            return inputErrorExitCode;
        }

        FrontendAnalysis analysis;
        try
        {
            analysis = frontend.Analyze(options.LegacyPath, options.ModernPath, config, CancellationToken.None);
        }
        catch (FrontendLoadException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitCodes.LoadFailure;
        }

        // Two numbers, never a total: the licence measures each codebase on its own (ticket M3-014).
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"analysed lines of code: legacy={analysis.Lines.Legacy} modern={analysis.Lines.Modern}"));
        return options.DryRun ? ExitCodes.Success : Report(options, analysis, config, backend, baseline, sink);
    }

    /// <summary>
    /// The lowering census, the analysed line counts and the projects each solution does not build go into the run's
    /// property bag on every run (ADR 0027; tickets M3-014, P2-013), and every skipped project is a notification (ADR 0029). <c>--lower-only</c> stops there: the
    /// Added and Removed results, no backend call, exit 0 unless a C# project was skipped.
    /// </summary>
    private static int Report(
        CompareOptions options, FrontendAnalysis analysis, EquivConfig config, IVerificationBackend backend, SarifLog? baseline, IReportSink sink)
    {
        MatchResult matchResult = analysis.Match;
        List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered = Lowered(matchResult);
        LoweringCensus census = LoweringCensus.Compute(
            [.. lowered.Select(static p => (p.Old, p.New, IsCongruent(p.Pair, p.Old, p.New)))],
            removed: matchResult.Removed.Length,
            added: matchResult.Added.Length,
            projectsSkipped: new SideCounts(matchResult.LegacySkipped.Length, matchResult.ModernSkipped.Length),
            unlowered: matchResult.LoweringFailures.Length);

        // P2-011: a pair the frontend could not lower takes the same path as a pair whose verification throws (ADR 0023).
        List<Notification> pairFailures = [.. matchResult.LoweringFailures.Select(static f => PairFailure("Lowering", f.Old, f.New, f.Exception))];
        List<ProcedureIdentity> unverifiedPairs = [.. matchResult.LoweringFailures.Select(static f => f.New)];
        (List<VerificationResult> verified, List<Notification> verifyFailures, List<ProcedureIdentity> unverifiedVerified) =
            options.LowerOnly ? ([], [], []) : Verified(lowered, backend, config);
        pairFailures.AddRange(verifyFailures);
        unverifiedPairs.AddRange(unverifiedVerified);
        List<VerificationResult> results = WithAssumptions(verified, lowered, matchResult);
        if (!options.LowerOnly)
        {
            census = census with { UnknownByScope = ScopeCounts.Of(results) };
        }

        results.AddRange(matchResult.Added.Select(static identity => new VerificationResult(identity, new Added())));
        results.AddRange(matchResult.Removed.Select(static identity => new VerificationResult(identity, new Removed())));

        (List<Notification> skippedProjectNotifications, List<ProcedureIdentity> skippedProjectProcedures) = SkippedProjects(matchResult);
        List<Notification> notifications = [.. pairFailures, .. skippedProjectNotifications];
        List<ProcedureIdentity> unverified = [.. unverifiedPairs, .. skippedProjectProcedures];
        SarifLog log = SarifReportWriter.Write(
            results,
            baseline,
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["loweringCensus"] = census.ToProperty(),
                ["analysedLinesOfCode"] = LoweringCensus.Property(new SideCounts(analysis.Lines.Legacy, analysis.Lines.Modern)),
                ["projectsNotBuilt"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["legacy"] = (List<string>)[.. analysis.LegacyNotBuilt],
                    ["modern"] = (List<string>)[.. analysis.ModernNotBuilt],
                },
            },
            notifications,
            unverified);
        sink.Write(log);

        // ADR 0023: a pair that failed to verify outranks a skipped project, which outranks a verdict, because each
        // makes the result set more incomplete than the last (ARCHITECTURE.md's exit-code precedence).
        bool anyPairFailed = pairFailures.Count > 0;
        bool anyProjectSkipped = skippedProjectNotifications.Exists(static n => n.Level == FailureLevel.Error);
        return (anyPairFailed, anyProjectSkipped, options.LowerOnly) switch
        {
            (true, _, _) => ExitCodes.InternalError,
            (false, true, _) => ExitCodes.LoadFailure,
            (false, false, true) => ExitCodes.Success,
            _ => DecideExitCode(results, log, options.FailOn),
        };
    }

    /// <summary>
    /// ADR 0029 decision 1: one tool-execution notification per project the frontend skipped, on stderr as well as in
    /// the SARIF, and every procedure that is unverified because of it. A skipped C# project is an <c>error</c>, which
    /// makes the run incomplete and outranks any verdict (<see cref="ExitCodes"/>); a project in another language is a
    /// <c>warning</c>.
    /// </summary>
    private static (List<Notification> Notifications, List<ProcedureIdentity> Unverified) SkippedProjects(MatchResult matchResult)
    {
        List<Notification> notifications = [];
        List<ProcedureIdentity> unverified = [];
        foreach ((string side, UnverifiedProject project) in matchResult.LegacySkipped.Select(static p => ("legacy", p))
            .Concat(matchResult.ModernSkipped.Select(static p => ("modern", p))))
        {
            FailureLevel level = project.IsCSharp ? FailureLevel.Error : FailureLevel.Warning;
            string subject = project.Name.Length > 0
                ? $"{side} project '{project.Name}' (assembly '{project.AssemblyName}')"
                : $"a {side} project the workspace did not name";
            string text = $"{subject} was skipped: {string.Join("; ", project.Diagnostics)}";
            Console.Error.WriteLine($"{(project.IsCSharp ? "error" : "warning")}: {text}");
            notifications.Add(new Notification { Level = level, Message = new Message { Text = text } });
            unverified.AddRange(project.Procedures);
        }

        return (notifications, unverified);
    }

    /// <summary>
    /// Checks <paramref name="configPath"/>/<paramref name="baselinePath"/> exist (criterion 3's
    /// "Missing file: exit 3" is not limited to <c>--legacy</c>/<c>--modern</c>) and loads both,
    /// catching a malformed (not just wrong-shaped) <c>--config</c> file as a usage error too.
    /// </summary>
    private static bool TryLoadInputs(string? baselinePath, string? configPath, out EquivConfig config, out SarifLog? baseline, out int exitCode)
    {
        config = EquivConfig.Default;
        baseline = null;
        exitCode = ExitCodes.Success;

        if (configPath is not null && !File.Exists(configPath))
        {
            Console.Error.WriteLine($"error: file not found (config={configPath})");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        if (baselinePath is not null && !File.Exists(baselinePath))
        {
            Console.Error.WriteLine($"error: file not found (baseline={baselinePath})");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        try
        {
            config = LoadConfig(configPath);
        }
        catch (EquivConfigParseException exception)
        {
            Console.Error.WriteLine(exception.Message);
            exitCode = ExitCodes.UsageError;
            return false;
        }

        if (baselinePath is null)
        {
            return true;
        }

        try
        {
            baseline = SarifLog.Load(baselinePath);
            return true;
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"error: '{baselinePath}' is not a valid SARIF log: {exception.Message}");
            exitCode = ExitCodes.UsageError;
            return false;
        }
    }

    private static EquivConfig LoadConfig(string? configPath)
    {
        if (configPath is null)
        {
            return EquivConfig.Default;
        }

        EquivConfigResult result = EquivConfigLoader.Load(File.ReadAllText(configPath));
        foreach (EquivConfigDiagnostic diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine($"warning: {diagnostic.Id} {diagnostic.Path}: {diagnostic.Message}");
        }

        return result.Config;
    }

    /// <summary>
    /// Both lowered bodies of every matched pair. A frontend must attach them (ticket M2-003); a pair without
    /// one is a frontend bug, not an input problem.
    /// </summary>
    private static List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> Lowered(MatchResult matchResult) =>
        [.. matchResult.Pairs.Select(static pair => (
            pair,
            pair.OldBody ?? throw new InvalidOperationException($"The frontend matched {pair.Old.Value} without lowering its legacy body."),
            pair.NewBody ?? throw new InvalidOperationException($"The frontend matched {pair.New.Value} without lowering its modern body.")))];

    /// <summary>
    /// Every matched pair that is neither unbound (ADR 0029) nor congruent (ADR 0024) goes to <paramref name="backend"/> with
    /// both lowered bodies. A crash on one pair (a
    /// replay mismatch M3-001 treats as an encoder bug, a <c>Z3Exception</c>) does not end the run (ADR 0023): the
    /// pair gets no <see cref="VerificationResult"/>, an <c>error</c> notification naming both identities and
    /// carrying the exception, and its identity in the returned unverified list; every other pair is still
    /// verified. <see cref="OperationCanceledException"/> and <see cref="OutOfMemoryException"/> propagate unchanged.
    /// </summary>
    private static (List<VerificationResult> Results, List<Notification> Failures, List<ProcedureIdentity> Unverified) Verified(
        List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered, IVerificationBackend backend, EquivConfig config)
    {
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        List<VerificationResult> results = [];
        List<Notification> failures = [];
        List<ProcedureIdentity> unverified = [];

        foreach ((ProcedurePair pair, IrProcedure old, IrProcedure @new) in lowered)
        {
            // ADR 0029 decision 2: erroneous code is Unknown(Unbound) without asking the solver; it is never evidence of equivalence.
            string unbound = string.Join("; ", UnboundCauses("legacy", old).Concat(UnboundCauses("modern", @new)));
            if (unbound.Length > 0)
            {
                results.Add(new VerificationResult(pair.New, new Unknown(UnknownReason.Unbound, unbound)) { EquivalencesApplied = pair.EquivalencesApplied });
                continue;
            }

            // ADR 0024: identical bound code is Equivalent without the solver.
            if (IsCongruent(pair, old, @new))
            {
                results.Add(new VerificationResult(pair.New, new Equivalent(ProofMethod.Congruence)) { EquivalencesApplied = pair.EquivalencesApplied });
                continue;
            }

            try
            {
                results.Add(new VerificationResult(pair.New, backend.Verify(old, @new, options)) { EquivalencesApplied = pair.EquivalencesApplied });
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                failures.Add(PairFailure("Verifying", pair.Old, pair.New, exception));
                unverified.Add(pair.New);
            }
        }

        return (results, failures, unverified);
    }

    /// <summary>
    /// ADR 0024 decision 1: the two bound fingerprints are equal and not runtime-sensitive. A body with erroneous code is
    /// never congruent, because two error symbols with the same name are no evidence of the same behaviour (ADR 0029 decision 2).
    /// </summary>
    internal static bool IsCongruent(ProcedurePair pair, IrProcedure old, IrProcedure @new) =>
        pair.OldFingerprint is { RuntimeSensitive: false } fingerprint
        && fingerprint == pair.NewFingerprint
        && !UnboundCauses("legacy", old).Any()
        && !UnboundCauses("modern", @new).Any();

    /// <summary>
    /// ADR 0019, once every verdict is known: each result of a lowered pair lists the matched pairs (lowered or not) that
    /// either body calls, other than itself, as <see cref="VerificationResult.AssumedCallees"/>, sorted and distinct, and
    /// those whose own result in this run is not Equivalent as <see cref="VerificationResult.UnprovenAssumptions"/>. No
    /// verdict changes.
    /// </summary>
    private static List<VerificationResult> WithAssumptions(
        List<VerificationResult> verified, List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered, MatchResult matchResult)
    {
        HashSet<string> matched = new(
            matchResult.Pairs.Select(static p => p.New.Value).Concat(matchResult.LoweringFailures.Select(static f => f.New.Value)),
            StringComparer.Ordinal);
        Dictionary<string, Verdict> verdicts = verified.ToDictionary(static r => r.Identity.Value, static r => r.Verdict, StringComparer.Ordinal);
        Dictionary<string, (IrProcedure Old, IrProcedure New)> bodies = lowered.ToDictionary(static p => p.Pair.New.Value, static p => (p.Old, p.New), StringComparer.Ordinal);
        return
        [
            .. verified.Select(result =>
            {
                (IrProcedure old, IrProcedure @new) = bodies[result.Identity.Value];
                ImmutableArray<string> assumed =
                [
                    .. Callees(old).Concat(Callees(@new))
                        .Where(callee => matched.Contains(callee) && !string.Equals(callee, result.Identity.Value, StringComparison.Ordinal))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal),
                ];
                return result with
                {
                    AssumedCallees = assumed,
                    UnprovenAssumptions = [.. assumed.Where(callee => verdicts.GetValueOrDefault(callee) is not Equivalent)],
                };
            }),
        ];
    }

    private static IEnumerable<string> Callees(IrProcedure body) =>
        body.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>().Select(static call => call.Callee.Value);

    /// <summary>
    /// ADR 0023's record of a pair the tool failed on: an <c>error</c> notification naming both identities and carrying
    /// the exception, also written to stderr. <paramref name="stage"/> says what failed (<c>Lowering</c>, <c>Verifying</c>).
    /// </summary>
    private static Notification PairFailure(string stage, ProcedureIdentity old, ProcedureIdentity @new, Exception exception)
    {
        string text = $"{stage} {old.Value} against {@new.Value} failed: {exception.Message}";
        Console.Error.WriteLine($"error: {text}");
        return new Notification
        {
            Level = FailureLevel.Error,
            Message = new Message { Text = text },
            Exception = new ExceptionData
            {
                Kind = exception.GetType().FullName,
                Message = exception.Message,
                Stack = Stack.CreateStacks(exception).FirstOrDefault(),
            },
        };
    }

    /// <summary>Each <see cref="Unknown.UnboundOpaqueReason"/> opaque in <paramref name="body"/>, as <c>side: unbound at path line:column</c>.</summary>
    private static IEnumerable<string> UnboundCauses(string side, IrProcedure body) =>
        body.Blocks
            .SelectMany(static b => b.Instructions)
            .OfType<IrOpaque>()
            .Where(static o => string.Equals(o.Reason, Unknown.UnboundOpaqueReason, StringComparison.Ordinal))
            .Select(o => string.Create(CultureInfo.InvariantCulture, $"{side}: unbound at {o.Span.Path} {o.Span.StartLine}:{o.Span.StartColumn}"));

    private static int DecideExitCode(List<VerificationResult> results, SarifLog log, string? failOn)
    {
        IList<Result> sarifResults = log.Runs[0].Results;

        bool anyNewDivergent = false;
        bool anyNewUnknown = false;
        for (int i = 0; i < results.Count; i++)
        {
            if (sarifResults[i].BaselineState != BaselineState.New)
            {
                continue;
            }

            switch (results[i].Verdict)
            {
                case Divergent:
                    anyNewDivergent = true;
                    break;
                case Unknown:
                    anyNewUnknown = true;
                    break;
            }
        }

        return anyNewDivergent switch
        {
            true => ExitCodes.Divergent,
            false when string.Equals(failOn, "unknown", StringComparison.Ordinal) && anyNewUnknown => ExitCodes.UnknownPresent,
            _ => ExitCodes.Success,
        };
    }
}
