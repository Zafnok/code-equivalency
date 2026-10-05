using SolverPortfolioSpike;

namespace Cvc5PartitioningSpike;

/// <summary>Prints the tables <c>docs/runs/&lt;date&gt;-cvc5-partitioning.md</c> quotes.</summary>
internal static class Report
{
    public static void Print(List<Known> known, List<Outcome> outcomes, List<ReadBackRow> readBacks, int timeoutMs)
    {
        Dictionary<string, ReadBackRow> readBack = readBacks.ToDictionary(static r => r.Key, StringComparer.Ordinal);
        Dictionary<int, Known> byIndex = known.ToDictionary(static k => k.Index);
        List<Outcome> hard = [.. outcomes.Where(static o => o.Set == Program.Timeouts)];
        int queries = hard.Select(static o => o.Index).Distinct().Count();

        Console.WriteLine($"## The {queries} files cvc5 timed out on, each partition given {timeoutMs} ms (criterion 2)");
        foreach (int run in hard.Select(static o => o.Run).Distinct().Order())
        {
            Console.WriteLine();
            Console.WriteLine($"Run {run}.");
            Console.WriteLine();
            Console.WriteLine("| partitions asked | strategy | queries split | partitions made, median | unsatisfiable | satisfiable, replayed | satisfiable, not replayed | still undecided | not split | error | wall-clock s | process s |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var config in hard.Where(o => o.Run == run).GroupBy(static o => (o.Count, o.Strategy)).OrderBy(static g => g.Key.Count).ThenBy(static g => Array.IndexOf(PartitionedSolve.Strategies, g.Key.Strategy)))
            {
                List<Outcome> rows = [.. config];
                List<int> made = [.. rows.Where(static o => o.Partitions > 0).Select(static o => o.Partitions).Order()];
                int replayed = rows.Count(o => o.Status == PartitionedSolve.Sat && Replayed(readBack, o));
                Console.WriteLine(
                    $"| {config.Key.Count} | {config.Key.Strategy} | {made.Count} | {(made.Count == 0 ? "n/a" : made[made.Count / 2])} | {Count(rows, PartitionedSolve.Unsat)} | {replayed} | {Count(rows, PartitionedSolve.Sat) - replayed} | {Count(rows, PartitionedSolve.Undecided)} | {Count(rows, PartitionedSolve.NoPartitions)} | {Count(rows, PartitionedSolve.Error)} | {rows.Sum(static o => o.WallMs) / 1000} | {rows.Sum(static o => o.CpuMs) / 1000} |");
            }
        }

        Console.WriteLine();
        Console.WriteLine("## Partitions, over every run (criterion 2)");
        Console.WriteLine("| a partition's answer | partitions |");
        Console.WriteLine("|---|---|");
        foreach (var group in hard.SelectMany(static o => o.Partitions == 0 ? [] : o.Parts.Split(',').Select(static p => p.Split('='))).GroupBy(static p => p[0]).OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"| {group.Key} | {group.Sum(static p => int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture))} |");
        }

        Console.WriteLine();
        Console.WriteLine($"partition sets that cover every case: {hard.Count(static o => o.Cover == PartitionedSolve.Unsat)} of {hard.Count(static o => o.Partitions > 0)}");

        Console.WriteLine();
        Console.WriteLine("## The same run, twice (criterion 3)");
        Console.WriteLine("| partitions asked | strategy | queries | a different answer the second time |");
        Console.WriteLine("|---|---|---|---|");
        HashSet<int> differing = [];
        foreach (var config in hard.GroupBy(static o => (o.Count, o.Strategy)).OrderBy(static g => g.Key.Count).ThenBy(static g => Array.IndexOf(PartitionedSolve.Strategies, g.Key.Strategy)))
        {
            List<int> differs = [.. config.GroupBy(static o => o.Index).Where(static q => q.Select(static o => Answer(o)).Distinct().Count() > 1).Select(static q => q.Key)];
            differing.UnionWith(differs);
            Console.WriteLine($"| {config.Key.Count} | {config.Key.Strategy} | {config.Select(static o => o.Index).Distinct().Count()} | {differs.Count} |");
        }

        Console.WriteLine();
        Console.WriteLine($"queries with a different answer the second time, at any count and strategy: {differing.Count} of {queries}");

        // Decided by any count and strategy, in either run.
        List<IGrouping<int, Outcome>> decided = [.. hard.Where(static o => o.Decided).GroupBy(static o => o.Index).OrderBy(static g => g.Key)];
        int proofs = decided.Count(g => g.Any(o => o.Status == PartitionedSolve.Unsat && readBack.GetValueOrDefault(o.Key)?.Text == ReadBackRow.Proved));
        int divergent = decided.Count(g => g.Any(o => readBack.GetValueOrDefault(o.Key)?.Text == ReadBack.Divergent));
        Console.WriteLine();
        Console.WriteLine($"of the {queries}, partitioning decides {decided.Count}; proofs (the pair would be Equivalent) {proofs}; replay to a Divergent {divergent}");

        if (decided.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("## Decided queries");
            Console.WriteLine("| procedure identity | query | answer | by (run, partitions asked, strategy) | worth |");
            Console.WriteLine("|---|---|---|---|---|");
            foreach (IGrouping<int, Outcome> group in decided)
            {
                foreach (var answer in group.GroupBy(o => (o.Status, Worth: readBack.GetValueOrDefault(o.Key)?.Text ?? "not read back")))
                {
                    Console.WriteLine($"| `{byIndex[group.Key].Identity}` | `{byIndex[group.Key].Query}` | {answer.Key.Status} | {string.Join("; ", answer.Select(static o => $"{o.Run}, {o.Count}, {o.Strategy}"))} | {answer.Key.Worth} |");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"read back against the file P1-025 exported: {readBack.Values.Count(static r => r.SameFile)} of {readBack.Count} answers; the others against the pair as this commit encodes it");
        }

        List<Outcome> control = [.. outcomes.Where(static o => o.Set == Program.Answered)];
        if (control.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("## Control: the files cvc5 already answers (criterion 4)");
        Console.WriteLine("| run | partitions asked | strategy | files | same answer | the other answer | no longer answered | of those: not split | wall-clock s | process s | plain cvc5 s (P1-025) |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var config in control.GroupBy(static o => (o.Run, o.Count, o.Strategy)).OrderBy(static g => g.Key.Run))
        {
            List<Outcome> rows = [.. config];
            int same = rows.Count(o => o.Status == byIndex[o.Index].Plain);
            int other = rows.Count(o => o.Decided && o.Status != byIndex[o.Index].Plain);
            Console.WriteLine($"| {config.Key.Run} | {config.Key.Count} | {config.Key.Strategy} | {rows.Count} | {same} | {other} | {rows.Count - same - other} | {rows.Count(o => !o.Decided && o.Partitions == 0)} | {rows.Sum(static o => o.WallMs) / 1000} | {rows.Sum(static o => o.CpuMs) / 1000} | {rows.Sum(o => byIndex[o.Index].PlainMs) / 1000} |");
        }
    }

    /// <summary>The answer two runs are compared on: a query not split is as undecided as one whose partitions time out.</summary>
    private static string Answer(Outcome outcome) => outcome.Status == PartitionedSolve.NoPartitions ? PartitionedSolve.Undecided : outcome.Status;

    private static bool Replayed(Dictionary<string, ReadBackRow> readBack, Outcome outcome) =>
        readBack.GetValueOrDefault(outcome.Key)?.Text is ReadBack.Divergent or ReadBack.Abstraction;

    private static int Count(List<Outcome> rows, string status) => rows.Count(o => o.Status == status);
}
