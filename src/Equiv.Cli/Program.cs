using System.CommandLine;
using System.CommandLine.Parsing;

namespace Equiv.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        // ADR 0012 (proposed): wiring CSharpFrontend here makes NoBackend's throw on a matched pair
        // reachable for the first time (samples/identical has two). Left unwired pending the ADR.
        Command compareCommand = CompareCommand.Create(frontends: [], backend: new NoBackend());
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

        return parseResult.Invoke();
    }
}
