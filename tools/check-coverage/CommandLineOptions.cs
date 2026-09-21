namespace CheckCoverage;

internal sealed record CommandLineOptions(string TestResultsDir, string SrcDir)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        string testResultsDir = "TestResults";
        string srcDir = "src";

        int i = 0;
        while (i < args.Count)
        {
            if (string.Equals(args[i], "--test-results", StringComparison.Ordinal) && i + 1 < args.Count)
            {
                testResultsDir = args[i + 1];
                i += 2;
            }
            else if (string.Equals(args[i], "--src", StringComparison.Ordinal) && i + 1 < args.Count)
            {
                srcDir = args[i + 1];
                i += 2;
            }
            else
            {
                i++;
            }
        }

        return new CommandLineOptions(testResultsDir, srcDir);
    }
}
