using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

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
/// verdict. <see cref="McpCommand"/> registers it only when <c>equiv mcp</c> was started with <c>--execute</c> (ADR 0035:
/// opt-in on every surface, so an agent cannot turn execution on by itself). A pair with a side on .NET Framework is refused
/// off Windows, as <c>compare --execute</c> refuses it (ADR 0040 decision 3; ticket P2-056).
/// </summary>
internal sealed class ProbeTool(IReadOnlyList<ILanguageFrontend> frontends, ExecutionEnvironment execution)
{
    private static readonly JsonSerializerOptions OmitNulls = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public McpServerTool Create() => McpServerTool.Create(
        Probe,
        new McpServerToolCreateOptions
        {
            Name = "probe",
            Title = "Run one matched pair on both runtimes",
            Description =
                "Runs code from both solutions on this machine: calls one matched pair's method on the legacy runtime "
                + "and on the modern runtime with the given arguments, and returns each side's outcome. It never writes "
                + "SARIF and never changes a compare verdict; a mismatch here is a hypothesis, not a proof (ADR 0036). "
                + "equal is absent, and reason says why, when either side's outcome has no comparable form. "
                + "With config, the pair is analysed under that equiv.config.json (its runtimes, renames and other "
                + "declarations), as compare --config does.",
            ReadOnly = false,
        });

    public CallToolResult Probe(
        [Description("Path to the legacy solution (.sln or .slnx).")] string legacy,
        [Description("Path to the modern solution (.sln or .slnx).")] string modern,
        [Description("The matched pair's normalised identity, as compare's SARIF logical locations report it.")] string identity,
        [Description("The method's own arguments, in order (its receiver excluded), as JSON values; null for a reference-typed one, an object by field name for a struct of plain fields. A constructor is called like a static method that returns the new object.")] JsonElement[] arguments,
        [Description("Culture to run the case under; the invariant culture when omitted.")] string? culture = null,
        [Description("Path to the pair's equiv.config.json, as compare's config takes it; the default config when omitted.")] string? config = null)
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

        if (LoadConfig(config, out EquivConfig loaded) is { } configError)
        {
            return Failure(configError);
        }

        FrontendAnalysis analysis;
        try
        {
            analysis = frontend.Analyze(legacy, modern, loaded, NullRunLog.Instance, CancellationToken.None);
        }
        catch (FrontendLoadException exception)
        {
            return Failure(exception.Message);
        }

        if (analysis.Replay is not { } factory)
        {
            return Failure("error: this frontend cannot run code");
        }

        if (execution.Refusal(analysis) is { } refusal)
        {
            return Failure(refusal);
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

            (ExecutionOutcome legacyOutcome, ExecutionOutcome modernOutcome) = new Replayer(execution.Host.Within(directory)).Run(plan, culture ?? Replayer.Culture);
            return Success(legacyOutcome, modernOutcome);
        }
        finally
        {
            CompareCommand.DeleteTemporary(directory, static d => Directory.Delete(d, recursive: true), TimeSpan.FromMilliseconds(500));
        }
    }

    /// <summary>
    /// Loads <paramref name="path"/> as <c>compare --config</c> does (<see cref="EquivConfigLoader"/>; ticket P2-241), or the
    /// default config without one, and returns the error to report, or null. A config diagnostic is an error here, not the
    /// warning <c>compare</c> prints: a tool result has no channel for warnings, and a config taken only in part would
    /// analyse the pair differently from the run the agent is following up.
    /// </summary>
    private static string? LoadConfig(string? path, out EquivConfig config)
    {
        config = EquivConfig.Default;
        if (path is null)
        {
            return null;
        }

        if (!File.Exists(path))
        {
            return $"error: file not found (config={path})";
        }

        EquivConfigResult result;
        try
        {
            result = EquivConfigLoader.Load(File.ReadAllText(path));
        }
        catch (EquivConfigParseException exception)
        {
            return $"error: {exception.Message}";
        }

        if (!result.IsValid)
        {
            return string.Join('\n', result.Diagnostics.Select(static d => $"error: equiv.config.json: {d.Id} {d.Path}: {d.Message}"));
        }

        config = result.Config;
        return null;
    }

    /// <summary>
    /// Both outcomes, and <c>equal</c> when both have a canonical form; otherwise no <c>equal</c>, and a <c>reason</c> naming
    /// the side that cannot be compared (ticket P2-242), since two outcomes with no canonical form say nothing about the values.
    /// </summary>
    private static CallToolResult Success(ExecutionOutcome legacy, ExecutionOutcome modern)
    {
        (bool? equal, string? reason) = Replayer.Compare(legacy, modern);
        string json = JsonSerializer.Serialize(new
        {
            legacy = new { kind = Name(legacy.Kind), canonical = legacy.Canonical },
            modern = new { kind = Name(modern.Kind), canonical = modern.Canonical },
            equal,
            reason,
        }, OmitNulls);
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
