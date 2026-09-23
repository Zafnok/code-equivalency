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
    /// The lowering census and the analysed line counts go into the run's property bag on every run (ADR 0027;
    /// ticket M3-014), and every skipped project is a notification (ADR 0029). <c>--lower-only</c> stops there: the
    /// Added and Removed results, no backend call, exit 0 unless a C# project was skipped.
    /// </summary>
    private static int Report(
        CompareOptions options, FrontendAnalysis analysis, EquivConfig config, IVerificationBackend backend, SarifLog? baseline, IReportSink sink)
    {
        MatchResult matchResult = analysis.Match;
        List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered = Lowered(matchResult);
        LoweringCensus census = LoweringCensus.Compute(
            [.. lowered.Select(static p => (p.Old, p.New))],
            removed: matchResult.Removed.Length,
            added: matchResult.Added.Length,
            projectsSkipped: new SideCounts(matchResult.LegacySkipped.Length, matchResult.ModernSkipped.Length));

        List<VerificationResult> results = options.LowerOnly ? [] : Verified(lowered, backend, config);
        results.AddRange(matchResult.Added.Select(static identity => new VerificationResult(identity, new Added())));
        results.AddRange(matchResult.Removed.Select(static identity => new VerificationResult(identity, new Removed())));

        (List<Notification> notifications, List<ProcedureIdentity> unverified) = SkippedProjects(matchResult);
        SarifLog log = SarifReportWriter.Write(
            results,
            baseline,
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["loweringCensus"] = census.ToProperty(),
                ["analysedLinesOfCode"] = LoweringCensus.Property(new SideCounts(analysis.Lines.Legacy, analysis.Lines.Modern)),
            },
            notifications,
            unverified);
        sink.Write(log);

        bool incomplete = notifications.Exists(static n => n.Level == FailureLevel.Error);
        return (incomplete, options.LowerOnly) switch
        {
            (true, _) => ExitCodes.LoadFailure,
            (false, true) => ExitCodes.Success,
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
    /// Every matched pair goes to <paramref name="backend"/> with both lowered bodies. A backend failure is a bug
    /// too (M3-001 fails loudly on an encoder bug); it is rethrown naming the pair.
    /// </summary>
    private static List<VerificationResult> Verified(List<(ProcedurePair Pair, IrProcedure Old, IrProcedure New)> lowered, IVerificationBackend backend, EquivConfig config)
    {
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        return [.. lowered.Select(p => new VerificationResult(p.Pair.New, Verify(backend, p.Pair, p.Old, p.New, options)))];
    }

    private static Verdict Verify(IVerificationBackend backend, ProcedurePair pair, IrProcedure old, IrProcedure @new, VerificationOptions options)
    {
        // ADR 0029 decision 2: erroneous code is Unknown(Unbound) without asking the solver; it is never evidence of equivalence.
        string unbound = string.Join("; ", UnboundCauses("legacy", old).Concat(UnboundCauses("modern", @new)));
        if (unbound.Length > 0)
        {
            return new Unknown(UnknownReason.Unbound, unbound);
        }

        try
        {
            return backend.Verify(old, @new, options);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Verifying {pair.Old.Value} against {pair.New.Value} failed: {exception.Message}", exception);
        }
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
