using System.ComponentModel;
using System.Globalization;

using Equiv.Cli.Mcp;
using Equiv.Core;
using Equiv.Core.Progress;
using Equiv.Core.Reporting;

using Microsoft.CodeAnalysis.Sarif;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Equiv.Cli;

/// <summary>
/// The tools <c>equiv mcp</c> serves (ADR 0033; ticket M5-001): <c>compare</c> and <c>lower_only</c>. Each runs
/// <see cref="CompareCommand.Run"/>, the pipeline <c>equiv compare</c> runs, with an in-memory sink so nothing is written to
/// disk, and returns a short summary, the verdict counts and then <c>compare</c>'s review list (ticket P2-064), followed by
/// the SARIF log as JSON text. An input error the CLI maps to exit 3 or 4 has
/// no log; it comes back as a tool error (<c>isError: true</c>) carrying the message the CLI prints on stderr.
/// <paramref name="execution"/> also registers <see cref="ProbeTool"/>'s <c>probe</c> tool, only when <c>equiv mcp</c> was
/// started with <c>--execute</c> (ADR 0035; tickets M5-002, P2-056); null leaves it unregistered.
/// </summary>
internal sealed class EquivTools(IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend, ExecutionEnvironment? execution = null)
{
    /// <summary>The report path a tool run passes; never read, since the log goes to <see cref="LogSink"/> and no tool sets DryRun.</summary>
    private const string NoOutPath = "";

    /// <summary>The <c>legacy</c> path both tools take: the names stay, and mean before and after (ADR 0040 decision 4; ticket P2-057).</summary>
    private const string LegacyDescription = "Path to the solution before the change (.sln or .slnx).";

    /// <summary>The <c>modern</c> path both tools take.</summary>
    private const string ModernDescription = "Path to the solution after it (.sln or .slnx).";

    /// <summary>The <c>ilFallback</c> flag both tools take (<c>--il-fallback</c>; ADR 0039, ticket P1-016).</summary>
    private const string IlFallbackDescription = "Lower a pair that is not congruent and holds an opaque the other side lacks again from IL on both sides, keeping the IL bodies when they hold fewer (--il-fallback). Off by default.";

    /// <summary>Every tool, registered explicitly: no assembly scanning (ADR 0033).</summary>
    public IReadOnlyList<McpServerTool> Create()
    {
        List<McpServerTool> tools =
        [
            McpServerTool.Create(
                Compare,
                new McpServerToolCreateOptions
                {
                    Name = "compare",
                    Title = "Compare two solutions",
                    Description = "Checks whether a legacy and a modern solution behave the same. Returns a short summary (the verdict counts, then the review list: flagged results grouped by cause, most certain first), then the SARIF 2.1.0 log `equiv compare` writes.",
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
        if (execution is { } executing)
        {
            tools.Add(new ProbeTool(frontends, executing).Create());
        }

        return tools;
    }

    public CallToolResult Compare(
        [Description(LegacyDescription)] string legacy,
        [Description(ModernDescription)] string modern,
        [Description("Path to an equiv.config.json.")] string? config = null,
        [Description("Path to a previous SARIF log; results already in it are reported as unchanged.")] string? baseline = null,
        [Description("Loop unrolling bound; overrides the config's, must be positive.")] int? bound = null,
        [Description("Solver timeout per procedure pair in milliseconds; overrides the config's, must be positive.")] int? timeoutMs = null,
        [Description(IlFallbackDescription)] bool ilFallback = false) =>
        Run(new CompareOptions(legacy, modern, NoOutPath, baseline, config, FailOn: null, DryRun: false) { Bound = bound, TimeoutMs = timeoutMs, IlFallback = ilFallback });

    public CallToolResult LowerOnly(
        [Description(LegacyDescription)] string legacy,
        [Description(ModernDescription)] string modern,
        [Description("Path to an equiv.config.json.")] string? config = null,
        [Description(IlFallbackDescription)] bool ilFallback = false) =>
        Run(new CompareOptions(legacy, modern, NoOutPath, BaselinePath: null, config, FailOn: null, DryRun: false, LowerOnly: true) { IlFallback = ilFallback });

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
    /// project is a tool-execution notification without an exception or a descriptor; a pair the tool failed on carries an
    /// exception (ADR 0023), and an uncovered runtime range a descriptor (ADR 0040; ticket P2-055).
    /// The review list's lines follow, the ones <c>equiv compare</c> prints on stdout (ticket P2-064); <c>lower_only</c> has none.
    /// </summary>
    private static string Summary(SarifLog log, int exitCode)
    {
        Run run = log.Runs[0];
        int Count(params string[] ruleIds) => run.Results.Count(result => ruleIds.Contains(result.RuleId, StringComparer.Ordinal));
        int skipped = run.Invocations?.SelectMany(static invocation => invocation.ToolExecutionNotifications).Count(static notification => notification.Exception is null && notification.Descriptor is null) ?? 0;
        string verdicts = string.Create(
            CultureInfo.InvariantCulture,
            $"Equivalent {Count("EQ001")}, Divergent {Count("EQ002", "EQ006")}, Unknown {Count("EQ003")}, skipped projects {skipped}, exit code {exitCode}");
        return string.Join('\n', [verdicts, .. ReviewList.Lines(run)]);
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
