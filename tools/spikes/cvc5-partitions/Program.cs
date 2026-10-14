using System.Diagnostics;
using System.Globalization;

using Equiv.Core;
using Equiv.Verify.Cvc5;

namespace Cvc5PartitionsMeasure;

/// <summary>
/// P1-039 criterion 5: the 34 rung 1 queries cvc5 timed out on in P1-036, each split by the product's
/// <see cref="Cvc5Solver"/> (<see cref="Cvc5Solver.Split"/>) under its resource limit and the wall-clock limit given here.
/// The plain query is not asked again: P1-036 measured that cvc5 gives up on each, and the product splits only a query it
/// gave up on (ticket P1-039 criterion 3's tests). As many queries run side by side as the solver's processes allow at
/// the count asked, and every process of every query takes its place from those processes, as in a run. Each query is
/// one line of the output file, with its answer and how every partition ended. <c>--report</c> prints the tables the
/// run file quotes.
/// </summary>
internal static class Program
{
    private const string Header = "run\tcount\tindex\tanswer\treason\twritten\tunsat\tsat\tresourceLimit\twallClock\tstopped\tother\tcover\tms";

    public static int Main(string[] args)
    {
        switch (args)
        {
            case ["--run", string cvc5, string spike, string smt, string output, string count, string run, string timeoutMs, string processes]:
                return Run(cvc5, spike, smt, output, Int(count), Int(run), Int(timeoutMs), Int(processes));
            case ["--report", string output]:
                Report(output);
                return 0;
            default:
                Console.Error.WriteLine("usage: --run <cvc5> <p1036 partitioned.tsv> <smtDir> <out.tsv> <partitions> <run> <timeoutMs> <processes> | --report <out.tsv>");
                return 2;
        }
    }

    private static int Run(string cvc5, string spike, string smt, string output, int count, int run, int timeoutMs, int processes)
    {
        List<int> queries = Timeouts(spike);
        HashSet<string> done = File.Exists(output) ? [.. File.ReadLines(output).Skip(1).Select(static l => l.Split('\t')).Select(static c => $"{c[0]}/{c[1]}/{c[2]}")] : [];
        if (!File.Exists(output))
        {
            File.WriteAllText(output, Header + "\n");
        }

        Cvc5Solver solver = new(cvc5) { Processes = processes };
        TimeSpan limit = TimeSpan.FromMilliseconds(timeoutMs);
        int sideBySide = Math.Max(1, processes / count);
        Console.Error.WriteLine($"run {run}, {count} partitions: {queries.Count} queries, cvc5 {solver.Version}, rlimit {solver.ResourceLimit}, tlimit {timeoutMs} ms, {processes} processes, {sideBySide} queries at once");
        object gate = new();
        Parallel.ForEach(
            queries.Where(i => !done.Contains($"{run}/{count}/{i}")),
            new ParallelOptions { MaxDegreeOfParallelism = sideBySide },
            index =>
            {
                string script = File.ReadAllText(Path.Combine(smt, index.ToString("000", CultureInfo.InvariantCulture) + ".u.smt2"));
                Stopwatch clock = Stopwatch.StartNew();
                Cvc5Solver.Partitioned split = solver.Split(script, count, limit);
                long ms = clock.ElapsedMilliseconds;
                string[] endings = [.. split.Answers.Select(Ending)];
                string line = string.Join(
                    '\t',
                    run,
                    count,
                    index,
                    Ending(split.Answer),
                    split.Answer is SmtUnknown unknown ? unknown.Reason : "partitions",
                    split.Partitions.Length,
                    endings.Count(static e => e == "unsat"),
                    endings.Count(static e => e == "sat"),
                    endings.Count(static e => e == "resource-limit"),
                    endings.Count(static e => e == "wall-clock"),
                    endings.Count(static e => e == "stopped"),
                    endings.Count(static e => e == "other"),
                    split.Cover is null ? "n/a" : Ending(split.Cover),
                    ms);
                lock (gate)
                {
                    File.AppendAllText(output, line + "\n");
                    Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
                }
            });
        return 0;
    }

