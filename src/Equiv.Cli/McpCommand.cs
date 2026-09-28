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

    /// <summary>The <c>mcp</c> command over the process's own stdin and stdout.</summary>
    public static Command Create(IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend) =>
        Create(frontends, backend, ProcessStreams);

    /// <summary>The <c>mcp</c> command over <paramref name="streams"/>: the stream the server reads, then the one it writes.</summary>
    internal static Command Create(IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend, Func<(Stream Input, Stream Output)> streams)
    {
        ArgumentNullException.ThrowIfNull(frontends);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(streams);

        Command command = new("mcp", "Runs an MCP server over stdio that serves the compare pipeline to a coding agent.");
        command.SetAction((_, cancellationToken) =>
        {
            (Stream input, Stream output) = streams();
            return ServeAsync(input, output, new EquivTools(frontends, backend), cancellationToken);
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
}
