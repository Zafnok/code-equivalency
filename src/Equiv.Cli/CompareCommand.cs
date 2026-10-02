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
using Equiv.Core.Verdicts;
using Equiv.Execute;
using Equiv.Execute.Testing;

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

    /// <summary>How many times <see cref="DeleteTemporary"/> tries before it leaves the folder behind.</summary>
    internal const int DeleteAttempts = 5;

    /// <summary>The <c>compare</c> command; <paramref name="execution"/> is where <c>--execute</c> runs, this machine when null.</summary>
    public static Command Create(IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend, ExecutionEnvironment? execution = null)
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
        Option<bool> executeOption = new("--execute");
        Option<bool> chcIntModeOption = new("--chc-int-mode") { DefaultValueFactory = _ => true };
        Option<string?> testTargetOption = new("--test-target");
        testTargetOption.Validators.Add(static result => Validate(result, TestingOptions.TryParse(result.GetValueOrDefault<string?>(), budget: null, out string error), error));
        Option<string?> testBudgetOption = new("--test-budget");
        testBudgetOption.Validators.Add(static result => Validate(result, TestingOptions.TryParse(target: null, result.GetValueOrDefault<string?>(), out string error), error));
        Option<string?> invariantModelOption = new("--invariant-model");
        Option<string> verbosityOption = new("--verbosity") { DefaultValueFactory = _ => "normal" };
        verbosityOption.AcceptOnlyFromAmong("quiet", "normal", "debug");
        Option<string?> logOption = new("--log");
        Option<bool> ilFallbackOption = new("--il-fallback");

        Command command = new("compare")
        {
            legacyOption, modernOption, outOption, baselineOption, configOption, failOnOption, dryRunOption, lowerOnlyOption, executeOption, chcIntModeOption,
            testTargetOption, testBudgetOption, invariantModelOption, verbosityOption, logOption, ilFallbackOption,
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
            },
            frontends,
            backend,
            execution));

        return command;
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
    /// <c>verify</c>, <c>execute</c> and <c>write</c> phases, and the backend hears it through
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

        if (options.LowerOnly && (options.BaselinePath is not null || options.FailOn is not null))
        {
            streams.Error.WriteLine("error: --lower-only cannot be combined with --baseline or --fail-on");
            return ExitCodes.UsageError;
        }

        if (options.Bound is <= 0 || options.TimeoutMs is <= 0)
        {
            streams.Error.WriteLine("error: bound and timeoutMs must be positive integers");
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

        EquivConfig config = loaded with { Bound = options.Bound ?? loaded.Bound, TimeoutMs = options.TimeoutMs ?? loaded.TimeoutMs, IlFallback = options.IlFallback };
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
            [.. lowered.Select(static p => (p.Old, p.New, IsCongruent(p.Pair, p.Old, p.New)))],
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
        (List<VerificationResult> verified, List<Notification> verifyFailures, List<ProcedureIdentity> unverifiedVerified) =
            options.LowerOnly ? ([], [], []) : Verified(lowered, backend, Verification(config, options, runLog), runLog, error);
        pairFailures.AddRange(verifyFailures);
        unverifiedPairs.AddRange(unverifiedVerified);
        List<VerificationResult> results = Executed(
            WithContracts(WithAssumptions(verified, lowered, matchResult), lowered, backend, Verification(config, options, runLog), error), lowered, analysis.Replay, execution, options.Testing, runLog);
        if (!options.LowerOnly)
        {
            census = census with { UnknownByScope = ScopeCounts.Of(results), FailureRefinement = RefinementTime.Of(results) };
        }

        results.AddRange(matchResult.Added.Select(static identity => new VerificationResult(identity, new Added())));
        results.AddRange(matchResult.Removed.Select(static identity => new VerificationResult(identity, new Removed())));
        results.AddRange(matchResult.Ambiguous.Select(static identity => new VerificationResult(identity, new Unknown(UnknownReason.UnmatchedOverload, AmbiguousDetail(identity)))));

        (List<Notification> skippedProjectNotifications, List<ProcedureIdentity> skippedProjectProcedures) = SkippedProjects(matchResult, error);
        List<Notification> notifications = [.. pairFailures, .. skippedProjectNotifications];
        List<ProcedureIdentity> unverified = [.. unverifiedPairs, .. skippedProjectProcedures];
        SarifLog log = SarifReportWriter.Write(
            results,
            baseline,
            RunProperties(analysis, census),
            notifications,
            unverified,
            reviewList: !options.LowerOnly);
        Written(sink, log, options.OutPath, runLog);
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

    /// <summary>The run's property bag (ADR 0027; tickets M3-014, P2-013, P2-053).</summary>
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
    /// <c>proofMethod: observed</c> when the runtimes are seen to differ (ticket P1-008). Execution never makes a result
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
        IRunLog runLog)
    {
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
                    (Unknown unknown, var (pair, old, @new)) => (tester.Test(factory.Plan(pair, unknown.Candidate, directory), old, @new).Apply(result), "tested"),
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

        return result.Config;
    }

    /// <summary>The backend's knobs for this run: the config's bound, timeout and renames, and the command line's rung options.</summary>
    private static VerificationOptions Verification(EquivConfig config, CompareOptions options, IRunLog runLog) =>
        new(config.Bound, config.TimeoutMs, config.CallIdentityRenames)
        {
            ChcIntMode = options.ChcIntMode,
            InvariantModel = options.InvariantModel,
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
    /// the pairs the solver decides (ADR 0038).
    /// </summary>
    private static (List<VerificationResult> Results, List<Notification> Failures, List<ProcedureIdentity> Unverified) Verified(
        List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered, IVerificationBackend backend, VerificationOptions options, IRunLog runLog, TextWriter error)
    {
        if (options.InvariantModel is { } model)
        {
            // Ticket P1-002 criterion 4: rung 5 sends loop IR text to the model, so say so before any pair is verified.
            error.WriteLine($"note: sending loop IR text to {model}");
        }

        List<VerificationResult> results = [];
        List<Notification> failures = [];
        List<ProcedureIdentity> unverified = [];

        List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New, Decision? Decided, long Weight)> pairs =
            [.. lowered.Select(static p => Weighed(p.Pair, p.Old, p.New, Decide(p.Pair, p.Old, p.New)))];
        List<int> solverRungs = [.. pairs.Where(static p => p.Decided is null).Select(static p => PairWeight.Rungs(p.Old, p.New))];
        runLog.Phase("verify", pairs.Count, pairs.Sum(static p => p.Weight), new PhaseBound(solverRungs.Count, options.TimeoutMs, solverRungs.DefaultIfEmpty(1).Max()));
        foreach ((ProcedurePair pair, IrProcedure old, IrProcedure @new, Decision? decided, long weight) in pairs)
        {
            runLog.Item(pair.New.Value, weight);
            if (decided is not null)
            {
                results.Add(decided.Result with { Lowering = pair.Lowering });
                runLog.ItemDone(decided.Outcome);
                continue;
            }

            try
            {
                Verdict verdict = backend.Verify(old, @new, options);
                results.Add(new VerificationResult(pair.New, verdict) { EquivalencesApplied = pair.EquivalencesApplied, Lowering = pair.Lowering });
                runLog.ItemDone(verdict switch
                {
                    Equivalent => "equivalent",
                    Divergent => "divergent",
                    _ => "unknown",
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                failures.Add(PairFailure("Verifying", pair.Old, pair.New, exception, error));
                unverified.Add(pair.New);
                runLog.ItemDone("failed");
            }
        }

        runLog.PhaseDone();
        return (results, failures, unverified);
    }

    private static (ProcedurePair Pair, IrProcedure Old, IrProcedure New, Decision? Decided, long Weight) Weighed(
        ProcedurePair pair, IrProcedure old, IrProcedure @new, Decision? decided) =>
        (pair, old, @new, decided, PairWeight.Of(old, @new, solver: decided is null));

    /// <summary>The result of a pair decided without the solver, with its one-word outcome; null when the solver decides it.</summary>
    private static Decision? Decide(ProcedurePair pair, IrProcedure old, IrProcedure @new)
    {
        // ADR 0029 decision 2: erroneous code is Unknown(Unbound) without asking the solver; it is never evidence of equivalence.
        string unbound = string.Join("; ", UnboundCauses(LegacySide, old).Concat(UnboundCauses(ModernSide, @new)));
        if (unbound.Length > 0)
        {
            return new Decision(new VerificationResult(pair.New, new Unknown(UnknownReason.Unbound, unbound)) { EquivalencesApplied = pair.EquivalencesApplied }, "unbound");
        }

        // Ticket M4-006: exactly one side is async, so exception timing differs; the frontend made both bodies one opaque.
        ImmutableArray<UnknownCause> mismatch = [.. AsyncMismatch(Codebase.Legacy, old), .. AsyncMismatch(Codebase.Modern, @new)];
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
        && !UnboundCauses(LegacySide, old).Any()
        && !UnboundCauses(ModernSide, @new).Any();

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
    /// </summary>
    private static List<VerificationResult> WithContracts(
        List<VerificationResult> results, List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered, IVerificationBackend backend, VerificationOptions options, TextWriter error)
    {
        Dictionary<string, (IrProcedure Old, IrProcedure New)> bodies = lowered.ToDictionary(static p => p.Pair.New.Value, static p => (p.Old, p.New), StringComparer.Ordinal);
        Dictionary<string, VerificationResult> byIdentity = results.ToDictionary(static r => r.Identity.Value, StringComparer.Ordinal);
        return [.. results.Select(result => UnderContracts(result, bodies, byIdentity, backend, options, error))];
    }

    private static VerificationResult UnderContracts(
        VerificationResult result,
        Dictionary<string, (IrProcedure Old, IrProcedure New)> bodies,
        Dictionary<string, VerificationResult> byIdentity,
        IVerificationBackend backend,
        VerificationOptions options,
        TextWriter error)
    {
        ImmutableArray<CalleePair> callees = [.. result.UnprovenAssumptions.Where(bodies.ContainsKey).Select(c => new CalleePair(c, bodies[c].Old, bodies[c].New))];
        if (result.Verdict is not Equivalent || callees.IsEmpty)
        {
            return result;
        }

        (IrProcedure old, IrProcedure @new) = bodies[result.Identity.Value];
        Equivalent? proved;
        try
        {
            proved = backend.VerifyUnderContracts(old, @new, callees, options);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            error.WriteLine($"warning: Verifying {result.Identity.Value} under callee contracts failed, so it keeps its verdict: {exception.Message}");
            return result;
        }

        if (proved is null)
        {
            return result;
        }

        HashSet<string> contracted = new(proved.ContractsUsed.Select(static c => c.Callee), StringComparer.Ordinal);
        string[] inherited = [.. contracted.SelectMany(c => byIdentity.TryGetValue(c, out VerificationResult? callee) ? callee.UnprovenAssumptions : [])];
        return result with
        {
            Verdict = proved,
            AssumedCallees = [.. result.AssumedCallees.Concat(inherited).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            UnprovenAssumptions = [.. result.UnprovenAssumptions.Where(c => !contracted.Contains(c)).Concat(inherited).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        };
    }

    private static IEnumerable<string> Callees(IrProcedure body) =>
        body.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>().Select(static call => call.Callee.Value);

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

    /// <summary>Each <see cref="Unknown.UnboundOpaqueReason"/> opaque in <paramref name="body"/>, as <c>side: unbound at path line:column</c>.</summary>
    private static IEnumerable<string> UnboundCauses(string side, IrProcedure body) =>
        body.Blocks
            .SelectMany(static b => b.Instructions)
            .OfType<IrOpaque>()
            .Where(static o => string.Equals(o.Reason, Unknown.UnboundOpaqueReason, StringComparison.Ordinal))
            .Select(o => string.Create(CultureInfo.InvariantCulture, $"{side}: unbound at {o.Span.Path} {o.Span.StartLine}:{o.Span.StartColumn}"));

    /// <summary>Each <see cref="Unknown.AsyncMismatchReason"/> opaque in <paramref name="body"/>, as a cause on <paramref name="side"/>.</summary>
    private static IEnumerable<UnknownCause> AsyncMismatch(Codebase side, IrProcedure body) =>
        body.Blocks
            .SelectMany(static b => b.Instructions)
            .OfType<IrOpaque>()
            .Where(static o => string.Equals(o.Reason, Unknown.AsyncMismatchReason, StringComparison.Ordinal))
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

    /// <summary>A pair's result decided without the solver, and the outcome the verify phase logs for it.</summary>
    private sealed record Decision(VerificationResult Result, string Outcome);

    /// <summary>Where <see cref="Report"/> writes: the SARIF sink, the run log and the run's two text streams (sonar(src): csharpsquid:S107).</summary>
    private sealed record Output(IReportSink Sink, IRunLog Log, Streams Streams);
}
