using Equiv.Core;

namespace Equiv.Cli.Mcp;

/// <summary>
/// ADR 0035's consequences for the <c>probe</c> tool, at <c>equiv mcp</c>'s server startup rather than per call (ticket
/// M5-002): without <c>--execute</c> nothing changes and <c>probe</c> is not registered (criterion 1); with it, the server
/// prints the same note on stderr <c>compare --execute</c> gives and registers <c>probe</c> too. Whether this OS can run a
/// pair depends on its solutions' runtimes, so <c>probe</c> asks <see cref="ExecutionEnvironment.Refusal"/> per call, as
/// <c>compare --execute</c> does once it has loaded them (ADR 0040 decision 3; ticket P2-056).
/// </summary>
internal static class McpExecuteGate
{
    public static Task<int> RunAsync(
        Stream input,
        Stream output,
        IReadOnlyList<ILanguageFrontend> frontends,
        IVerificationBackend backend,
        bool execute,
        ExecutionEnvironment? execution,
        CancellationToken cancellationToken) =>
        McpCommand.ServeAsync(input, output, Tools(frontends, backend, execute, execution), cancellationToken);

    /// <summary>The tools to serve: <c>probe</c> among them only under <c>--execute</c>.</summary>
    private static EquivTools Tools(IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend, bool execute, ExecutionEnvironment? execution)
    {
        if (!execute)
        {
            return new EquivTools(frontends, backend);
        }

        Console.Error.WriteLine(ExecutionEnvironment.Note);
        return new EquivTools(frontends, backend, execution ?? ExecutionEnvironment.Current);
    }
}
