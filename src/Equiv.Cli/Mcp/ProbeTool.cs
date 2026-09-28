using System.ComponentModel;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Execution;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Execute;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Equiv.Cli.Mcp;

/// <summary>
/// The <c>probe</c> tool (ADR 0035, ADR 0036; ticket M5-002): an agent names a matched pair by its normalised identity and
/// supplies its own arguments, and gets back both runtimes' outcomes, using the same driver-building path <c>--execute</c>'s
/// replay does (<see cref="IReplayDriverFactory.Probe"/>; ticket M4-009). It never writes SARIF and never changes a
/// verdict. <see cref="McpCommand"/> registers it only when <c>equiv mcp</c> was started with <c>--execute</c> on Windows
/// (ADR 0035: opt-in on every surface, so an agent cannot turn execution on by itself).
/// </summary>
internal sealed class ProbeTool(IReadOnlyList<ILanguageFrontend> frontends, ExecutionEnvironment execution)
{
    public McpServerTool Create() => McpServerTool.Create(
        Probe,
        new McpServerToolCreateOptions
        {
            Name = "probe",
            Title = "Run one matched pair on both runtimes",
            Description =
                "Runs code from both solutions on this machine: calls one matched pair's method on the legacy runtime "
                + "and on the modern runtime with the given arguments, and returns each side's outcome. It never writes "
                + "SARIF and never changes a compare verdict; a mismatch here is a hypothesis, not a proof (ADR 0036).",
            ReadOnly = false,
        });

    public CallToolResult Probe(
        [Description("Path to the legacy solution (.sln or .slnx).")] string legacy,
        [Description("Path to the modern solution (.sln or .slnx).")] string modern,
        [Description("The matched pair's normalised identity, as compare's SARIF logical locations report it.")] string identity,
        [Description("The method's own arguments, in order (its receiver excluded), as JSON values; null for a reference-typed one.")] JsonElement[] arguments,
        [Description("Culture to run the case under; the invariant culture when omitted.")] string? culture = null)
    {
        if (!File.Exists(legacy) || !File.Exists(modern))
        {
            return Failure($"error: file not found (legacy={legacy}, modern={modern})");
        }

        ILanguageFrontend? frontend = FrontendRouter.Route(frontends, legacy, modern);
        if (frontend is null)
        {
            return Failure($"error: no frontend supports both legacy={legacy} and modern={modern}");
        }

        FrontendAnalysis analysis;
        try
        {
            analysis = frontend.Analyze(legacy, modern, EquivConfig.Default, NullRunLog.Instance, CancellationToken.None);
        }
        catch (FrontendLoadException exception)
        {
            return Failure(exception.Message);
        }

        if (analysis.Replay is not { } factory)
        {
            return Failure("error: this frontend cannot run code");
        }

        ProcedurePair? pair = analysis.Match.Pairs.FirstOrDefault(p => string.Equals(p.New.Value, identity, StringComparison.Ordinal));
        if (pair is null)
        {
            return Failure($"error: no matched pair has identity '{identity}'");
        }

        string directory = Directory.CreateTempSubdirectory("equiv-probe-").FullName;
        try
        {
            ReplayPlan plan = factory.Probe(pair, arguments, directory);
            if (plan.Drivers is null)
            {
                return Failure($"error: {plan.Reason}");
            }

            (ExecutionOutcome legacyOutcome, ExecutionOutcome modernOutcome) = new Replayer(execution.Host).Run(plan, culture ?? Replayer.Culture);
            return Success(legacyOutcome, modernOutcome);
        }
        finally
        {
            CompareCommand.DeleteTemporary(directory, static d => Directory.Delete(d, recursive: true), TimeSpan.FromMilliseconds(500));
        }
    }

    private static CallToolResult Success(ExecutionOutcome legacy, ExecutionOutcome modern)
    {
        bool equal = legacy.Kind == modern.Kind && string.Equals(legacy.Canonical, modern.Canonical, StringComparison.Ordinal);
        string json = JsonSerializer.Serialize(new
        {
            legacy = new { kind = Name(legacy.Kind), canonical = legacy.Canonical },
            modern = new { kind = Name(modern.Kind), canonical = modern.Canonical },
            equal,
        });
        return new CallToolResult { Content = [new TextContentBlock { Text = json }] };
    }

    /// <summary>An <see cref="OutcomeKind"/>'s name, spelled out so no enum formatting is involved (matches <c>Equiv.Execute</c>'s own wire form).</summary>
    private static string Name(OutcomeKind kind) => kind switch
    {
        OutcomeKind.Returned => "Returned",
        OutcomeKind.Threw => "Threw",
        OutcomeKind.NotComparable => "NotComparable",
        _ => "NotConstructible",
    };

    private static CallToolResult Failure(string message) => new() { IsError = true, Content = [new TextContentBlock { Text = message }] };
}
