using System.ComponentModel;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Progress;
using Equiv.Core.Reporting;

using Microsoft.CodeAnalysis.Sarif;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Equiv.Cli;

/// <summary>
/// The two tools <c>equiv mcp</c> serves (ADR 0033; ticket M5-001): <c>compare</c> and <c>lower_only</c>. Each runs
/// <see cref="CompareCommand.Run"/>, the pipeline <c>equiv compare</c> runs, with an in-memory sink so nothing is written to
/// disk, and returns a one-line summary followed by the SARIF log as JSON text. An input error the CLI maps to exit 3 or 4 has
/// no log; it comes back as a tool error (<c>isError: true</c>) carrying the message the CLI prints on stderr.
/// </summary>
internal sealed class EquivTools(IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend)
{
    /// <summary>Both tools, registered explicitly: no assembly scanning (ADR 0033).</summary>
    public IReadOnlyList<McpServerTool> Create() =>
    [
        McpServerTool.Create(
            Compare,
            new McpServerToolCreateOptions
            {
                Name = "compare",
                Title = "Compare two solutions",
                Description = "Checks whether a legacy and a modern solution behave the same. Returns a one-line verdict summary, then the SARIF 2.1.0 log `equiv compare` writes.",
                ReadOnly = true,
            }),
        McpServerTool.Create(
            LowerOnly,
            new McpServerToolCreateOptions
            {
                Name = "lower_only",
                Title = "Lower two solutions without verifying",
                Description = "Loads, matches and lowers a legacy and a modern solution and reports the lowering census and the added and removed procedures, without calling the solver (`equiv compare --lower-only`).",
                ReadOnly = true,
            }),
    ];

    public CallToolResult Compare(
        [Description("Path to the legacy solution (.sln or .slnx).")] string legacy,
        [Description("Path to the modern solution (.sln or .slnx).")] string modern,
        [Description("Path to an equiv.config.json.")] string? config = null,
        [Description("Path to a previous SARIF log; results already in it are reported as unchanged.")] string? baseline = null,
        [Description("Loop unrolling bound; overrides the config's, must be positive.")] int? bound = null,
        [Description("Solver timeout per procedure pair in milliseconds; overrides the config's, must be positive.")] int? timeoutMs = null) =>
        Run(new CompareOptions(legacy, modern, string.Empty, baseline, config, FailOn: null, DryRun: false) { Bound = bound, TimeoutMs = timeoutMs });

    public CallToolResult LowerOnly(
        [Description("Path to the legacy solution (.sln or .slnx).")] string legacy,
        [Description("Path to the modern solution (.sln or .slnx).")] string modern,
        [Description("Path to an equiv.config.json.")] string? config = null) =>
        Run(new CompareOptions(legacy, modern, string.Empty, BaselinePath: null, config, FailOn: null, DryRun: false, LowerOnly: true));

    private CallToolResult Run(CompareOptions options)
    {
        LogSink sink = new();
        using StringWriter error = new(CultureInfo.InvariantCulture);
        int exitCode;
        try
        {
            // stdout is the protocol channel: the run's own stdout lines go to stderr, and its messages are kept for the tool error.
            exitCode = CompareCommand.Run(options with { Streams = new Streams(Console.Error, error) }, frontends, backend, sink, NullRunLog.Instance);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure(exception.Message);
        }

        string messages = error.ToString();
        Console.Error.Write(messages);
        return sink.Log is { } log ? Success(log, exitCode) : Failure(messages.TrimEnd());
    }

    private static CallToolResult Success(SarifLog log, int exitCode) =>
        new() { Content = [new TextContentBlock { Text = Summary(log, exitCode) }, new TextContentBlock { Text = Json(log) }] };

    private static CallToolResult Failure(string message) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    /// <summary>
    /// The verdict counts, read from the log's rule ids (EQ001 equivalent, EQ002 and EQ006 divergent, EQ003 unknown). A skipped
    /// project is a tool-execution notification without an exception; a pair the tool failed on carries one (ADR 0023).
    /// </summary>
    private static string Summary(SarifLog log, int exitCode)
    {
        Run run = log.Runs[0];
        int Count(params string[] ruleIds) => run.Results.Count(result => ruleIds.Contains(result.RuleId, StringComparer.Ordinal));
        int skipped = run.Invocations?.SelectMany(static invocation => invocation.ToolExecutionNotifications).Count(static notification => notification.Exception is null) ?? 0;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Equivalent {Count("EQ001")}, Divergent {Count("EQ002", "EQ006")}, Unknown {Count("EQ003")}, skipped projects {skipped}, exit code {exitCode}");
    }

    private static string Json(SarifLog log)
    {
        using MemoryStream stream = new();
        log.Save(stream);
        stream.Position = 0;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Keeps the log the pipeline writes, in memory (never a file).</summary>
    private sealed class LogSink : IReportSink
    {
        public SarifLog? Log { get; private set; }

        public void Write(SarifLog log) => Log = log;
    }
}
