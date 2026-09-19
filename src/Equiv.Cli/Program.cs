using System.CommandLine;
using System.CommandLine.Parsing;

using Equiv.Frontend.CSharp;

namespace Equiv.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        // ADR 0012: no verification backend exists before M3-001 wires Equiv.Verify.Z3; matched
        // pairs get no result until then (CompareCommand.BuildResults skips them when null).
        Command compareCommand = CompareCommand.Create(frontends: [new CSharpFrontend()], backend: null);
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
