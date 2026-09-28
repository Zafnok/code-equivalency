using System.Collections.Immutable;
using System.CommandLine;
using System.IO.Pipes;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.Configuration;
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
        Assert.Equal(
            ["legacy", "modern"],
            compare.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(
            ["baseline", "bound", "config", "legacy", "modern", "timeoutMs"],
            compare.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        McpClientTool lowerOnly = tools.Single(static t => string.Equals(t.Name, "lower_only", StringComparison.Ordinal));
        Assert.Equal(
            ["config", "legacy", "modern"],
            lowerOnly.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);
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
        Command command = McpCommand.Create([], new FakeBackend(NoVerdicts), () => (serverInput, serverToClient));

        Task<int> server = Task.Run(() => command.Parse(["mcp"]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        McpClient client = await McpClient.CreateAsync(new StreamClientTransport(clientToServer, clientInput), cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal("equiv", client.ServerInfo.Name);
        await client.DisposeAsync().ConfigureAwait(true);
        await clientToServer.DisposeAsync().ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, await server.ConfigureAwait(true));
    }

    [Fact]
    public void Create_RejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => McpCommand.Create(null!, new FakeBackend(NoVerdicts), () => (Stream.Null, Stream.Null)));
        Assert.Throws<ArgumentNullException>(() => McpCommand.Create([], null!, () => (Stream.Null, Stream.Null)));
        Assert.Throws<ArgumentNullException>(() => McpCommand.Create([], new FakeBackend(NoVerdicts), null!));
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
        Assert.Equal("Equivalent 2, Divergent 1, Unknown 1, skipped projects 0, exit code 1", Text(result.Content[0]));
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

        Assert.Equal("Equivalent 0, Divergent 1, Unknown 0, skipped projects 0, exit code 0", Text(second.Content[0]));
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
        Assert.Equal("Equivalent 1, Divergent 0, Unknown 0, skipped projects 1, exit code 4", Text(result.Content[0]));
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
        Assert.Equal("error: bound and timeoutMs must be positive integers", Assert.Single(result.Content.Select(Text)));
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
    private static Dictionary<string, object?> Args(string legacy, string modern, string? config = null, string? baseline = null, int? bound = null, int? timeoutMs = null)
    {
        Dictionary<string, object?> arguments = new(StringComparer.Ordinal) { ["legacy"] = legacy, ["modern"] = modern };
        AddIfPresent(arguments, "config", config);
        AddIfPresent(arguments, "baseline", baseline);
        AddIfPresent(arguments, "bound", bound);
        AddIfPresent(arguments, "timeoutMs", timeoutMs);
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

        public static async Task<Session> StartAsync(ILanguageFrontend frontend, IVerificationBackend backend)
        {
            Session session = new();
            session.server = Task.Run(() => McpCommand.ServeAsync(session.serverInput, session.serverToClient, new EquivTools([frontend], backend), TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
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
