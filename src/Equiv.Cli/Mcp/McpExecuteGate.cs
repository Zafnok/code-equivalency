using Equiv.Core;

namespace Equiv.Cli.Mcp;

/// <summary>
/// ADR 0035's consequences for the <c>probe</c> tool, at <c>equiv mcp</c>'s server startup rather than per call (ticket
/// M5-002): without <c>--execute</c> nothing changes and <c>probe</c> is not registered (criterion 1); with it, a
/// non-Windows OS stops the server before it serves anything, with the same message <c>compare --execute</c> gives, and
/// Windows prints the same note on stderr and registers <c>probe</c> too.
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
        Tools(frontends, backend, execute, execution) is { } tools
            ? McpCommand.ServeAsync(input, output, tools, cancellationToken)
            : Task.FromResult(ExitCodes.UsageError);

    /// <summary>The tools to serve, or null when <c>--execute</c> was asked for off Windows.</summary>
    private static EquivTools? Tools(IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend, bool execute, ExecutionEnvironment? execution)
    {
        if (!execute)
        {
            return new EquivTools(frontends, backend);
        }

        ExecutionEnvironment executing = execution ?? ExecutionEnvironment.Current;
        if (!executing.IsWindows)
        {
            Console.Error.WriteLine(ExecutionEnvironment.NeedsWindows);
            return null;
        }

        Console.Error.WriteLine(ExecutionEnvironment.Note);
        return new EquivTools(frontends, backend, executing);
    }
}
