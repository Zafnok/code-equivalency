using System.CommandLine;

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
        Option<string> failOnOption = new("--fail-on") { DefaultValueFactory = _ => "divergent" };
        failOnOption.AcceptOnlyFromAmong("divergent", "unknown");
        Option<bool> dryRunOption = new("--dry-run");

        Command command = new("compare")
        {
            legacyOption, modernOption, outOption, baselineOption, configOption, failOnOption, dryRunOption,
        };

        command.SetAction(parseResult => Run(
            parseResult.GetValue(legacyOption)!,
            parseResult.GetValue(modernOption)!,
            parseResult.GetValue(outOption)!,
            parseResult.GetValue(baselineOption),
            parseResult.GetValue(configOption),
            parseResult.GetValue(failOnOption)!,
            parseResult.GetValue(dryRunOption),
            frontends,
            backend,
            new FileReportSink(parseResult.GetValue(outOption)!)));

        return command;
    }

    public static int Run(
        string legacyPath,
        string modernPath,
        string outPath,
        string? baselinePath,
        string? configPath,
        string failOn,
        bool dryRun,
        IReadOnlyList<ILanguageFrontend> frontends,
        IVerificationBackend backend,
        IReportSink sink)
    {
        ArgumentNullException.ThrowIfNull(frontends);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(sink);

        if (!File.Exists(legacyPath) || !File.Exists(modernPath))
        {
            Console.Error.WriteLine($"error: file not found (legacy={legacyPath}, modern={modernPath})");
            return ExitCodes.UsageError;
        }

        ILanguageFrontend? frontend = FrontendRouter.Route(frontends, legacyPath, modernPath);
        if (frontend is null)
        {
            Console.Error.WriteLine($"error: no frontend supports both legacy={legacyPath} and modern={modernPath}");
            return ExitCodes.UsageError;
        }

        if (dryRun)
        {
            Console.WriteLine($"route: {frontend.Language} legacy={legacyPath} modern={modernPath} out={outPath}");
            return ExitCodes.Success;
        }

        if (!TryLoadInputs(baselinePath, configPath, out EquivConfig config, out SarifLog? baseline, out int inputErrorExitCode))
        {
            return inputErrorExitCode;
        }

        MatchResult matchResult;
        try
        {
            matchResult = frontend.Analyze(legacyPath, modernPath, config, CancellationToken.None);
        }
        catch (FrontendLoadException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitCodes.LoadFailure;
        }

        List<VerificationResult> results = BuildResults(matchResult, backend, config);
        SarifLog log = SarifReportWriter.Write(results, baseline);
        sink.Write(log);

        return DecideExitCode(results, log, failOn);
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
    /// Every matched pair goes to <paramref name="backend"/> with both lowered bodies. A frontend must
    /// attach them (ticket M2-003); a pair without one is a frontend bug, not an input problem. So is a
    /// backend failure (M3-001 fails loudly on an encoder bug); it is rethrown naming the pair.
    /// </summary>
    private static List<VerificationResult> BuildResults(MatchResult matchResult, IVerificationBackend backend, EquivConfig config)
    {
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);

        List<VerificationResult> results = new(matchResult.Pairs.Length + matchResult.Added.Length + matchResult.Removed.Length);
        foreach (ProcedurePair pair in matchResult.Pairs)
        {
            IrProcedure old = pair.OldBody ?? throw new InvalidOperationException($"The frontend matched {pair.Old.Value} without lowering its legacy body.");
            IrProcedure @new = pair.NewBody ?? throw new InvalidOperationException($"The frontend matched {pair.New.Value} without lowering its modern body.");
            results.Add(new VerificationResult(pair.New, Verify(backend, pair, old, @new, options)));
        }

        foreach (ProcedureIdentity identity in matchResult.Added)
        {
            results.Add(new VerificationResult(identity, new Added()));
        }

        foreach (ProcedureIdentity identity in matchResult.Removed)
        {
            results.Add(new VerificationResult(identity, new Removed()));
        }

        return results;
    }

    private static Verdict Verify(IVerificationBackend backend, ProcedurePair pair, IrProcedure old, IrProcedure @new, VerificationOptions options)
    {
        try
        {
            return backend.Verify(old, @new, options);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Verifying {pair.Old.Value} against {pair.New.Value} failed: {exception.Message}", exception);
        }
    }

    private static int DecideExitCode(List<VerificationResult> results, SarifLog log, string failOn)
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
