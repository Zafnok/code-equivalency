namespace RuntimeChangesBackfill;

internal static class Program
{
    internal static int Main(string[] args) => Backfill.Run(args, Console.Out);
}