    /// <summary>
    /// P1-036's 34 queries: the indices its <c>partitioned.tsv</c> lists for the set <c>timeouts</c>. It took them from
    /// P1-025's <c>results.tsv</c>, which was written again afterwards.
    /// </summary>
    private static List<int> Timeouts(string spike) =>
        [.. File.ReadLines(spike).Select(static l => l.Split('\t')).Where(static c => c[0] == "timeouts").Select(static c => Int(c[4])).Distinct().Order()];

    /// <summary>How one process ended: its answer, or what ended it.</summary>
    private static string Ending(SmtAnswer answer) => answer switch
    {
        SmtSat => "sat",
        SmtUnsat => "unsat",
        SmtUnknown { Reason: "unknown" } => "resource-limit",
        SmtUnknown { Reason: "cvc5 interrupted by timeout." or "wall-clock limit: the process was killed" } => "wall-clock",
        SmtUnknown { Reason: var reason } when reason.StartsWith("stopped", StringComparison.Ordinal) => "stopped",
        _ => "other",
    };

    private static void Report(string output)
    {
        List<string[]> rows = [.. File.ReadLines(output).Skip(1).Select(static l => l.Split('\t'))];
        Console.WriteLine("| run | partitions asked | queries | split | not split | sat | unsat | unknown | partitions made | unsat | sat | resource limit | wall-clock | stopped | other | cover unsat | query s, summed |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (IGrouping<(string Run, string Partitions), string[]> group in rows.GroupBy(static r => (Run: r[0], Partitions: r[1])).OrderBy(static g => Int(g.Key.Partitions)).ThenBy(static g => g.Key.Run, StringComparer.Ordinal))
        {
            Console.WriteLine(string.Join(
                " | ",
                $"| {group.Key.Run}",
                group.Key.Partitions,
                group.Count(),
                group.Count(static r => Int(r[5]) > 0),
                group.Count(static r => Int(r[5]) == 0),
                group.Count(static r => r[3] == "sat"),
                group.Count(static r => r[3] == "unsat"),
                group.Count(static r => r[3] is not ("sat" or "unsat")),
                group.Sum(static r => Int(r[5])),
                group.Sum(static r => Int(r[6])),
                group.Sum(static r => Int(r[7])),
                group.Sum(static r => Int(r[8])),
                group.Sum(static r => Int(r[9])),
                group.Sum(static r => Int(r[10])),
                group.Sum(static r => Int(r[11])),
                group.Count(static r => r[12] == "unsat"),
                (group.Sum(static r => long.Parse(r[13], CultureInfo.InvariantCulture)) / 1000).ToString(CultureInfo.InvariantCulture) + " |"));
        }

        Console.WriteLine();
        Console.WriteLine("| partitions asked | queries in both runs | a different answer the second time | a different count of partitions or of endings |");
        Console.WriteLine("|---|---|---|---|");
        foreach (IGrouping<string, string[]> count in rows.GroupBy(static r => r[1]).OrderBy(static g => Int(g.Key)))
        {
            Dictionary<string, string[]> first = count.Where(static r => r[0] == "1").ToDictionary(static r => r[2], StringComparer.Ordinal);
            List<(string[] One, string[] Two)> both = [.. count.Where(r => r[0] == "2" && first.ContainsKey(r[2])).Select(r => (first[r[2]], r))];
            Console.WriteLine($"| {count.Key} | {both.Count} | {both.Count(static p => p.One[3] != p.Two[3])} | {both.Count(static p => !Same(p.One, p.Two))} |");
            foreach ((string[] one, string[] two) in both.Where(static p => p.One[3] != p.Two[3] || !Same(p.One, p.Two)))
            {
                Console.WriteLine($"  query {one[2]}: run 1 {string.Join(',', one[3..13])}; run 2 {string.Join(',', two[3..13])}");
            }
        }
    }

    /// <summary>Whether two runs of a query made the same partitions and they ended the same way.</summary>
    private static bool Same(string[] one, string[] two) => one.AsSpan(5, 8).SequenceEqual(two.AsSpan(5, 8));

    private static int Int(string text) => int.Parse(text, CultureInfo.InvariantCulture);
}
