namespace LicenceCheck;

/// <summary>Pure: no I/O, no environment access.</summary>
internal static class ArgsParser
{
    public static CliArgs Parse(string[] args)
    {
        string repoRoot = ".";
        bool fix = false;
        int i = 0;
        while (i < args.Length)
        {
            if (string.Equals(args[i], "--repo-root", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                repoRoot = args[i + 1];
                i += 2;
            }
            else if (string.Equals(args[i], "--fix", StringComparison.Ordinal))
            {
                fix = true;
                i += 1;
            }
            else
            {
                i += 1;
            }
        }

        return new CliArgs(repoRoot, fix);
    }
}
