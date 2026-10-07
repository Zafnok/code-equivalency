using System.Collections.Immutable;
using System.CommandLine;
using System.IO.Pipes;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// <c>equiv mcp</c> (ticket M5-001; ADR 0033) over an in-process client and server joined by anonymous pipes, with a fake
/// frontend and backend, plus the <see cref="CompareCommand"/> changes it needs: the run's own lines go through
/// <see cref="Streams"/>, and <c>Bound</c>/<c>TimeoutMs</c> override the config. The tests that read the console redirect it, so
/// this class shares the "Console" collection with <see cref="CompareCommandTests"/> and <see cref="ProgramTests"/>.
/// </summary>
[Collection("Console")]
public sealed class McpCommandTests
{
    private static readonly ProcedureIdentity PairIdentity = new("T::Pair()");
    private static readonly ImmutableDictionary<string, Verdict> NoVerdicts = [];

    [Fact]
    public async Task ListTools_ReturnsCompareAndLowerOnly()
    {
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true), new FakeBackend(NoVerdicts)).ConfigureAwait(true);

        IList<McpClientTool> tools = await session.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(["compare", "lower_only"], tools.Select(static t => t.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.All(tools, static t => Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint));
        McpClientTool compare = tools.Single(static t => string.Equals(t.Name, "compare", StringComparison.Ordinal));
        Assert.Equal("Compare two solutions", compare.ProtocolTool.Title);
        Assert.Equal(
            "Checks whether a legacy and a modern solution behave the same. Returns a short summary (the verdict counts, then the review list: flagged results grouped by cause, most certain first), then the SARIF 2.1.0 log `equiv compare` writes.",
            compare.ProtocolTool.Description);
        Assert.Equal(
            ["legacy", "modern"],
            compare.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(
            ["baseline", "bound", "config", "ilFallback", "legacy", "mode", "modern", "timeoutMs"],
            compare.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        McpClientTool lowerOnly = tools.Single(static t => string.Equals(t.Name, "lower_only", StringComparison.Ordinal));
        Assert.Equal("Lower two solutions without verifying", lowerOnly.ProtocolTool.Title);
        Assert.Equal(
            "Loads, matches and lowers a legacy and a modern solution and reports the lowering census and the added and removed procedures, without calling the solver (`equiv compare --lower-only`).",
            lowerOnly.ProtocolTool.Description);
        Assert.Equal(
            ["config", "ilFallback", "legacy", "modern"],
            lowerOnly.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    /// <summary>The summary's <c>Divergent</c> count matches EQ006 (a runtime-changed callee) as well as EQ002.</summary>
    [Fact]
    public async Task Compare_RuntimeChangedDivergent_CountsAsDivergent()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        CallIdentity flagged = new("System.String::IndexOf(char)", RuntimeChanged: true);
        IrCallRecord record = new(flagged, [new IrBitVecValue(16, 'a')]);
        Counterexample counterexample = Counterexample() with { Old = Run(1) with { Trace = [record] } };
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Divergent(counterexample) });
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        using Session session = await Session.StartAsync(frontend, backend).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);

        Assert.Equal(
            "Equivalent 0, Divergent 1, Unknown 0, skipped projects 0, exit code 1\nreview list: 1 groups for 1 flagged results\n  EQ006 count=1 rank=40.002 runtime-change:System.String::IndexOf(",
            Text(result.Content[0]));
    }

    /// <summary>
    /// Ticket P2-064 criterion 4: the summary holds the lines <c>equiv compare</c> prints for the review list, after the
    /// verdict counts, and nothing reaches stdout, the protocol channel.
    /// </summary>
    [Fact]
    public async Task Compare_SummaryHoldsTheReviewListAndStdoutDoesNot()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity unknown = new("T::Unknown()");
        ProcedureIdentity divergent = new("T::Divergent()");
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded),
            [unknown.Value] = new Unknown(UnknownReason.Timeout, "slow"),
            [divergent.Value] = new Divergent(Counterexample()),
        });
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity), Pair(unknown), Pair(divergent)], [], [], []));
        using Session session = await Session.StartAsync(frontend, backend).ConfigureAwait(true);
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        CallToolResult result;
        try
        {
            result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(
            [
                "Equivalent 1, Divergent 1, Unknown 1, skipped projects 0, exit code 1",
                "review list: 2 groups for 2 flagged results",
                "  EQ002 count=1 rank=60.002 proofMethod:none",
                "  EQ003 count=1 rank=0.002 timeout",
            ],
            Text(result.Content[0]).Split('\n'),
            StringComparer.Ordinal);
        Assert.Empty(stdout.ToString());
        Assert.True(Parse(Text(result.Content[1])).Runs[0].TryGetSerializedPropertyValue("reviewList", out string? _));
    }

    [Fact]
    public async Task Server_AdvertisesEquivAndTheMinVerVersion()
    {
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true), new FakeBackend(NoVerdicts)).ConfigureAwait(true);

        Assert.Equal("equiv", session.Client.ServerInfo.Name);
        Assert.Equal(McpCommand.Version, session.Client.ServerInfo.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+", McpCommand.Version);
    }

    [Fact]
    public async Task Server_ExitsZeroWhenInputCloses()
    {
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true), new FakeBackend(NoVerdicts)).ConfigureAwait(true);

        int exitCode = await session.CloseAsync().ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
    }

    [Fact]
    public async Task Create_ServesOverTheGivenStreamsAndExitsZero()
    {
        using AnonymousPipeServerStream clientToServer = new(PipeDirection.Out);
        using AnonymousPipeClientStream serverInput = new(PipeDirection.In, clientToServer.ClientSafePipeHandle);
        using AnonymousPipeServerStream serverToClient = new(PipeDirection.Out);
        using AnonymousPipeClientStream clientInput = new(PipeDirection.In, serverToClient.ClientSafePipeHandle);
        Command command = McpCommand.Create([], new FakeBackend(NoVerdicts), execution: null, () => (serverInput, serverToClient));

        Task<int> server = Task.Run(() => command.Parse(["mcp"]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        McpClient client = await McpClient.CreateAsync(new StreamClientTransport(clientToServer, clientInput), cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal("equiv", client.ServerInfo.Name);
        await client.DisposeAsync().ConfigureAwait(true);
        await clientToServer.DisposeAsync().ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, await server.ConfigureAwait(true));
    }

    /// <summary>
    /// Ticket M5-002 criteria 1 and 3, through the real <c>--execute</c> command-line path rather than <see cref="Session"/>'s
    /// direct <see cref="McpCommand.ServeAsync"/> call: on Windows it prints ADR 0035's note, serves <c>probe</c> and exits 0.
    /// </summary>
    [Fact]
    public async Task Create_WithExecuteOnWindows_PrintsNoteAndServesProbe()
    {
        using AnonymousPipeServerStream clientToServer = new(PipeDirection.Out);
        using AnonymousPipeClientStream serverInput = new(PipeDirection.In, clientToServer.ClientSafePipeHandle);
        using AnonymousPipeServerStream serverToClient = new(PipeDirection.Out);
        using AnonymousPipeClientStream clientInput = new(PipeDirection.In, serverToClient.ClientSafePipeHandle);
        ExecutionEnvironment windows = new(IsWindows: true, new FakeReplay(string.Empty, string.Empty));
        Command command = McpCommand.Create([], new FakeBackend(NoVerdicts), windows, () => (serverInput, serverToClient));
        TextWriter originalError = Console.Error;
        using StringWriter error = new();
        Console.SetError(error);
        Task<int> server;
        IList<McpClientTool> tools;
        try
        {
            server = Task.Run(() => command.Parse(["mcp", "--execute"]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
            McpClient client = await McpClient.CreateAsync(new StreamClientTransport(clientToServer, clientInput), cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            await client.DisposeAsync().ConfigureAwait(true);
            await clientToServer.DisposeAsync().ConfigureAwait(true);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(ExitCodes.Success, await server.ConfigureAwait(true));
        Assert.Contains(ExecutionEnvironment.Note, error.ToString(), StringComparison.Ordinal);
        Assert.Contains(tools, static t => string.Equals(t.Name, "probe", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_RejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => McpCommand.Create(null!, new FakeBackend(NoVerdicts), execution: null, () => (Stream.Null, Stream.Null)));
        Assert.Throws<ArgumentNullException>(() => McpCommand.Create([], null!, execution: null, () => (Stream.Null, Stream.Null)));
        Assert.Throws<ArgumentNullException>(() => McpCommand.Create([], new FakeBackend(NoVerdicts), execution: null, null!));
    }

    /// <summary>The root command lists <c>mcp</c>; the process-stdio overload builds it without opening any stream.</summary>
    [Fact]
    public void Program_ListsTheMcpCommand()
    {
        int exitCode = ExitCodes.UsageError;
        string help = CaptureStdOut(() => exitCode = Program.Main(["mcp", "--help"]));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("MCP server", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-055: an uncovered runtime range is a notification without an exception, as a skipped project's is, but it
    /// carries a descriptor and is not counted as a skipped project.
    /// </summary>
    [Fact]
    public async Task Compare_AnUncoveredRuntimeRangeIsNotASkippedProject()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedurePair pair = Pair(PairIdentity) with { Runtimes = new RuntimeInterval(TargetRuntime.Parse("netcoreapp2.1")!, TargetRuntime.Parse("net8.0")!) };
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([pair], [], [], []));
        using Session session = await Session.StartAsync(frontend, EquivalentBackend()).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);

        Assert.Equal("Equivalent 1, Divergent 0, Unknown 0, skipped projects 0, exit code 0\nreview list: 0 groups for 0 flagged results", Text(result.Content[0]));
        Assert.Contains("uncovered-runtime-range", Text(result.Content[1]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_ReturnsSummaryThenSarif()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity other = new("T::Other()");
        ProcedureIdentity unknown = new("T::Unknown()");
        ProcedureIdentity divergent = new("T::Divergent()");
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded),
            [other.Value] = new Equivalent(ProofMethod.Bounded),
            [unknown.Value] = new Unknown(UnknownReason.Timeout, "slow"),
            [divergent.Value] = new Divergent(Counterexample()),
        });
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity), Pair(other), Pair(unknown), Pair(divergent)], [new ProcedureIdentity("T::Added()")], [], []));
        using Session session = await Session.StartAsync(frontend, backend).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(2, result.Content.Count);
        Assert.Equal("Equivalent 2, Divergent 1, Unknown 1, skipped projects 0, exit code 1", VerdictLine(result));
        SarifLog log = Parse(Text(result.Content[1]));
        Assert.Equal(5, log.Runs[0].Results.Count);
        Assert.Single(backend.Calls.Select(static o => o.Bound).Distinct());
        Assert.Equal(EquivConfig.Default.Bound, backend.Calls[0].Bound);
    }

    /// <summary><c>config</c> is read, and <c>bound</c> and <c>timeoutMs</c> override the config's own values.</summary>
    [Fact]
    public async Task Compare_ConfigBoundAndTimeoutReachTheBackend()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        using TempFile config = new();
        await File.WriteAllTextAsync(config.Path, """{ "bound": 9, "timeoutMs": 900 }""", TestContext.Current.CancellationToken).ConfigureAwait(true);
        FakeBackend backend = EquivalentBackend();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        using Session session = await Session.StartAsync(frontend, backend).ConfigureAwait(true);

        await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path, config: config.Path)).ConfigureAwait(true);
        await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path, config: config.Path, bound: 4, timeoutMs: 40)).ConfigureAwait(true);

        Assert.Equal([(9, 900), (4, 40)], backend.Calls.Select(static o => (o.Bound, o.TimeoutMs)));
    }

    /// <summary>Ticket P1-016 criterion 1: both tools take <c>ilFallback</c>, off unless given, and pass it to the frontend as <c>--il-fallback</c> does.</summary>
    [Theory]
    [InlineData("compare")]
    [InlineData("lower_only")]
    public async Task IlFallbackIsOffUnlessGiven(string tool)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        using Session session = await Session.StartAsync(frontend, EquivalentBackend()).ConfigureAwait(true);

        await session.CallAsync(tool, Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);
        bool off = frontend.LastConfig!.IlFallback;
        await session.CallAsync(tool, Args(legacy: legacy.Path, modern: modern.Path, ilFallback: true)).ConfigureAwait(true);

        Assert.False(off);
        Assert.True(frontend.LastConfig!.IlFallback);
    }

    /// <summary>
    /// Ticket P1-032 criterion 1 (ADR 0049 decision 1, ADR 0052): <c>compare</c> takes <c>mode</c> as <c>--mode</c> does,
    /// quick unless given, and any other value is a tool error, not a run.
    /// </summary>
    [Fact]
    public async Task Compare_ModeSelectsTheModeAndAnyOtherValueIsAToolError()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        using Session session = await Session.StartAsync(frontend, EquivalentBackend()).ConfigureAwait(true);
        Dictionary<string, object?> Mode(string mode) => new(Args(legacy: legacy.Path, modern: modern.Path), StringComparer.Ordinal) { ["mode"] = mode };

        await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);
        CompareMode byDefault = frontend.LastConfig!.Mode;
        CallToolResult thorough = await session.CallAsync("compare", Mode("thorough")).ConfigureAwait(true);
        CompareMode asked = frontend.LastConfig!.Mode;
        CallToolResult other = await session.CallAsync("compare", Mode("fast")).ConfigureAwait(true);

        Assert.Equal((CompareMode.Quick, CompareMode.Thorough), (byDefault, asked));
        Assert.NotEqual(true, thorough.IsError);
        Assert.Contains("\"name\":\"thorough\"", Assert.IsType<TextContentBlock>(thorough.Content[1]).Text, StringComparison.Ordinal);
        Assert.True(other.IsError);
        Assert.Equal("error: mode must be thorough or quick, not 'fast'", Assert.IsType<TextContentBlock>(Assert.Single(other.Content)).Text);
        Assert.Equal(2, frontend.AnalyzeCallCount);
    }

    /// <summary><c>baseline</c> works as <c>--baseline</c> does: a result already in the baseline is unchanged, so the run exits 0 and says so.</summary>
    [Fact]
    public async Task Compare_BaselineMarksResultsUnchanged()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        using TempFile baseline = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Divergent(Counterexample()) });
        using Session session = await Session.StartAsync(frontend, backend).ConfigureAwait(true);
        CallToolResult first = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);
        await File.WriteAllTextAsync(baseline.Path, Text(first.Content[1]), TestContext.Current.CancellationToken).ConfigureAwait(true);

        CallToolResult second = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path, baseline: baseline.Path)).ConfigureAwait(true);

        // The unchanged Divergent is in the run's list and not in the summary's, which counts new and updated results (ticket P2-064).
        Assert.Equal("Equivalent 0, Divergent 1, Unknown 0, skipped projects 0, exit code 0\nreview list: 0 groups for 0 flagged results", Text(second.Content[0]));
        Assert.All(Parse(Text(second.Content[1])).Runs[0].Results, static r => Assert.Equal(BaselineState.Unchanged, r.BaselineState));
    }

    [Fact]
    public async Task Compare_SkippedProject_IsAResultWithExitCode4NotAToolError()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        MatchResult match = new([Pair(PairIdentity)], [], [], [])
        {
            LegacySkipped = [new UnverifiedProject("Lib", "Lib", IsCSharp: true, ["CS0246: missing"], [])],
        };
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true, match), EquivalentBackend()).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Equivalent 1, Divergent 0, Unknown 0, skipped projects 1, exit code 4", VerdictLine(result));
    }

    [Fact]
    public async Task Compare_MissingSolution_IsToolError()
    {
        using TempFile legacy = new();
        string missing = Path.Combine(Path.GetTempPath(), $"equiv-M5-001-missing-{Guid.NewGuid():N}.sln");
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true), new FakeBackend(NoVerdicts)).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: missing)).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Equal($"error: file not found (legacy={legacy.Path}, modern={missing})", Assert.Single(result.Content.Select(Text)));
    }

    [Fact]
    public async Task Compare_UnsupportedInput_IsToolError()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => false);
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts)).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Equal($"error: no frontend supports both legacy={legacy.Path} and modern={modern.Path}", Assert.Single(result.Content.Select(Text)));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(null, 0)]
    [InlineData(null, -5)]
    public async Task Compare_NonPositiveBoundOrTimeout_IsToolError(int? bound, int? timeoutMs)
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeBackend backend = new(NoVerdicts);
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true), backend).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path, bound: bound, timeoutMs: timeoutMs)).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Equal("error: bound, timeoutMs, resourceLimit and jobs must be positive integers", Assert.Single(result.Content.Select(Text)));
        Assert.Empty(backend.Calls);
    }

    [Fact]
    public async Task Compare_MissingConfig_IsToolError()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        string missing = Path.Combine(Path.GetTempPath(), $"equiv-M5-001-missing-{Guid.NewGuid():N}.json");
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true), new FakeBackend(NoVerdicts)).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path, config: missing)).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Equal($"error: file not found (config={missing})", Assert.Single(result.Content.Select(Text)));
    }

    /// <summary>Exit 4 with no SARIF log (no C# project loaded) is a tool error carrying the frontend's message, as on stderr.</summary>
    [Fact]
    public async Task Compare_LoadFailure_IsToolError()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, throwOnAnalyze: new FrontendLoadException("no project loaded"));
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts)).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Equal("no project loaded", Assert.Single(result.Content.Select(Text)));
    }

    /// <summary>An exception that escapes the pipeline is a tool error with its message, not a protocol error and not a crash (ADR 0023's exit 5 case).</summary>
    [Fact]
    public async Task Compare_UnhandledException_IsToolError()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        IrProcedure body = IrText.Parse($"proc \"{PairIdentity.Value}\" () entry B0 B0: ret");
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([new ProcedurePair(PairIdentity, PairIdentity, body, NewBody: null)], [], [], []));
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts)).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains(PairIdentity.Value, Assert.Single(result.Content.Select(Text)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LowerOnly_NeverCallsBackend()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeBackend backend = EquivalentBackend();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [new ProcedureIdentity("T::Added()")], [new ProcedureIdentity("T::Removed()")], []));
        using Session session = await Session.StartAsync(frontend, backend).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("lower_only", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);

        Assert.Empty(backend.Calls);
        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Equivalent 0, Divergent 0, Unknown 0, skipped projects 0, exit code 0", Text(result.Content[0]));
        Assert.Equal(["EQ004", "EQ005"], Parse(Text(result.Content[1])).Runs[0].Results.Select(static r => r.RuleId).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    /// <summary>Ticket M5-002 criterion 1: without <c>--execute</c>, <c>probe</c> is not registered at all.</summary>
    [Fact]
    public async Task Probe_NotRegisteredWithoutExecute()
    {
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true), new FakeBackend(NoVerdicts)).ConfigureAwait(true);

        IList<McpClientTool> tools = await session.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.DoesNotContain(tools, static t => string.Equals(t.Name, "probe", StringComparison.Ordinal));
    }

    /// <summary>With no environment given, <c>--execute</c> runs on this machine and prints the note on any OS (ticket P2-056).</summary>
    [Fact]
    public void Probe_WithoutAnEnvironment_UsesThisMachine()
    {
        Command command = McpCommand.Create([], new FakeBackend(NoVerdicts), execution: null, () => (Stream.Null, Stream.Null));
        int exitCode = ExitCodes.Success;
        string stderr = CaptureStdErr(() =>
            exitCode = command.Parse(["mcp", "--execute"]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken).GetAwaiter().GetResult());

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.StartsWith(ExecutionEnvironment.Note, stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-056 criterion 3: off Windows <c>probe</c> is registered, refuses a pair with a side on .NET Framework, naming
    /// the project and its runtime, and runs a pair whose sides are both .NET.
    /// </summary>
    [Fact]
    public async Task Probe_OffWindows_RefusesOnlyAFrameworkPair()
    {
        FakeReplay replay = new("[\"Returned\",1]", "[\"Returned\",1]");
        ExecutionEnvironment nonWindows = new(IsWindows: false, replay);
        MatchResult match = new([Pair(PairIdentity)], [], [], []);
        FakeFrontend framework = new("csharp", _ => true, match, replay: replay, legacyRuntimes: [("Legacy", "net48", "attribute")], modernRuntimes: [("Modern", "net10.0", "attribute")]);
        FakeFrontend core = new("csharp", _ => true, match, replay: replay, legacyRuntimes: [("Legacy", "net8.0", "attribute")], modernRuntimes: [("Modern", "net8.0", "attribute")]);
        using TempFile legacy = new();
        using TempFile modern = new();

        using (Session session = await Session.StartAsync(framework, new FakeBackend(NoVerdicts), nonWindows).ConfigureAwait(true))
        {
            CallToolResult refused = await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, [null])).ConfigureAwait(true);

            Assert.True(refused.IsError);
            Assert.Equal("error: --execute: Legacy runs on net48, and .NET Framework needs Windows (ADR 0040)", Text(Assert.Single(refused.Content)));
            Assert.Empty(replay.Probes);
        }

        using (Session session = await Session.StartAsync(core, new FakeBackend(NoVerdicts), nonWindows).ConfigureAwait(true))
        {
            CallToolResult ran = await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, [null])).ConfigureAwait(true);

            Assert.NotEqual(true, ran.IsError);
            Assert.Single(replay.Probes);
        }
    }

    /// <summary>Ticket M5-002 criteria 1 and 3: with <c>--execute</c> on Windows, <c>probe</c> is registered and says it runs code on the host.</summary>
    [Fact]
    public async Task Probe_IsRegisteredWithExecuteOnWindowsAndNamesItsHostSideEffect()
    {
        ExecutionEnvironment windows = new(IsWindows: true, new FakeReplay(string.Empty, string.Empty));
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true), new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        IList<McpClientTool> tools = await session.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

        McpClientTool probe = tools.Single(static t => string.Equals(t.Name, "probe", StringComparison.Ordinal));
        Assert.Equal("Run one matched pair on both runtimes", probe.ProtocolTool.Title);
        Assert.Equal(
            "Runs code from both solutions on this machine: calls one matched pair's method on the legacy runtime "
            + "and on the modern runtime with the given arguments, and returns each side's outcome. It never writes "
            + "SARIF and never changes a compare verdict; a mismatch here is a hypothesis, not a proof (ADR 0036).",
            probe.ProtocolTool.Description);
        Assert.NotEqual(true, probe.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Equal(
            ["arguments", "culture", "identity", "legacy", "modern"],
            probe.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(
            ["arguments", "identity", "legacy", "modern"],
            probe.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    /// <summary>Ticket M5-002 criterion 2: an agent's own arguments come back as both runtimes' outcomes, never a verdict.</summary>
    [Fact]
    public async Task Probe_ReturnsBothOutcomes()
    {
        FakeReplay replay = new("[\"Threw\",\"System.ArgumentNullException\"]", "[\"Threw\",\"System.NullReferenceException\"]");
        ExecutionEnvironment windows = new(IsWindows: true, replay);
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []), replay: replay);
        using TempFile legacy = new();
        using TempFile modern = new();
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, [null])).ConfigureAwait(true);

        Assert.NotEqual(true, result.IsError);
        using JsonDocument document = JsonDocument.Parse(Text(Assert.Single(result.Content)));
        Assert.Equal("Threw", document.RootElement.GetProperty("legacy").GetProperty("kind").GetString());
        Assert.Equal("\"System.ArgumentNullException\"", document.RootElement.GetProperty("legacy").GetProperty("canonical").GetString());
        Assert.Equal("Threw", document.RootElement.GetProperty("modern").GetProperty("kind").GetString());
        Assert.Equal("\"System.NullReferenceException\"", document.RootElement.GetProperty("modern").GetProperty("canonical").GetString());
        Assert.False(document.RootElement.GetProperty("equal").GetBoolean());
        Assert.Single(replay.Probes);
    }

    /// <summary>Every <see cref="OutcomeKind"/> is reported by its own name, and equal answers on both sides give <c>equal: true</c>.</summary>
    [Theory]
    [InlineData("[\"Returned\",1]", "Returned")]
    [InlineData("[\"Threw\",\"E\"]", "Threw")]
    [InlineData("[\"NotComparable\",\"X\"]", "NotComparable")]
    [InlineData("[\"NotConstructible\",\"Y\"]", "NotConstructible")]
    public async Task Probe_ReportsEveryOutcomeKindByName(string answer, string expectedKind)
    {
        FakeReplay replay = new(answer, answer);
        ExecutionEnvironment windows = new(IsWindows: true, replay);
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []), replay: replay);
        using TempFile legacy = new();
        using TempFile modern = new();
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, [null])).ConfigureAwait(true);

        Assert.NotEqual(true, result.IsError);
        using JsonDocument document = JsonDocument.Parse(Text(Assert.Single(result.Content)));
        Assert.Equal(expectedKind, document.RootElement.GetProperty("legacy").GetProperty("kind").GetString());
        Assert.Equal(expectedKind, document.RootElement.GetProperty("modern").GetProperty("kind").GetString());
        Assert.True(document.RootElement.GetProperty("equal").GetBoolean());
    }

    /// <summary>Differing kinds are never equal, without comparing their canonical text (the <c>&amp;&amp;</c> short-circuits).</summary>
    [Fact]
    public async Task Probe_DifferingKinds_AreNeverEqual()
    {
        FakeReplay replay = new("[\"Returned\",1]", "[\"Threw\",\"E\"]");
        ExecutionEnvironment windows = new(IsWindows: true, replay);
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []), replay: replay);
        using TempFile legacy = new();
        using TempFile modern = new();
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, [null])).ConfigureAwait(true);

        using JsonDocument document = JsonDocument.Parse(Text(Assert.Single(result.Content)));
        Assert.False(document.RootElement.GetProperty("equal").GetBoolean());
    }

    /// <summary>Ticket M5-002 criterion 2: an argument that cannot be built is a tool error naming it.</summary>
    [Fact]
    public async Task Probe_UnconstructibleParameter_IsToolError()
    {
        FakeReplay replay = new(string.Empty, string.Empty) { ProbeReason = "legacy: no System.Int32 argument can be built for x from \"oops\"" };
        ExecutionEnvironment windows = new(IsWindows: true, replay);
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []), replay: replay);
        using TempFile legacy = new();
        using TempFile modern = new();
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, ["oops"])).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("no System.Int32 argument can be built for x", Text(Assert.Single(result.Content)), StringComparison.Ordinal);
    }

    /// <summary>Ticket M5-002 criterion 2: an identity that matches no pair is a tool error, not a crash.</summary>
    [Fact]
    public async Task Probe_UnknownIdentity_IsToolError()
    {
        FakeReplay replay = new(string.Empty, string.Empty);
        ExecutionEnvironment windows = new(IsWindows: true, replay);
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []), replay: replay);
        using TempFile legacy = new();
        using TempFile modern = new();
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, "T::NoSuchPair()", [])).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("no matched pair has identity 'T::NoSuchPair()'", Text(Assert.Single(result.Content)), StringComparison.Ordinal);
        Assert.Empty(replay.Probes);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Probe_MissingSolution_IsToolError(bool legacyMissing, bool modernMissing)
    {
        FakeReplay replay = new(string.Empty, string.Empty);
        ExecutionEnvironment windows = new(IsWindows: true, replay);
        using TempFile existingLegacy = new();
        using TempFile existingModern = new();
        string missing = Path.Combine(Path.GetTempPath(), $"equiv-M5-002-missing-{Guid.NewGuid():N}.sln");
        string legacy = legacyMissing ? missing : existingLegacy.Path;
        string modern = modernMissing ? missing : existingModern.Path;
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true), new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("probe", ProbeArgs(legacy, modern, PairIdentity.Value, [])).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Equal($"error: file not found (legacy={legacy}, modern={modern})", Text(Assert.Single(result.Content)));
    }

    [Fact]
    public async Task Probe_UnsupportedInput_IsToolError()
    {
        ExecutionEnvironment windows = new(IsWindows: true, new FakeReplay(string.Empty, string.Empty));
        using TempFile legacy = new();
        using TempFile modern = new();
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => false), new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, [])).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Equal($"error: no frontend supports both legacy={legacy.Path} and modern={modern.Path}", Text(Assert.Single(result.Content)));
    }

    [Fact]
    public async Task Probe_LoadFailure_IsToolError()
    {
        ExecutionEnvironment windows = new(IsWindows: true, new FakeReplay(string.Empty, string.Empty));
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, throwOnAnalyze: new FrontendLoadException("no project loaded"));
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, [])).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Equal("no project loaded", Text(Assert.Single(result.Content)));
    }

    /// <summary>A frontend with no replay factory (<see cref="FrontendAnalysis.Replay"/> null) cannot run code at all.</summary>
    [Fact]
    public async Task Probe_FrontendCannotRunCode_IsToolError()
    {
        ExecutionEnvironment windows = new(IsWindows: true, new FakeReplay(string.Empty, string.Empty));
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []));
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, [])).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Equal("error: this frontend cannot run code", Text(Assert.Single(result.Content)));
    }

    private static Dictionary<string, object?> ProbeArgs(string legacy, string modern, string identity, object?[] arguments, string? culture = null)
    {
        Dictionary<string, object?> args = new(StringComparer.Ordinal) { ["legacy"] = legacy, ["modern"] = modern, ["identity"] = identity, ["arguments"] = arguments };
        if (culture is not null)
        {
            args["culture"] = culture;
        }

        return args;
    }

    /// <summary>An explicit <c>culture</c> reaches the driver's case line; it is not always the invariant culture.</summary>
    [Fact]
    public async Task Probe_CultureReachesTheDriver()
    {
        FakeReplay replay = new("[\"Returned\",1]", "[\"Returned\",1]");
        ExecutionEnvironment windows = new(IsWindows: true, replay);
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []), replay: replay);
        using TempFile legacy = new();
        using TempFile modern = new();
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, [null], culture: "fr-FR")).ConfigureAwait(true);

        Assert.All(replay.Lines, static line => Assert.StartsWith("[\"fr-FR\"", line, StringComparison.Ordinal));
        Assert.NotEmpty(replay.Lines);
    }

    /// <summary>The temporary directory a probe built its drivers in is deleted afterwards, as <c>--execute</c>'s replay does.</summary>
    [Fact]
    public async Task Probe_DeletesItsTemporaryDirectoryAfterward()
    {
        FakeReplay replay = new("[\"Returned\",1]", "[\"Returned\",1]");
        ExecutionEnvironment windows = new(IsWindows: true, replay);
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []), replay: replay);
        using TempFile legacy = new();
        using TempFile modern = new();
        using Session session = await Session.StartAsync(frontend, new FakeBackend(NoVerdicts), windows).ConfigureAwait(true);

        await session.CallAsync("probe", ProbeArgs(legacy.Path, modern.Path, PairIdentity.Value, [null])).ConfigureAwait(true);

        string directory = Assert.Single(replay.Probes).Directory;
        Assert.StartsWith("equiv-probe-", Path.GetFileName(directory), StringComparison.Ordinal);
        Assert.False(Directory.Exists(directory));

        // The drivers start under that temporary folder, not in the caller's working directory (ticket P2-040).
        Assert.Equal([directory], replay.Hosts);
    }

    [Fact]
    public async Task LowerOnly_MissingSolution_IsToolError()
    {
        using TempFile modern = new();
        using Session session = await Session.StartAsync(new FakeFrontend("csharp", _ => true), new FakeBackend(NoVerdicts)).ConfigureAwait(true);

        CallToolResult result = await session.CallAsync("lower_only", Args(legacy: "missing.sln", modern: modern.Path)).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.StartsWith("error: file not found", Assert.Single(result.Content.Select(Text)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterion 5: in <c>mcp</c> mode nothing the pipeline prints reaches <see cref="Console.Out"/> (the protocol channel);
    /// the analysed line counts go to stderr, and a tool error's message is written there too.
    /// </summary>
    [Fact]
    public async Task McpMode_WritesNothingButProtocolToStdout()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([Pair(PairIdentity)], [], [], []), lines: new AnalysedLines(12, 34));
        using Session session = await Session.StartAsync(frontend, EquivalentBackend()).ConfigureAwait(true);
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            await session.CallAsync("compare", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);
            await session.CallAsync("lower_only", Args(legacy: legacy.Path, modern: modern.Path)).ConfigureAwait(true);
            await session.CallAsync("compare", Args(legacy: "missing.sln", modern: modern.Path)).ConfigureAwait(true);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Empty(stdout.ToString());
        Assert.Contains("analysed lines of code: legacy=12 modern=34", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("error: file not found", stderr.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Criterion 5: without <see cref="CompareOptions.Streams"/>, <c>equiv compare</c> writes the same lines to the console as before.</summary>
    [Fact]
    public void CompareCommand_OutputUnchanged()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, lines: new AnalysedLines(5, 6));
        CompareOptions dryRun = new(legacy.Path, modern.Path, "out.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: true);
        int exitCode = ExitCodes.InternalError;

        string stdout = CaptureStdOut(() => exitCode = CompareCommand.Run(dryRun, [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));
        string stderr = CaptureStdErr(() => CompareCommand.Run(dryRun with { LegacyPath = "missing.sln" }, [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            $"route: csharp legacy={legacy.Path} modern={modern.Path} out=out.sarif{Environment.NewLine}analysed lines of code: legacy=5 modern=6{Environment.NewLine}",
            stdout);
        Assert.Equal($"error: file not found (legacy=missing.sln, modern={modern.Path}){Environment.NewLine}", stderr);
    }

    /// <summary>With <see cref="CompareOptions.Streams"/> the same lines go to the given writers and the console hears nothing.</summary>
    [Fact]
    public void CompareCommand_WritesToTheGivenStreams()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        FakeFrontend frontend = new("csharp", _ => true, lines: new AnalysedLines(5, 6));
        using StringWriter output = new();
        using StringWriter error = new();
        CompareOptions dryRun = new(legacy.Path, modern.Path, "out.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: true) { Streams = new Streams(output, error) };
        int exitCode = ExitCodes.InternalError;

        string console = CaptureStdOut(() => CaptureStdErr(() =>
        {
            exitCode = CompareCommand.Run(dryRun, [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance);
            CompareCommand.Run(dryRun with { ModernPath = "missing.sln" }, [frontend], new FakeBackend(NoVerdicts), new InMemoryReportSink(), NullRunLog.Instance);
        }));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(console);
        Assert.Contains("route: csharp", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("analysed lines of code: legacy=5 modern=6", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("error: file not found", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A tool call's arguments; a null one is left out, as an agent that does not pass an optional argument does.</summary>
    private static Dictionary<string, object?> Args(string legacy, string modern, string? config = null, string? baseline = null, int? bound = null, int? timeoutMs = null, bool? ilFallback = null)
    {
        Dictionary<string, object?> arguments = new(StringComparer.Ordinal) { ["legacy"] = legacy, ["modern"] = modern };
        AddIfPresent(arguments, "config", config);
        AddIfPresent(arguments, "baseline", baseline);
        AddIfPresent(arguments, "bound", bound);
        AddIfPresent(arguments, "timeoutMs", timeoutMs);
        AddIfPresent(arguments, "ilFallback", ilFallback);
        return arguments;
    }

    private static void AddIfPresent(Dictionary<string, object?> arguments, string name, object? value)
    {
        if (value is not null)
        {
            arguments[name] = value;
        }
    }

    private static FakeBackend EquivalentBackend() =>
        new(new Dictionary<string, Verdict>(StringComparer.Ordinal) { [PairIdentity.Value] = new Equivalent(ProofMethod.Bounded) });

    private static ProcedurePair Pair(ProcedureIdentity identity)
    {
        IrProcedure body = IrText.Parse($"proc \"{identity.Value}\" () entry B0 B0: ret");
        return new ProcedurePair(identity, identity, body, body);
    }

    private static Counterexample Counterexample() =>
        new(new IrInputs([new IrBitVecValue(32, 0)]), Run(1), Run(2));

    private static IrRun Run(int returned) => new(new IrReturned(new IrBitVecValue(32, (ulong)returned)), [], []);

    private static string Text(ContentBlock block) => Assert.IsType<TextContentBlock>(block).Text;

    /// <summary>The summary's first line, the verdict counts; the review list's lines follow it (ticket P2-064).</summary>
    private static string VerdictLine(CallToolResult result) => Text(result.Content[0]).Split('\n')[0];

    private static SarifLog Parse(string json)
    {
        using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(json));
        return SarifLog.Load(stream, deferred: false);
    }

    private static string CaptureStdOut(Action action)
    {
        TextWriter original = Console.Out;
        using StringWriter writer = new();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    private static string CaptureStdErr(Action action)
    {
        TextWriter original = Console.Error;
        using StringWriter writer = new();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return writer.ToString();
    }

    /// <summary>A server running <see cref="McpCommand.ServeAsync"/> and a client, joined by two anonymous pipes.</summary>
    private sealed class Session : IDisposable
    {
        private readonly AnonymousPipeServerStream clientToServer = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream serverToClient = new(PipeDirection.Out);
        private readonly AnonymousPipeClientStream serverInput;
        private readonly AnonymousPipeClientStream clientInput;
        private Task<int> server = Task.FromResult(-1);

        private Session()
        {
            serverInput = new AnonymousPipeClientStream(PipeDirection.In, clientToServer.ClientSafePipeHandle);
            clientInput = new AnonymousPipeClientStream(PipeDirection.In, serverToClient.ClientSafePipeHandle);
        }

        public McpClient Client { get; private set; } = null!;

        public static async Task<Session> StartAsync(ILanguageFrontend frontend, IVerificationBackend backend, ExecutionEnvironment? execution = null)
        {
            Session session = new();
            session.server = Task.Run(
                () => McpCommand.ServeAsync(session.serverInput, session.serverToClient, new EquivTools([frontend], backend, execution), TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            session.Client = await McpClient.CreateAsync(new StreamClientTransport(session.clientToServer, session.clientInput), cancellationToken: timeout.Token).ConfigureAwait(false);
            return session;
        }

        public async Task<CallToolResult> CallAsync(string tool, Dictionary<string, object?> arguments)
        {
            return await Client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>Closes the client and its end of the pipe, which ends the server's input, and returns the server's exit code.</summary>
        public async Task<int> CloseAsync()
        {
            await Client.DisposeAsync().ConfigureAwait(false);
            await clientToServer.DisposeAsync().ConfigureAwait(false);
            return await server.ConfigureAwait(false);
        }

        public void Dispose()
        {
            CloseAsync().GetAwaiter().GetResult();
            serverInput.Dispose();
            clientInput.Dispose();
            clientToServer.Dispose();
            serverToClient.Dispose();
        }
    }
}
