using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

using Equiv.Core;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

namespace Equiv.Cli;

internal static class Program
{
    public static int Main(string[] args) => Run(args, [new CSharpFrontend()], new Z3Backend());

    /// <summary>Seam for unit tests: <see cref="Main"/>'s pipeline over injected collaborators.</summary>
    internal static int Run(string[] args, IReadOnlyList<ILanguageFrontend> frontends, IVerificationBackend backend)
    {
        Command compareCommand = CompareCommand.Create(frontends, backend);
        RootCommand root = new("Compares two versions of a codebase for behavioural equivalence.") { compareCommand };

        ParseResult parseResult = root.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (ParseError error in parseResult.Errors)
            {
                Console.Error.WriteLine(error.Message);
            }

            return ExitCodes.UsageError;
        }

        // ADR 0023: a crash is an internal error (exit 5), not System.CommandLine's own exit 1 for an
        // unhandled exception. EnableDefaultExceptionHandler = false lets it propagate here instead.
        try
        {
            return parseResult.Invoke(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitCodes.InternalError;
        }
    }
}
