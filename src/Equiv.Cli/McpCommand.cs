using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Equiv.Core;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Equiv.Cli;

/// <summary>
/// <c>equiv mcp</c>: an MCP server over stdio in the same binary, serving <see cref="EquivTools"/> (ADR 0033; ticket M5-001).
/// stdout carries protocol messages and nothing else; <see cref="CompareCommand"/>'s own stdout lines go to stderr here.
/// </summary>
internal static class McpCommand
{
    /// <summary>The server name an agent sees.</summary>
    internal const string ServerName = "equiv";

    /// <summary>The <c>mcp</c> command over the process's own stdin and stdout; <paramref name="execution"/> is where <c>--execute</c>
    /// runs the <c>probe</c> tool's code, this machine when null.</summary>
    public static Command Create(IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend, ExecutionEnvironment? execution = null) =>
        Create(frontends, backend, execution, ProcessStreams);

    /// <summary>The <c>mcp</c> command over <paramref name="streams"/>: the stream the server reads, then the one it writes.</summary>
    internal static Command Create(
        IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend, ExecutionEnvironment? execution, Func<(Stream Input, Stream Output)> streams)
    {
        ArgumentNullException.ThrowIfNull(frontends);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(streams);

        Option<bool> executeOption = new("--execute");
        Command command = new("mcp", "Runs an MCP server over stdio that serves the compare pipeline to a coding agent.") { executeOption };
        command.SetAction((parseResult, cancellationToken) =>
        {
            (Stream input, Stream output) = streams();
            return RunAsync(input, output, frontends, backend, parseResult.GetValue(executeOption), execution, cancellationToken);
        });
        return command;
    }

    /// <summary>Serves <paramref name="tools"/> until <paramref name="input"/> closes, then exits 0.</summary>
    internal static async Task<int> ServeAsync(Stream input, Stream output, EquivTools tools, CancellationToken cancellationToken)
    {
        McpServerOptions options = new()
        {
            ServerInfo = new Implementation { Name = ServerName, Version = Version },
            ToolCollection = [.. tools.Create()],
        };
        StreamServerTransport transport = new(input, output, ServerName);
        McpServer server = McpServer.Create(transport, options);
        try
        {
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
            return ExitCodes.Success;
        }
        finally
        {
            await server.DisposeAsync().ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The version MinVer stamped on this assembly.</summary>
    internal static string Version { get; } = typeof(McpCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    [ExcludeFromCodeCoverage(Justification = "M5-001: the process's own stdin and stdout, exercised by Equiv.Tests.Integration's McpIntegrationTests")]
    private static (Stream Input, Stream Output) ProcessStreams() => (Console.OpenStandardInput(), Console.OpenStandardOutput());

    /// <summary>
    /// ADR 0035's consequences for the <c>probe</c> tool, at server startup rather than per call: without <c>--execute</c>
    /// nothing changes and <c>probe</c> is not registered (ticket M5-002 criterion 1); with it, a non-Windows OS stops the
    /// server before it serves anything, with the same message <c>compare --execute</c> gives, and Windows prints the same
    /// note on stderr and registers <c>probe</c> too.
    /// </summary>
    private static async Task<int> RunAsync(
        Stream input,
        Stream output,
        IReadOnlyList<ILanguageFrontend> frontends,
        IVerificationBackend backend,
        bool execute,
        ExecutionEnvironment? execution,
        CancellationToken cancellationToken)
    {
        if (!execute)
        {
            return await ServeAsync(input, output, new EquivTools(frontends, backend), cancellationToken).ConfigureAwait(false);
        }

        ExecutionEnvironment executing = execution ?? ExecutionEnvironment.Current;
        if (!executing.IsWindows)
        {
            await Console.Error.WriteLineAsync(ExecutionEnvironment.NeedsWindows).ConfigureAwait(false);
            return ExitCodes.UsageError;
        }

        await Console.Error.WriteLineAsync(ExecutionEnvironment.Note).ConfigureAwait(false);
        return await ServeAsync(input, output, new EquivTools(frontends, backend, executing), cancellationToken).ConfigureAwait(false);
    }
}
