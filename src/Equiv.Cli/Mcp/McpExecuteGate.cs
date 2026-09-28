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
    public static async Task<int> RunAsync(
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
            return await McpCommand.ServeAsync(input, output, new EquivTools(frontends, backend), cancellationToken).ConfigureAwait(false);
        }

        ExecutionEnvironment executing = execution ?? ExecutionEnvironment.Current;
        if (!executing.IsWindows)
        {
            await Console.Error.WriteLineAsync(ExecutionEnvironment.NeedsWindows).ConfigureAwait(false);
            return ExitCodes.UsageError;
        }

        await Console.Error.WriteLineAsync(ExecutionEnvironment.Note).ConfigureAwait(false);
        return await McpCommand.ServeAsync(input, output, new EquivTools(frontends, backend, executing), cancellationToken).ConfigureAwait(false);
    }
}
