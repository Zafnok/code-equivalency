using System.Globalization;

namespace FailureRefinementSpike;

/// <summary>
/// P1-021: what ADR 0037's two queries answer on a full run's Unknowns today (<see cref="Tabulation"/>), and what they
/// would answer on its <c>timeout</c> Unknowns, which the backend does not ask (<see cref="TimeoutQueries"/>). Prints
/// identities, reasons and counts; never a model value or source text.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        switch (args)
        {
            case ["--self-test"]:
                return SelfTest.Run();
            case ["tabulate", _, ..]:
                Tabulation.Print(args[1..]);
                return 0;
            case ["timeouts", _, _, _, ..] when args.Length <= 5:
                TimeoutQueries.Run(args[1], args[2], args[3], args.Length == 5 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 4);
                return 0;
            default:
                Console.Error.WriteLine("usage: failure-refinement-spike --self-test | tabulate <equiv.sarif>... | timeouts <equiv.sarif> <legacy.sln> <modern.sln> [<threads>]");
                return 2;
        }
    }

    public static string Share(int count, int total) => Invariant($"{100.0 * count / Math.Max(total, 1):F1}%");

    public static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
