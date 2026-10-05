using System.Collections.Immutable;
using System.CommandLine;
using System.Globalization;

using Equiv.Cli.Progress;
using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Reporting;
using Equiv.Core.RuntimeChanges;
using Equiv.Core.Verdicts;
using Equiv.Execute;
using Equiv.Execute.Testing;
using Equiv.Verify.Cvc5;

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
    private const string LegacySide = "legacy";
    private const string ModernSide = "modern";

    /// <summary>The descriptor id of the notification <see cref="UncoveredRuntimes"/> writes (ADR 0040; ticket P2-055).</summary>
    internal const string UncoveredRuntimeRange = "uncovered-runtime-range";

    /// <summary>The descriptor id of the notification <see cref="ContradictedConditions"/> writes (ADR 0048; ticket P1-022).</summary>
    internal const string ContradictedCondition = "contradicted-condition";

    /// <summary>How many times <see cref="DeleteTemporary"/> tries before it leaves the folder behind.</summary>
    internal const int DeleteAttempts = 5;

    /// <summary>The <c>compare</c> command; <paramref name="execution"/> is where <c>--execute</c> runs, this machine when null.</summary>
    public static Command Create(IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend, ExecutionEnvironment? execution = null)
    {
        ArgumentNullException.ThrowIfNull(frontends);
        ArgumentNullException.ThrowIfNull(backend);

        Option<string> legacyOption = new("--legacy", "--before") { Required = true };
        Option<string> modernOption = new("--modern", "--after") { Required = true };
        Option<string> outOption = new("--out") { DefaultValueFactory = _ => "equiv.sarif" };
        Option<string?> baselineOption = new("--baseline");
        Option<string?> configOption = new("--config");
        Option<string?> failOnOption = Among(new Option<string?>("--fail-on"), "divergent", "unknown");
        Option<bool> dryRunOption = new("--dry-run");
        Option<bool> lowerOnlyOption = new("--lower-only");
        Option<bool> executeOption = new("--execute");
        Option<bool> chcIntModeOption = new("--chc-int-mode") { DefaultValueFactory = _ => true };
        Option<string?> testTargetOption = new("--test-target");
        testTargetOption.Validators.Add(static result => Validate(result, TestingOptions.TryParse(result.GetValueOrDefault<string?>(), budget: null, out string error), error));
        Option<string?> testBudgetOption = new("--test-budget");
        testBudgetOption.Validators.Add(static result => Validate(result, TestingOptions.TryParse(target: null, result.GetValueOrDefault<string?>(), out string error), error));
        Option<string?> invariantModelOption = new("--invariant-model");
        Option<string> verbosityOption = Among(new Option<string>("--verbosity") { DefaultValueFactory = _ => "normal" }, "quiet", "normal", "debug");
        Option<string?> logOption = new("--log");
        Option<bool> ilFallbackOption = new("--il-fallback");
        Option<int?> resourceLimitOption = new("--resource-limit");
        Option<int?> jobsOption = new("--jobs");
        Option<string?> modeOption = Among(new Option<string?>("--mode"), Passes.ThoroughName, Passes.QuickName);

        Command command = new("compare")
        {
            legacyOption, modernOption, outOption, baselineOption, configOption, failOnOption, dryRunOption, lowerOnlyOption, executeOption, chcIntModeOption,
            testTargetOption, testBudgetOption, invariantModelOption, verbosityOption, logOption, ilFallbackOption, resourceLimitOption, jobsOption, modeOption,
        };

        command.SetAction(parseResult => RunLogged(
            new CompareOptions(
                parseResult.GetValue(legacyOption)!,
                parseResult.GetValue(modernOption)!,
                parseResult.GetValue(outOption)!,
                parseResult.GetValue(baselineOption),
                parseResult.GetValue(configOption),
                parseResult.GetValue(failOnOption),
                parseResult.GetValue(dryRunOption),
                parseResult.GetValue(lowerOnlyOption),
                parseResult.GetValue(executeOption),
                parseResult.GetValue(chcIntModeOption),
                parseResult.GetValue(invariantModelOption))
            {
                Testing = TestingOptions.TryParse(parseResult.GetValue(testTargetOption), parseResult.GetValue(testBudgetOption), out _)!,
                Verbosity = ToVerbosity(parseResult.GetValue(verbosityOption)),
                LogPath = parseResult.GetValue(logOption),
                IlFallback = parseResult.GetValue(ilFallbackOption),
                ResourceLimit = parseResult.GetValue(resourceLimitOption),
                Jobs = parseResult.GetValue(jobsOption),
                Mode = parseResult.GetValue(modeOption),
            },
            frontends,
            backend,
            execution));

        return command;
    }

    /// <summary><paramref name="option"/>, taking only <paramref name="values"/>; any other is the parser's usage error (exit 3).</summary>
    private static Option<T> Among<T>(Option<T> option, params string[] values)
    {
        option.AcceptOnlyFromAmong(values);
        return option;
    }

    /// <summary>
    /// <see cref="Run"/> with a <see cref="ChannelRunLog"/> built from <c>--verbosity</c> and <c>--log</c> (ticket M4-012).
    /// Progress goes to stderr and the <c>--log</c> file, never to stdout (ADR 0038, ADR 0033).
    /// </summary>
    private static int RunLogged(CompareOptions options, IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend, ExecutionEnvironment? execution)
    {
        using TextWriter logFile = OpenLog(options.LogPath);
        using ChannelRunLog runLog = new(options.Verbosity, Console.Error, logFile, TimeProvider.System);
        return Run(options, frontends, backend, new FileReportSink(options.OutPath), runLog, execution);
    }

    /// <summary>The <c>--log</c> file, flushed after every line so a run that is killed keeps what it wrote; <see cref="TextWriter.Null"/> without one.</summary>
    private static TextWriter OpenLog(string? path) => path is null ? TextWriter.Null : new StreamWriter(path, append: false) { AutoFlush = true };

    private static Verbosity ToVerbosity(string? verbosity) => verbosity switch
    {
        "quiet" => Verbosity.Quiet,
        "debug" => Verbosity.Debug,
        _ => Verbosity.Normal,
    };

    /// <summary><c>--test-target</c> and <c>--test-budget</c> are usage errors (exit 3) unless they parse (ticket P1-008).</summary>
    private static void Validate(System.CommandLine.Parsing.OptionResult result, TestingOptions? parsed, string error)
    {
        if (parsed is null)
        {
            result.AddError(error);
        }
    }

    /// <summary>
    /// The pipeline. <paramref name="execution"/> is where <c>--execute</c> runs, <see cref="ExecutionEnvironment.Current"/>
    /// when null; without <c>--execute</c> nothing reads it (ADR 0035; ticket M4-009). <paramref name="runLog"/> hears the
    /// <c>verify</c>, <c>contracts</c>, <c>execute</c> and <c>write</c> phases, and the backend hears it through
    /// <see cref="VerificationOptions.Log"/> (ADR 0038; ticket M4-012); tests pass <see cref="NullRunLog.Instance"/>.
    /// </summary>
    public static int Run(
        CompareOptions options,
        IReadOnlyList<ILanguageFrontend> frontends,
        IVerificationBackend backend,
        IReportSink sink,
        IRunLog runLog,
        ExecutionEnvironment? execution = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(frontends);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(runLog);

        Streams streams = options.Streams ?? Streams.Current;
        ExecutionEnvironment? executing = options.Execute ? execution ?? ExecutionEnvironment.Current : null;
        if (!File.Exists(options.LegacyPath) || !File.Exists(options.ModernPath))
        {
            streams.Error.WriteLine($"error: file not found (legacy={options.LegacyPath}, modern={options.ModernPath})");
            return ExitCodes.UsageError;
        }

        if (UsageError(options) is { } usageError)
        {
            streams.Error.WriteLine(usageError);
            return ExitCodes.UsageError;
        }

        ILanguageFrontend? frontend = FrontendRouter.Route(frontends, options.LegacyPath, options.ModernPath);
        if (frontend is null)
        {
            streams.Error.WriteLine($"error: no frontend supports both legacy={options.LegacyPath} and modern={options.ModernPath}");
            return ExitCodes.UsageError;
        }

        if (options.DryRun)
        {
            streams.Out.WriteLine($"route: {frontend.Language} legacy={options.LegacyPath} modern={options.ModernPath} out={options.OutPath}");
        }

        if (!TryLoadInputs(options.BaselinePath, options.ConfigPath, streams.Error, out EquivConfig loaded, out SarifLog? baseline, out int inputErrorExitCode))
        {
            return inputErrorExitCode;
        }

        EquivConfig config = Configured(loaded, options);
        FrontendAnalysis? analysis = Loaded(frontend, options, config, runLog, streams.Error);
        if (analysis is null)
        {
            return ExitCodes.LoadFailure;
        }

        if (!StartExecuting(executing, analysis, streams.Error))
        {
            return ExitCodes.UsageError;
        }

        // Two numbers, never a total: the licence measures each codebase on its own (ticket M3-014).
        streams.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"analysed lines of code: legacy={analysis.Lines.Legacy} modern={analysis.Lines.Modern}"));
        return options.DryRun ? ExitCodes.Success : Report(options, analysis, config, backend, baseline, new Output(sink, runLog, streams), executing);
    }

    /// <summary>
    /// The message of an option combination or value the run cannot use (exit 3), or null: <c>--lower-only</c> with
    /// <c>--baseline</c> or <c>--fail-on</c>, a budget or <c>jobs</c> that is not positive, or a mode that is neither
    /// <c>thorough</c> nor <c>quick</c> (ADR 0049 decision 1; ticket P1-032).
    /// </summary>
    private static string? UsageError(CompareOptions options)
    {
        if (options.LowerOnly && (options.BaselinePath is not null || options.FailOn is not null))
        {
            return "error: --lower-only cannot be combined with --baseline or --fail-on";
        }

        if (options.Bound is <= 0 || options.TimeoutMs is <= 0 || options.ResourceLimit is <= 0 || options.Jobs is <= 0)
        {
            return "error: bound, timeoutMs, resourceLimit and jobs must be positive integers";
        }

        return options.Mode is { } mode && EquivConfigLoader.ParseMode(mode) is null ? $"error: mode must be {Passes.ThoroughName} or {Passes.QuickName}, not '{mode}'" : null;
    }

    /// <summary>
    /// The run's config: the file's, with the command line's settings over it, each one named as explicit (ADR 0049
    /// decisions 1 and 4; ticket P1-032). <c>--lower-only</c> verifies nothing, so it takes no mode and the frontend keeps no
    /// second lowering for it.
    /// </summary>
    private static EquivConfig Configured(EquivConfig loaded, CompareOptions options) =>
        (loaded with
        {
            Bound = options.Bound ?? loaded.Bound,
            TimeoutMs = options.TimeoutMs ?? loaded.TimeoutMs,
            ResourceLimit = options.ResourceLimit ?? loaded.ResourceLimit,
            Jobs = options.Jobs ?? loaded.Jobs,
            IlFallback = options.IlFallback,
            Mode = options.LowerOnly ? CompareMode.Quick : EquivConfigLoader.ParseMode(options.Mode) ?? loaded.Mode,
        })
        .WithExplicit(EquivConfig.BoundSetting, options.Bound is not null)
        .WithExplicit(EquivConfig.ResourceLimitSetting, options.ResourceLimit is not null)
        .WithExplicit(EquivConfig.TimeoutSetting, options.TimeoutMs is not null);

    /// <summary>The frontend's analysis, or null, with the message on stderr, when it cannot load (the frontend reports its own phases; ADR 0038, ticket M4-013).</summary>
    private static FrontendAnalysis? Loaded(ILanguageFrontend frontend, CompareOptions options, EquivConfig config, IRunLog runLog, TextWriter error)
    {
        try
        {
            return frontend.Analyze(options.LegacyPath, options.ModernPath, config, runLog, CancellationToken.None);
        }
        catch (FrontendLoadException exception)
        {
            error.WriteLine(exception.Message);
            return null;
        }
    }

    /// <summary>
    /// The lowering census, the analysed line counts, the projects each solution does not build and each project's runtime go
    /// into the run's property bag on every run (ADR 0027; tickets M3-014, P2-013, P2-053), and every skipped project is a notification (ADR 0029). <c>--lower-only</c> stops there: the
    /// Added and Removed results, no backend call, exit 0 unless a C# project was skipped. Every other run also lists its
    /// review groups in the run and prints them after the line counts (ticket P2-064).
    /// </summary>
    private static int Report(
        CompareOptions options, FrontendAnalysis analysis, EquivConfig config, IVerificationBackend backend, SarifLog? baseline, Output output, ExecutionEnvironment? execution)
    {
        (IReportSink sink, IRunLog runLog, (TextWriter stdout, TextWriter error)) = output;
        MatchResult matchResult = analysis.Match;
        List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered = Lowered(matchResult);
        LoweringCensus census = LoweringCensus.Compute(
            [.. lowered.Select(static p => (p.Old, p.New, IsCongruent(p.Pair, p.Old, p.New), p.Pair.Runtimes))],
            removed: matchResult.Removed.Length,
            added: matchResult.Added.Length,
            projectsSkipped: new SideCounts(matchResult.LegacySkipped.Length, matchResult.ModernSkipped.Length),
            unlowered: matchResult.LoweringFailures.Length) with
        {
            IlFallback = options.IlFallback ? (matchResult.Pairs.Count(static p => p.IlFallbackTried), matchResult.Pairs.Count(static p => string.Equals(p.Lowering, "il", StringComparison.Ordinal))) : null,
        };

        // P2-011: a pair the frontend could not lower takes the same path as a pair whose verification throws (ADR 0023).
        List<Notification> pairFailures = [.. matchResult.LoweringFailures.Select(f => PairFailure("Lowering", f.Old, f.New, f.Exception, error))];
        List<ProcedureIdentity> unverifiedPairs = [.. matchResult.LoweringFailures.Select(static f => f.New)];
        Passes passes = Passes.Of(config, Verification(config, options, runLog));
        (List<VerificationResult> results, List<Notification> verifyFailures, List<ProcedureIdentity> unverifiedVerified) =
            options.LowerOnly ? ([], [], []) : Decided(lowered, analysis, passes, new Verifying(backend, config.Jobs, error), (execution, options.Testing));
        pairFailures.AddRange(verifyFailures);
        unverifiedPairs.AddRange(unverifiedVerified);
        QueryEndings? endings = options.LowerOnly ? null : QueryEndings.Of(results);
        if (endings is not null)
        {
            census = census with { UnknownByScope = ScopeCounts.Of(results), FailureRefinement = RefinementTime.Of(results), AgreesWhen = ConditionTime.Of(results) };
        }

        results.AddRange(matchResult.Added.Select(static identity => new VerificationResult(identity, new Added())));
        results.AddRange(matchResult.Removed.Select(static identity => new VerificationResult(identity, new Removed())));
        results.AddRange(matchResult.Ambiguous.Select(static identity => new VerificationResult(identity, new Unknown(UnknownReason.UnmatchedOverload, AmbiguousDetail(identity)))));

        (List<Notification> skippedProjectNotifications, List<ProcedureIdentity> skippedProjectProcedures) = SkippedProjects(matchResult, error);
        List<Notification> notifications = [.. pairFailures, .. skippedProjectNotifications, .. UncoveredRuntimes(matchResult, error), .. ContradictedConditions(results, error)];
        List<ProcedureIdentity> unverified = [.. unverifiedPairs, .. skippedProjectProcedures];
        SarifLog log = SarifReportWriter.Write(
            results,
            baseline,
            RunProperties(analysis, census, endings, passes.ToProperty(config.Explicit)),
            notifications,
            unverified,
            reviewList: !options.LowerOnly);
        Written(sink, log, options.OutPath, runLog);
        WarnOfAnotherMode(baseline, passes, error);
        foreach (string line in ReviewList.Lines(log.Runs[0]))
        {
            stdout.WriteLine(line);
        }

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
    /// The run's property bag (ADR 0027; tickets M3-014, P2-013, P2-053), with <c>mode</c> (ADR 0049 decision 6; ticket
    /// P1-032) and then <c>queryEndings</c> last on a run that verifies (ticket P2-077).
    /// </summary>
    private static Dictionary<string, object> RunProperties(FrontendAnalysis analysis, LoweringCensus census, QueryEndings? endings, Dictionary<string, object> mode)
    {
        Dictionary<string, object> properties = RunProperties(analysis, census);
        if (endings is not null)
        {
            properties[Passes.Property] = mode;
            properties["queryEndings"] = endings.ToProperty();
        }

        return properties;
    }

    /// <summary>
    /// ADR 0049 decision 6: one warning on stderr when <c>--baseline</c> was written in the other mode, since what the
    /// modes decide differently then shows as <c>new</c> results. A baseline that names no mode, one from before the modes,
    /// gets none.
    /// </summary>
    private static void WarnOfAnotherMode(SarifLog? baseline, Passes passes, TextWriter error)
    {
        if (baseline?.Runs is [{ } run, ..] && run.TryGetProperty(Passes.Property, out Dictionary<string, object> written) && written.GetValueOrDefault("name") is string name && !string.Equals(name, passes.Name, StringComparison.Ordinal))
        {
            error.WriteLine($"warning: the baseline was written in {name} mode and this run is in {passes.Name} mode; results the modes decide differently are reported as new");
        }
    }

    /// <summary>
    /// Every result of the matched pairs, in the pairs' order (ADR 0049 decision 2; ticket P1-032): the first pass
    /// (<see cref="Verified"/>), thorough mode's later passes over what is still Unknown (<see cref="LaterPasses"/>), each
    /// result's assumptions, thorough mode's contracts pass, and <c>--execute</c>. The failures and the unverified
    /// identities are the first pass's: a later pass that fails on a pair leaves the pair its earlier result. Thorough
    /// without <c>--execute</c> says once on stderr that its Unknowns were not tested (decision 3).
    /// </summary>
    private static (List<VerificationResult> Results, List<Notification> Failures, List<ProcedureIdentity> Unverified) Decided(
        List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered, FrontendAnalysis analysis, Passes passes, Verifying verifying, (ExecutionEnvironment? Environment, TestingOptions Testing) executing)
    {
        (IVerificationBackend backend, int jobs, TextWriter error) = verifying;
        (List<VerificationResult> verified, List<Notification> failures, List<ProcedureIdentity> unverified) = Verified(lowered, backend, passes.First, jobs, error);
        (List<VerificationResult> passed, List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> decidedFrom) = LaterPasses(verified, lowered, passes, verifying);
        List<VerificationResult> assumed = WithAssumptions(passed, decidedFrom, analysis.Match);
        List<VerificationResult> contracted = passes.Thorough ? WithContracts(assumed, decidedFrom, backend, passes.Whole, jobs, error) : assumed;
        List<VerificationResult> results = Executed(contracted, decidedFrom, analysis.Replay, executing.Environment, executing.Testing, passes);
        if (passes.Thorough && executing.Environment is null && results.Exists(static r => r.Verdict is Unknown))
        {
            error.WriteLine("note: the Unknown results were not tested on generated inputs; --execute tests them, and runs the solutions' code to do it");
        }

        return (results, failures, unverified);
    }

    /// <summary>
    /// Thorough mode's later passes, each over the results that are still Unknown and that the solver, not
    /// <see cref="Decide"/>, gave (ADR 0049 decision 2; ticket P1-032). The budget pass, when the escalation asks for more
    /// than the first pass had, verifies again each one whose ladder holds a step that hit a budget or whose pair has a loop
    /// or a self-call. The IL pass then verifies again, from its IL bodies, each one whose pair the frontend lowered from IL
    /// as well (ADR 0039's condition). A pair the IL pass decided is returned with its IL bodies, so its assumptions, its
    /// contracts and its replay read the bodies its verdict is about. Quick mode has no later pass.
    /// </summary>
    private static (List<VerificationResult> Results, List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> Lowered) LaterPasses(
        List<VerificationResult> verified, List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered, Passes passes, Verifying verifying)
    {
        if (!passes.Thorough)
        {
            return (verified, lowered);
        }

        Dictionary<string, (ProcedurePair Pair, IrProcedure Old, IrProcedure New)> pairs = lowered.ToDictionary(static p => p.Pair.New.Value, StringComparer.Ordinal);
        (Unknown Earlier, ProcedurePair Pair, IrProcedure Old, IrProcedure New)? Undecided(VerificationResult result) =>
            result.Verdict is Unknown unknown && pairs[result.Identity.Value] is var (pair, old, @new) && Decide(pair, old, @new) is null ? (unknown, pair, old, @new) : null;

        List<VerificationResult> results = verified;
        if (passes.Budget is { } budget)
        {
            results = LaterPass(
                Passes.BudgetPhase,
                results,
                result => Undecided(result) is var (unknown, pair, old, @new) && (HitABudget(unknown) || LoopsOrCallsItself(old, @new)) ? new PassCandidate(unknown, pair, old, @new, FromIl: false) : null,
                budget,
                verifying);
        }

        results = LaterPass(
            Passes.IlPhase,
            results,
            result => Undecided(result) is (var unknown, { Il: { } il } pair, _, _) ? new PassCandidate(unknown, pair, il.Old, il.New, FromIl: true) : null,
            passes.Budget ?? passes.Whole,
            verifying);
        HashSet<string> fromIl = [.. results.Where(static r => string.Equals(r.DecidedBy, VerificationResult.IlPass, StringComparison.Ordinal)).Select(static r => r.Identity.Value)];
        return (results, [.. lowered.Select(p => fromIl.Contains(p.Pair.New.Value) ? (p.Pair with { OldBody = p.Pair.Il!.Old, NewBody = p.Pair.Il.New }, p.Pair.Il.Old, p.Pair.Il.New) : p)]);
    }

    /// <summary>Whether a rung of <paramref name="verdict"/>'s ladder ended because the solver gave up, which a larger budget may change.</summary>
    private static bool HitABudget(Verdict verdict) => verdict.Ladder.Any(static step => step.Outcome == RungOutcome.Timeout);

    /// <summary>Whether either body has a loop or calls itself, which a larger bound unrolls further.</summary>
    private static bool LoopsOrCallsItself(IrProcedure old, IrProcedure @new) =>
        new[] { IrLoopAnalysis.Of(old), IrLoopAnalysis.Of(@new) }.Any(static shape => shape.IsSelfRecursive || !shape.Loops.IsEmpty);

    /// <summary>
    /// One later pass, a phase of the run log named <paramref name="phase"/> and weighed as <c>verify</c> is (ADR 0038):
    /// each result <paramref name="candidate"/> gives a pair for is verified again with <paramref name="options"/>, and
    /// <see cref="Standing"/> says whether the new result replaces it. A pass with no candidate has no phase. A crash on
    /// one pair leaves its earlier result and is a warning on stderr, never ADR 0023's exit 5 (ADR 0049 decision 5). Up to
    /// the run's jobs are verified at once, and the results and the warnings keep the order they came in.
    /// </summary>
    private static List<VerificationResult> LaterPass(
        string phase, List<VerificationResult> results, Func<VerificationResult, PassCandidate?> candidate, VerificationOptions options, Verifying verifying)
    {
        PassCandidate?[] candidates = [.. results.Select(candidate)];
        List<PassCandidate> chosen = [.. candidates.OfType<PassCandidate>()];
        if (chosen.Count == 0)
        {
            return results;
        }

        options.Log.Phase(phase, chosen.Count, chosen.Sum(static c => c.Weight), new PhaseBound(chosen.Count, options.TimeoutMs, chosen.Max(static c => c.Rungs)));
        int workers = PairWorkers.Count(verifying.Jobs, chosen.Count);
        VerificationOptions shared = PairWorkers.Sharing(options, workers);
        (VerificationResult Result, string? Warning)[] passed = PairWorkers.Run(
            results.Count,
            workers,
            i => candidates[i] is { } again ? VerifiedAgain(phase, results[i], again, verifying.Backend, shared) : (results[i], null));
        options.Log.PhaseDone();
        foreach (string warning in passed.Select(static p => p.Warning).OfType<string>())
        {
            verifying.Error.WriteLine(warning);
        }

        return [.. passed.Select(static p => p.Result)];
    }

    /// <summary>
    /// One item of a later pass, on the thread that calls it: the result that stands, and the warning to write when the
    /// backend threw. The later result's ladder holds both passes and it names the pass (ADR 0049 decision 6). When the
    /// earlier result stands and has no failure refinement, it takes the later one's, which is how a <c>timeout</c> Unknown
    /// carries the answers asked at the budget pass's budgets (ADR 0049's table).
    /// </summary>
    private static (VerificationResult Result, string? Warning) VerifiedAgain(
        string phase, VerificationResult earlier, PassCandidate candidate, IVerificationBackend backend, VerificationOptions options)
    {
        options.Log.Item(earlier.Identity.Value, candidate.Weight);
        Verdict verdict;
        try
        {
            verdict = backend.Verify(candidate.Old, candidate.New, options);
        }
        catch (Exception exception) when (IsPairFailure(exception))
        {
            options.Log.ItemDone("failed");
            return (earlier, $"warning: Verifying {earlier.Identity.Value} again in the {phase} pass failed, so it keeps its result: {exception.Message}");
        }

        options.Log.ItemDone(Outcome(verdict));
        VerificationResult later = candidate.Over(earlier) with { Verdict = verdict with { Ladder = [.. earlier.Verdict.Ladder, .. verdict.Ladder] }, DecidedBy = $"{phase}-pass" };
        VerificationResult standing = Standing(earlier, later);
        return candidate.Earlier.FailureRefinement is null && verdict is Unknown { FailureRefinement: { } refinement } && ReferenceEquals(standing, earlier)
            ? (earlier with { Verdict = candidate.Earlier with { FailureRefinement = refinement } }, null)
            : (standing, null);
    }

    /// <summary>
    /// ADR 0049 decision 2, and the only place the rule lives: which of a pair's earlier result and a later pass's stands.
    /// The later one replaces the earlier when it is Equivalent or Divergent, or when it is an Unknown that is neither
    /// <c>timeout</c> nor <c>chc-timeout</c> and the earlier one was. Otherwise the earlier result stands, so a result that
    /// is decided is never replaced.
    /// </summary>
    internal static VerificationResult Standing(VerificationResult earlier, VerificationResult later) => (earlier.Verdict, later.Verdict) switch
    {
        (not Unknown, _) => earlier,
        (_, not Unknown) => later,
        (Unknown { Reason: UnknownReason.Timeout or UnknownReason.ChcTimeout }, Unknown { Reason: not (UnknownReason.Timeout or UnknownReason.ChcTimeout) }) => later,
        _ => earlier,
    };

    /// <summary>The one word the run log ends a verified pair's item with.</summary>
    private static string Outcome(Verdict verdict) => verdict switch
    {
        Equivalent => "equivalent",
        Divergent => "divergent",
        _ => "unknown",
    };

    private static Dictionary<string, object> RunProperties(FrontendAnalysis analysis, LoweringCensus census) =>
        new(StringComparer.Ordinal)
        {
            ["loweringCensus"] = census.ToProperty(),
            ["analysedLinesOfCode"] = LoweringCensus.Property(new SideCounts(analysis.Lines.Legacy, analysis.Lines.Modern)),
            ["projectsNotBuilt"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [LegacySide] = (List<string>)[.. analysis.LegacyNotBuilt],
                [ModernSide] = (List<string>)[.. analysis.ModernNotBuilt],
            },
            ["runtimes"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [LegacySide] = SarifReportWriter.RuntimesProperty(analysis.LegacyRuntimes),
                [ModernSide] = SarifReportWriter.RuntimesProperty(analysis.ModernRuntimes),
            },
        };

    /// <summary>
    /// ADR 0040 decision 2: when any matched pair's runtimes reach below the oldest .NET version <c>runtime-changes.json</c>
    /// covers, one <c>warning</c> notification, on stderr as well as in the SARIF, naming the widest range no row covers
    /// (ticket P2-055). A behaviour that changed inside it is not flagged, so a verdict there rests on the two runtimes
    /// agreeing. It carries the descriptor <see cref="UncoveredRuntimeRange"/>, which tells it from a skipped project's.
    /// </summary>
    private static List<Notification> UncoveredRuntimes(MatchResult matchResult, TextWriter error)
    {
        TargetRuntime coveredFrom = RuntimeChangeTable.Load().CoveredFrom;
        List<RuntimeInterval> gaps = [.. matchResult.Pairs.Select(pair => pair.Runtimes?.UncoveredRange(coveredFrom)).OfType<RuntimeInterval>()];
        if (gaps.Count == 0)
        {
            return [];
        }

        string text = string.Create(
            CultureInfo.InvariantCulture,
            $"runtime-changes.json lists no runtime changes between {gaps.Min(static g => g.Older)} and {gaps.Max(static g => g.Newer)}, which {gaps.Count} matched pair(s) cross; a behaviour that changed in that range is not flagged");
        error.WriteLine($"warning: {text}");
        return [new Notification { Level = FailureLevel.Warning, Message = new Message { Text = text }, Descriptor = new ReportingDescriptorReference { Id = UncoveredRuntimeRange } }];
    }

    /// <summary>
    /// ADR 0048 decision 7: one <c>warning</c> notification, on stderr as well as in the SARIF, per result whose own
    /// counterexample satisfied the input condition the solver had proved for its pair (ticket P1-022). That is a bug in
    /// the tool, so the result carries no <c>agreesWhen</c>; its verdict and the exit code are what they were. It carries
    /// the descriptor <see cref="ContradictedCondition"/>.
    /// </summary>
    private static List<Notification> ContradictedConditions(List<VerificationResult> results, TextWriter error)
    {
        List<Notification> notifications = [];
        foreach (VerificationResult result in results.Where(static r => ConditionSearch.Of(r.Verdict) is { Contradicted: true }))
        {
            string text = $"{result.Identity.Value}: the counterexample satisfies the input condition proved for the pair; agreesWhen is left out (a bug in equiv, please report it)";
            error.WriteLine($"warning: {text}");
            notifications.Add(new Notification { Level = FailureLevel.Warning, Message = new Message { Text = text }, Descriptor = new ReportingDescriptorReference { Id = ContradictedCondition } });
        }

        return notifications;
    }

    /// <summary>The <c>write</c> phase (ADR 0038): one item, the SARIF log.</summary>
    private static void Written(IReportSink sink, SarifLog log, string outPath, IRunLog runLog)
    {
        runLog.Phase("write", 1, 1);
        runLog.Item(outPath, 1);
        sink.Write(log);
        runLog.ItemDone("written");
        runLog.PhaseDone();
    }

    /// <summary>
    /// ADR 0035's consequences for <c>--execute</c>, once the solutions are loaded: a side on .NET Framework needs Windows,
    /// else the run stops with exit 3 naming the project and its runtime (ADR 0040 decision 3; ticket P2-056), and it says
    /// on stderr that it runs the solutions' code. Without <c>--execute</c> (<paramref name="executing"/> null) it does nothing.
    /// </summary>
    private static bool StartExecuting(ExecutionEnvironment? executing, FrontendAnalysis analysis, TextWriter error)
    {
        if (executing is null)
        {
            return true;
        }

        string? refusal = executing.Refusal(analysis);
        error.WriteLine(refusal ?? ExecutionEnvironment.Note);
        return refusal is null;
    }

    /// <summary>
    /// ADR 0035 decisions 2 and 3, under <c>--execute</c>. Every Divergent's model is replayed on both real runtimes and
    /// recorded as the result's <see cref="VerificationResult.Replay"/>, which never changes the verdict, the rule id, the
    /// fingerprint or the exit code (ticket M4-009). Every Unknown pair is tested on generated inputs within
    /// <paramref name="testing"/>: it stays Unknown with <see cref="VerificationResult.Testing"/>, or becomes Divergent with
    /// <c>proofMethod: observed</c> when the runtimes are seen to differ (ticket P1-008). Quick mode tests no Unknown (ADR
    /// 0049's table; ticket P1-032). Execution never makes a result
    /// Equivalent. Projects and drivers are emitted into a temporary folder that is deleted afterwards. Without
    /// <c>--execute</c> (<paramref name="execution"/> null), or from a frontend that cannot build drivers, nothing runs.
    /// Otherwise each result is one item of the <c>execute</c> phase (ADR 0038).
    /// </summary>
    private static List<VerificationResult> Executed(
        List<VerificationResult> results,
        List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered,
        IReplayDriverFactory? factory,
        ExecutionEnvironment? execution,
        TestingOptions testing,
        Passes passes)
    {
        IRunLog runLog = passes.First.Log;
        if (execution is null || factory is null)
        {
            return results;
        }

        Dictionary<string, (ProcedurePair Pair, IrProcedure Old, IrProcedure New)> pairs = lowered.ToDictionary(static p => p.Pair.New.Value, StringComparer.Ordinal);
        string directory = Directory.CreateTempSubdirectory("equiv-execute-").FullName;

        // Every driver runs in a fresh folder under the temporary one, so a relative write by the solution's code is
        // deleted with it instead of landing next to the caller (ticket P2-040).
        IDriverHost host = execution.Host.Within(directory);
        Replayer replayer = new(host);
        DifferentialTester tester = new(host, testing, execution.Time);
        runLog.Phase("execute", results.Count, results.Count);
        try
        {
            List<VerificationResult> executed = [];
            foreach (VerificationResult result in results)
            {
                runLog.Item(result.Identity.Value, 1);
                (VerificationResult next, string outcome) = (result.Verdict, pairs[result.Identity.Value]) switch
                {
                    (Divergent divergent, var (pair, old, @new)) => (result with { Replay = replayer.Replay(factory.Create(pair, divergent.Counterexample, directory), divergent.Counterexample, old, @new) }, "replayed"),
                    (Unknown unknown, var (pair, old, @new)) when passes.Thorough => (tester.Test(factory.Plan(pair, unknown.Candidate, directory), old, @new).Apply(result), "tested"),
                    _ => (result, "skipped"),
                };
                executed.Add(next);
                runLog.ItemDone(outcome);
            }

            return executed;
        }
        finally
        {
            runLog.PhaseDone();
            DeleteTemporary(directory, static d => Directory.Delete(d, recursive: true), TimeSpan.FromMilliseconds(500));
        }
    }

    /// <summary>
    /// Deletes <c>--execute</c>'s temporary folder, trying <see cref="DeleteAttempts"/> times <paramref name="pause"/> apart
    /// and then leaving it to the OS's temp cleanup. A driver process that has exited can still hold its <c>.exe</c> open for
    /// a moment on Windows ("Access to the path is denied"), and a folder left behind must not turn a finished run into
    /// exit 5 with no SARIF.
    /// </summary>
    internal static void DeleteTemporary(string directory, Action<string> delete, TimeSpan pause)
    {
        for (int attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            try
            {
                delete(directory);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt < DeleteAttempts)
                {
                    Thread.Sleep(pause);
                }
            }
        }
    }

    /// <summary>
    /// ADR 0029 decision 1: one tool-execution notification per project the frontend skipped, on stderr as well as in
    /// the SARIF, and every procedure that is unverified because of it. A skipped C# project is an <c>error</c>, which
    /// makes the run incomplete and outranks any verdict (<see cref="ExitCodes"/>); a project in another language is a
    /// <c>warning</c>.
    /// </summary>
    private static (List<Notification> Notifications, List<ProcedureIdentity> Unverified) SkippedProjects(MatchResult matchResult, TextWriter error)
    {
        List<Notification> notifications = [];
        List<ProcedureIdentity> unverified = [];
        foreach ((string side, UnverifiedProject project) in matchResult.LegacySkipped.Select(static p => (LegacySide, p))
            .Concat(matchResult.ModernSkipped.Select(static p => (ModernSide, p))))
        {
            FailureLevel level = project.IsCSharp ? FailureLevel.Error : FailureLevel.Warning;
            string subject = project.Name.Length > 0
                ? $"{side} project '{project.Name}' (assembly '{project.AssemblyName}')"
                : $"a {side} project the workspace did not name";
            string text = $"{subject} was skipped: {string.Join("; ", project.Diagnostics)}";
            error.WriteLine($"{(project.IsCSharp ? "error" : "warning")}: {text}");
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
    private static bool TryLoadInputs(string? baselinePath, string? configPath, TextWriter error, out EquivConfig config, out SarifLog? baseline, out int exitCode)
    {
        config = EquivConfig.Default;
        baseline = null;
        exitCode = ExitCodes.Success;

        if (configPath is not null && !File.Exists(configPath))
        {
            error.WriteLine($"error: file not found (config={configPath})");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        if (baselinePath is not null && !File.Exists(baselinePath))
        {
            error.WriteLine($"error: file not found (baseline={baselinePath})");
            exitCode = ExitCodes.UsageError;
            return false;
        }

        try
        {
            config = LoadConfig(configPath, error);
        }
        catch (EquivConfigParseException exception)
        {
            error.WriteLine(exception.Message);
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
            error.WriteLine($"error: '{baselinePath}' is not a valid SARIF log: {exception.Message}");
            exitCode = ExitCodes.UsageError;
            return false;
        }
    }

    private static EquivConfig LoadConfig(string? configPath, TextWriter error)
    {
        if (configPath is null)
        {
            return EquivConfig.Default;
        }

        EquivConfigResult result = EquivConfigLoader.Load(File.ReadAllText(configPath));
        foreach (EquivConfigDiagnostic diagnostic in result.Diagnostics)
        {
            error.WriteLine($"warning: {diagnostic.Id} {diagnostic.Path}: {diagnostic.Message}");
        }

        // ADR 0049 decision 1: a mode that is neither of the two is a usage error, wherever it was given.
        return result.Diagnostics.FirstOrDefault(static d => string.Equals(d.Id, EquivConfigDiagnosticIds.InvalidMode, StringComparison.Ordinal)) is { } invalid
            ? throw new EquivConfigParseException($"error: equiv.config.json: {invalid.Message}")
            : result.Config;
    }

    /// <summary>
    /// The backend's knobs for this run: the config's bound, timeout, resource limit and renames, the command line's rung
    /// options, and cvc5 as the second solver when the config names its executable (ADR 0050; ticket P1-033).
    /// </summary>
    private static VerificationOptions Verification(EquivConfig config, CompareOptions options, IRunLog runLog) =>
        new(config.Bound, config.TimeoutMs, config.CallIdentityRenames)
        {
            ResourceLimit = config.ResourceLimit,
            ChcIntMode = options.ChcIntMode,
            InvariantModel = options.InvariantModel,
            Solver = config.Cvc5Path is { } cvc5 ? new Cvc5Solver(cvc5) : null,
            Log = runLog,
        };

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
    /// Each pair is one item of the <c>verify</c> phase, weighed by <see cref="PairWeight"/>, and the phase is bounded by
    /// the pairs the solver decides (ADR 0038). A pair whose deciding or weighing throws fails the same way, before the
    /// backend sees it, and weighs 1 (ticket P2-082). Up to <paramref name="jobs"/> pairs are verified at once
    /// (<see cref="PairWorkers"/>; ticket P2-077), never more than the solver decides, and the results, the notifications,
    /// the unverified identities and the lines on stderr are in the pairs' order whatever order they finished in.
    /// </summary>
    private static (List<VerificationResult> Results, List<Notification> Failures, List<ProcedureIdentity> Unverified) Verified(
        List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered, IVerificationBackend backend, VerificationOptions options, int jobs, TextWriter error)
    {
        IRunLog runLog = options.Log;
        if (options.InvariantModel is { } model)
        {
            // Ticket P1-002 criterion 4: rung 5 sends loop IR text to the model, so say so before any pair is verified.
            error.WriteLine($"note: sending loop IR text to {model}");
        }

        List<VerificationResult> results = [];
        List<Notification> failures = [];
        List<ProcedureIdentity> unverified = [];

        List<Weighing> pairs = [.. lowered.Select(static p => Weighed(p.Pair, p.Old, p.New))];
        List<int> solverRungs = [.. pairs.Where(static p => p.Rungs > 0).Select(static p => p.Rungs)];
        runLog.Phase("verify", pairs.Count, pairs.Sum(static p => p.Weight), new PhaseBound(solverRungs.Count, options.TimeoutMs, solverRungs.DefaultIfEmpty(1).Max()));
        int workers = PairWorkers.Count(jobs, solverRungs.Count);
        VerificationOptions shared = PairWorkers.Sharing(options, workers);
        PairOutcome[] outcomes = PairWorkers.Run(pairs.Count, workers, i => VerifiedPair(pairs[i], backend, shared));
        runLog.PhaseDone();
        foreach ((PairOutcome outcome, ProcedurePair pair) in outcomes.Zip(pairs.Select(static p => p.Pair)))
        {
            if (outcome.Result is { } result)
            {
                results.Add(result);
            }
            else
            {
                failures.Add(PairFailure(outcome.Stage, pair.Old, pair.New, outcome.Crash!, error));
                unverified.Add(pair.New);
            }
        }

        return (results, failures, unverified);
    }

    /// <summary>
    /// One item of the <c>verify</c> phase, on the thread that calls it: the pair's result, or the exception that failed
    /// it and the stage it failed in. It writes nothing but the run log, so pairs verified at once cannot interleave on stderr.
    /// </summary>
    private static PairOutcome VerifiedPair(Weighing weighing, IVerificationBackend backend, VerificationOptions options)
    {
        (ProcedurePair pair, IrProcedure old, IrProcedure @new, Decision? decided, long weight, _, Exception? crash) = weighing;
        options.Log.Item(pair.New.Value, weight);
        if (crash is not null)
        {
            options.Log.ItemDone("failed");
            return new PairOutcome(Result: null, "Weighing", crash);
        }

        if (decided is not null)
        {
            options.Log.ItemDone(decided.Outcome);
            return new PairOutcome(decided.Result with { Lowering = pair.Lowering, Runtimes = pair.Runtimes, ReboundCalls = pair.ReboundCalls, ForwardersResolved = pair.ForwardersResolved });
        }

        try
        {
            Verdict verdict = backend.Verify(old, @new, options);
            options.Log.ItemDone(Outcome(verdict));
            return new PairOutcome(new VerificationResult(pair.New, verdict)
            {
                EquivalencesApplied = pair.EquivalencesApplied,
                Lowering = pair.Lowering,
                Runtimes = pair.Runtimes,
                ReboundCalls = pair.ReboundCalls,
                ForwardersResolved = pair.ForwardersResolved,
            });
        }
        catch (Exception exception) when (IsPairFailure(exception))
        {
            options.Log.ItemDone("failed");
            return new PairOutcome(Result: null, "Verifying", exception);
        }
    }

    /// <summary>
    /// The pair decided without the solver if it can be, with its weight and, when the solver decides it, its rungs (0
    /// otherwise). A throw is the pair's <see cref="Weighing.Crash"/>, not the run's end (ticket P2-082, ADR 0023).
    /// </summary>
    private static Weighing Weighed(ProcedurePair pair, IrProcedure old, IrProcedure @new)
    {
        try
        {
            Decision? decided = Decide(pair, old, @new);
            return decided is null
                ? new Weighing(pair, old, @new, decided, PairWeight.Of(old, @new, solver: true), PairWeight.Rungs(old, @new), Crash: null)
                : new Weighing(pair, old, @new, decided, PairWeight.Of(old, @new, solver: false), Rungs: 0, Crash: null);
        }
        catch (Exception exception) when (IsPairFailure(exception))
        {
            return new Weighing(pair, old, @new, Decided: null, Weight: 1, Rungs: 0, exception);
        }
    }

    /// <summary>The result of a pair decided without the solver, with its one-word outcome; null when the solver decides it.</summary>
    private static Decision? Decide(ProcedurePair pair, IrProcedure old, IrProcedure @new)
    {
        // ADR 0029 decision 2: erroneous code is Unknown(Unbound) without asking the solver; it is never evidence of equivalence.
        // Its causes are the errors, so the result points at the modern side's first one. The detail only says which side
        // does not bind: it is part of the fingerprint, which must not change with where the code is checked out (ticket P2-085).
        ImmutableArray<UnknownCause> unbound = [.. Causes(Codebase.Legacy, old, Unknown.UnboundOpaqueReason), .. Causes(Codebase.Modern, @new, Unknown.UnboundOpaqueReason)];
        if (!unbound.IsEmpty)
        {
            string detail = string.Join("; ", unbound.Select(static c => c.Side).Distinct().Select(static side => side == Codebase.Legacy ? "the legacy body does not bind" : "the modern body does not bind"));
            return new Decision(
                new VerificationResult(pair.New, new Unknown(UnknownReason.Unbound, detail) { Causes = unbound }) { EquivalencesApplied = pair.EquivalencesApplied },
                "unbound");
        }

        // Ticket M4-006: exactly one side is async, so exception timing differs; the frontend made both bodies one opaque.
        ImmutableArray<UnknownCause> mismatch = [.. Causes(Codebase.Legacy, old, Unknown.AsyncMismatchReason), .. Causes(Codebase.Modern, @new, Unknown.AsyncMismatchReason)];
        if (!mismatch.IsEmpty)
        {
            return new Decision(
                new VerificationResult(pair.New, new Unknown(UnknownReason.Opaque, Unknown.AsyncMismatchReason) { Causes = mismatch }) { EquivalencesApplied = pair.EquivalencesApplied },
                "async-mismatch");
        }

        // ADR 0024: identical bound code is Equivalent without the solver.
        return IsCongruent(pair, old, @new)
            ? new Decision(new VerificationResult(pair.New, new Equivalent(ProofMethod.Congruence)) { EquivalencesApplied = pair.EquivalencesApplied }, "congruent")
            : null;
    }

    /// <summary>
    /// ADR 0024 decision 1: the two bound fingerprints are equal and not runtime-sensitive. A body with erroneous code is
    /// never congruent, because two error symbols with the same name are no evidence of the same behaviour (ADR 0029 decision 2).
    /// </summary>
    internal static bool IsCongruent(ProcedurePair pair, IrProcedure old, IrProcedure @new) =>
        pair.OldFingerprint is { RuntimeSensitive: false } fingerprint
        && fingerprint == pair.NewFingerprint
        && !Causes(Codebase.Legacy, old, Unknown.UnboundOpaqueReason).Any()
        && !Causes(Codebase.Modern, @new, Unknown.UnboundOpaqueReason).Any();

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

    /// <summary>
    /// ADR 0036 decision 2 (ticket P1-010), once every result carries its assumptions: an Equivalent result whose unproven
    /// assumptions include lowered callee pairs goes back to <paramref name="backend"/> with those pairs, to be proved again
    /// with a caller-sufficient contract for each. When it is, the result takes that verdict, each callee it has a contract
    /// for leaves <see cref="VerificationResult.UnprovenAssumptions"/>, and that callee's own unproven assumptions join the
    /// caller's assumed and unproven ones, since the contract's proof assumed them. Otherwise, and when the backend throws,
    /// the result stays as it was; a throw is written to stderr as a warning, because the verdict it leaves is still sound.
    /// Each result that goes back is one item of the <c>contracts</c> phase, weighed by <see cref="PairWeight"/> (ADR 0038;
    /// ticket P2-076); a run in which none does has no such phase. Up to <paramref name="jobs"/> results go back at once
    /// (<see cref="PairWorkers"/>; ticket P2-077), and the results and the warnings keep the order they came in.
    /// </summary>
    private static List<VerificationResult> WithContracts(
        List<VerificationResult> results, List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered, IVerificationBackend backend, VerificationOptions options, int jobs, TextWriter error)
    {
        Dictionary<string, (IrProcedure Old, IrProcedure New)> bodies = lowered.ToDictionary(static p => p.Pair.New.Value, static p => (p.Old, p.New), StringComparer.Ordinal);
        Dictionary<string, VerificationResult> byIdentity = results.ToDictionary(static r => r.Identity.Value, StringComparer.Ordinal);
        Dictionary<string, ContractCandidate> candidates = results
            .Where(static result => result.Verdict is Equivalent)
            .Select(result => (Result: result, Callees: result.UnprovenAssumptions.Where(bodies.ContainsKey).Select(c => new CalleePair(c, bodies[c].Old, bodies[c].New)).ToImmutableArray()))
            .Where(static c => !c.Callees.IsEmpty)
            .ToDictionary(static c => c.Result.Identity.Value, c => new ContractCandidate(bodies[c.Result.Identity.Value].Old, bodies[c.Result.Identity.Value].New, c.Callees), StringComparer.Ordinal);
        if (candidates.Count == 0)
        {
            return results;
        }

        options.Log.Phase("contracts", candidates.Count, candidates.Values.Sum(static c => c.Weight));
        int workers = PairWorkers.Count(jobs, candidates.Count);
        VerificationOptions shared = PairWorkers.Sharing(options, workers);
        (VerificationResult Result, string? Warning)[] contracted = PairWorkers.Run(
            results.Count,
            workers,
            i => candidates.TryGetValue(results[i].Identity.Value, out ContractCandidate? candidate) ? UnderContracts(results[i], candidate, byIdentity, backend, shared) : (results[i], null));
        options.Log.PhaseDone();
        foreach (string warning in contracted.Select(static c => c.Warning).OfType<string>())
        {
            error.WriteLine(warning);
        }

        return [.. contracted.Select(static c => c.Result)];
    }

    /// <summary>One item of the <c>contracts</c> phase, on the thread that calls it: the result, and the warning to write when the backend threw.</summary>
    private static (VerificationResult Result, string? Warning) UnderContracts(
        VerificationResult result,
        ContractCandidate candidate,
        Dictionary<string, VerificationResult> byIdentity,
        IVerificationBackend backend,
        VerificationOptions options)
    {
        (IrProcedure old, IrProcedure @new, ImmutableArray<CalleePair> callees) = candidate;
        options.Log.Item(result.Identity.Value, candidate.Weight);
        Equivalent? proved;
        try
        {
            proved = backend.VerifyUnderContracts(old, @new, callees, options);
        }
        catch (Exception exception) when (IsPairFailure(exception))
        {
            options.Log.ItemDone("failed");
            return (result, $"warning: Verifying {result.Identity.Value} under callee contracts failed, so it keeps its verdict: {exception.Message}");
        }

        if (proved is null)
        {
            options.Log.ItemDone("unchanged");
            return (result, null);
        }

        options.Log.ItemDone("proved");
        HashSet<string> contracted = new(proved.ContractsUsed.Select(static c => c.Callee), StringComparer.Ordinal);
        string[] inherited = [.. contracted.SelectMany(c => byIdentity.TryGetValue(c, out VerificationResult? callee) ? callee.UnprovenAssumptions : [])];
        return (result with
        {
            Verdict = proved,
            AssumedCallees = [.. result.AssumedCallees.Concat(inherited).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            UnprovenAssumptions = [.. result.UnprovenAssumptions.Where(c => !contracted.Contains(c)).Concat(inherited).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        }, null);
    }

    private static IEnumerable<string> Callees(IrProcedure body) =>
        body.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>().Select(static call => call.Callee.Value);

    /// <summary>Whether <paramref name="exception"/> fails one pair (ADR 0023) rather than the run: cancellation and running out of memory end the run.</summary>
    private static bool IsPairFailure(Exception exception) => exception is not OperationCanceledException and not OutOfMemoryException;

    /// <summary>
    /// ADR 0023's record of a pair the tool failed on: an <c>error</c> notification naming both identities and carrying
    /// the exception, also written to stderr. <paramref name="stage"/> says what failed (<c>Lowering</c>, <c>Verifying</c>).
    /// </summary>
    private static Notification PairFailure(string stage, ProcedureIdentity old, ProcedureIdentity @new, Exception exception, TextWriter error)
    {
        string text = $"{stage} {old.Value} against {@new.Value} failed: {exception.Message}";
        error.WriteLine($"error: {text}");
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

    /// <summary>
    /// M1-003's deferred call: an identity present more than once on a side that also has it (<see cref="StableIdentityMatcher"/>)
    /// is a group of overloads the matcher cannot choose among, so it is <see cref="UnknownReason.UnmatchedOverload"/> rather
    /// than a pair.
    /// </summary>
    private static string AmbiguousDetail(ProcedureIdentity identity) => $"{identity.Value} matches more than one overload with this identity";

    /// <summary>
    /// Each opaque in <paramref name="body"/> whose reason is <paramref name="reason"/> (<see cref="Unknown.UnboundOpaqueReason"/>,
    /// <see cref="Unknown.AsyncMismatchReason"/>), as a cause on <paramref name="side"/>, in the order the frontend gave them.
    /// </summary>
    private static IEnumerable<UnknownCause> Causes(Codebase side, IrProcedure body, string reason) =>
        body.Blocks
            .SelectMany(static b => b.Instructions)
            .OfType<IrOpaque>()
            .Where(o => string.Equals(o.Reason, reason, StringComparison.Ordinal))
            .Select(o => new UnknownCause(side, o.Reason, o.Span));

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

    /// <summary>
    /// A pair the contracts pass re-verifies: its two bodies and the lowered callee pairs its Equivalent still assumes; as an
    /// item of the <c>contracts</c> phase it weighs what it weighed in <c>verify</c>.
    /// </summary>
    private sealed record ContractCandidate(IrProcedure Old, IrProcedure New, ImmutableArray<CalleePair> Callees)
    {
        public long Weight { get; } = PairWeight.Of(Old, New, solver: true);
    }

    /// <summary>
    /// A pair a later pass verifies again, with the Unknown it has and the bodies the pass verifies: the pair's own in the
    /// budget pass, its IL bodies in the IL pass (<paramref name="FromIl"/>). As an item of the pass's phase it weighs what a pair of <c>verify</c> does.
    /// </summary>
    private sealed record PassCandidate(Unknown Earlier, ProcedurePair Pair, IrProcedure Old, IrProcedure New, bool FromIl)
    {
        public long Weight { get; } = PairWeight.Of(Old, New, solver: true);

        public int Rungs { get; } = PairWeight.Rungs(Old, New);

        /// <summary>
        /// <paramref name="earlier"/> as the result of these bodies: from IL, it names that lowering, applied no API
        /// equivalence and resolved the forwarders the IL bodies' calls name (ADR 0039, ADR 0047).
        /// </summary>
        public VerificationResult Over(VerificationResult earlier) =>
            FromIl ? earlier with { Lowering = "il", EquivalencesApplied = [], ForwardersResolved = Pair.Il!.ForwardersResolved } : earlier;
    }

    /// <summary>What every pass verifies with: the backend, how many pairs at once, and where its warnings go (sonar(src): csharpsquid:S107).</summary>
    private sealed record Verifying(IVerificationBackend Backend, int Jobs, TextWriter Error);

    /// <summary>What the verify phase made of one pair: its result, or the exception that failed it and the stage it failed in (<c>Weighing</c>, <c>Verifying</c>).</summary>
    private sealed record PairOutcome(VerificationResult? Result, string Stage = "", Exception? Crash = null);

    /// <summary>A pair's result decided without the solver, and the outcome the verify phase logs for it.</summary>
    private sealed record Decision(VerificationResult Result, string Outcome);

    /// <summary>A lowered pair ready for the verify phase: its decision without the solver, weight, solver rungs (0 when none) and the exception that ended its weighing.</summary>
    private sealed record Weighing(ProcedurePair Pair, IrProcedure Old, IrProcedure New, Decision? Decided, long Weight, int Rungs, Exception? Crash);

    /// <summary>Where <see cref="Report"/> writes: the SARIF sink, the run log and the run's two text streams (sonar(src): csharpsquid:S107).</summary>
    private sealed record Output(IReportSink Sink, IRunLog Log, Streams Streams);
}
