using System.Collections.Concurrent;
using System.Diagnostics;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

namespace FailureRefinementSpike;

/// <summary>
/// Criterion 2: every <c>timeout</c> Unknown of a run, built again as P1-019's spike builds a pair (both solutions through
/// the production frontend, default config), verified once at this commit (the baseline) and then asked ADR 0037's two
/// queries, which the backend skips on a timeout.
/// </summary>
internal static class TimeoutQueries
{
    private static readonly string[] Outcomes = ["none-proved", "found", "unknown", Measured.Timeout];

    public static void Run(string sarifPath, string legacy, string modern, int threads)
    {
        List<string> timeouts = [.. Tabulation.Read(sarifPath).Where(static u => u.Reason == "timeout").Select(static u => u.Identity)];
        EquivConfig config = EquivConfig.Default;
        Stopwatch clock = Stopwatch.StartNew();
        FrontendAnalysis analysis = new CSharpFrontend().Analyze(legacy, modern, config, NullRunLog.Instance, CancellationToken.None);
        Console.Error.WriteLine($"loaded and lowered {analysis.Match.Pairs.Length} pairs in {clock.Elapsed.TotalSeconds:F0} s");
        Dictionary<string, ProcedurePair> pairs = new(StringComparer.Ordinal);
        foreach (ProcedurePair pair in analysis.Match.Pairs)
        {
            pairs[pair.New.Value] = pair;
        }

        // What `compare` gives the backend at its defaults (CompareCommand.Verification), without a log.
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames) { ResourceLimit = config.ResourceLimit };
        Z3Backend backend = new();
        ConcurrentBag<Row> rows = [];
        int done = 0;
        clock.Restart();
        Parallel.ForEach(timeouts, new ParallelOptions { MaxDegreeOfParallelism = threads }, identity =>
        {
            Row row = Measure(identity, pairs.GetValueOrDefault(identity), backend, options);
            rows.Add(row);

            // One line per pair as it finishes, so a run that is stopped keeps what it measured.
            Console.Error.WriteLine(Program.Invariant(
                $"{Interlocked.Increment(ref done)}/{timeouts.Count}\t{identity}\tbaseline={row.Baseline} ({row.BaselineSeconds:F1}s)\tnew={row.Queries?.NewFailures.Outcome} ({row.Queries?.NewFailures.SolverSeconds:F1}s)\tremoved={row.Queries?.RemovedFailures.Outcome} ({row.Queries?.RemovedFailures.SolverSeconds:F1}s)\telapsed={row.Queries?.ElapsedSeconds:F1}s"));
        });

        Print([.. rows], options, clock.Elapsed.TotalSeconds, threads);
    }

    private static Row Measure(string identity, ProcedurePair? pair, Z3Backend backend, VerificationOptions options)
    {
        if (pair is not { OldBody: { } old, NewBody: { } @new })
        {
            return new Row(identity, "pair not found at this commit", 0, null);
        }

        try
        {
            Stopwatch clock = Stopwatch.StartNew();
            Verdict baseline = backend.Verify(old, @new, options);
            double baselineSeconds = clock.Elapsed.TotalSeconds;

            // As Z3Backend.Verify decides it: a pair rung 1 could not encode has no product to ask on.
            bool encodable = baseline.Ladder is not [{ Outcome: RungOutcome.NotApplicable }, ..];
            return new Row(identity, Name(baseline), baselineSeconds, Measured.Run(old, @new, options, encodable));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new Row(identity, $"crashed ({exception.GetType().Name})", 0, null);
        }
    }

    private static string Name(Verdict verdict) => verdict switch
    {
        Unknown { Reason: UnknownReason.Timeout } => Measured.Timeout,
        Unknown unknown => $"Unknown ({unknown.Reason})",
        _ => verdict.GetType().Name,
    };

    private static void Print(List<Row> rows, VerificationOptions options, double wallSeconds, int threads)
    {
        List<Row> queried = [.. rows.Where(static r => r.Queries is not null)];
        Console.WriteLine($"timeout Unknowns in the run: {rows.Count}; queried: {queried.Count}; resourceLimit {options.ResourceLimit}, timeoutMs {options.TimeoutMs}, bound {options.Bound}; {threads} threads, wall-clock {wallSeconds:F0} s");

        Console.WriteLine();
        Console.WriteLine("## Baseline at this commit (the full query, as `compare` asks it)");
        Console.WriteLine("| baseline | pairs |");
        Console.WriteLine("|---|---|");
        foreach (IGrouping<string, Row> group in rows.GroupBy(static r => r.Baseline).OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"| {group.Key} | {group.Count()} |");
        }

        Console.WriteLine();
        Console.WriteLine("## ADR 0037's queries on the timeout Unknowns");
        Console.WriteLine($"| query | pairs | {string.Join(" | ", Outcomes)} | solver seconds: sum / median / p90 / max |");
        Console.WriteLine($"|---|---|{string.Concat(Enumerable.Repeat("---|", Outcomes.Length))}---|");
        QueryRow("newFailures", queried, static r => r.Queries!.NewFailures);
        QueryRow("removedFailures", queried, static r => r.Queries!.RemovedFailures);

        Console.WriteLine();
        Console.WriteLine("## The same, by the baseline at this commit");
        Console.WriteLine($"| baseline | query | pairs | {string.Join(" | ", Outcomes)} |");
        Console.WriteLine($"|---|---|---|{string.Concat(Enumerable.Repeat("---|", Outcomes.Length))}");
        foreach (IGrouping<string, Row> group in queried.GroupBy(static r => r.Baseline).OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"| {group.Key} | newFailures | {group.Count()} | {Cells([.. group], static r => r.Queries!.NewFailures)} |");
            Console.WriteLine($"| {group.Key} | removedFailures | {group.Count()} | {Cells([.. group], static r => r.Queries!.RemovedFailures)} |");
        }

        Console.WriteLine();
        Console.WriteLine("## newFailures against removedFailures (pairs)");
        Console.WriteLine($"| newFailures \\ removedFailures | {string.Join(" | ", Outcomes)} |");
        Console.WriteLine($"|---|{string.Concat(Enumerable.Repeat("---|", Outcomes.Length))}");
        foreach (string outcome in Outcomes)
        {
            Console.WriteLine($"| {outcome} | {string.Join(" | ", Outcomes.Select(o => queried.Count(r => r.Queries!.NewFailures.Outcome == outcome && r.Queries.RemovedFailures.Outcome == o)))} |");
        }

        double baselineSeconds = rows.Sum(static r => r.BaselineSeconds);
        double addedSeconds = queried.Sum(static r => r.Queries!.ElapsedSeconds);
        double checkSeconds = queried.Sum(static r => r.Queries!.NewFailures.SolverSeconds + r.Queries.RemovedFailures.SolverSeconds);
        int noneProved = queried.Count(static r => r.Queries!.NewFailures.Outcome == "none-proved");
        int both = queried.Count(static r => r.Queries!.NewFailures.Outcome == "none-proved" && r.Queries.RemovedFailures.Outcome == "none-proved");
        Console.WriteLine();
        Console.WriteLine(Program.Invariant($"baseline pair seconds (sum) {baselineSeconds:F0}; added by the two queries (sum of pair seconds) {addedSeconds:F0}, of which solver checks {checkSeconds:F0}; added / baseline {addedSeconds / Math.Max(baselineSeconds, 1):F2}"));
        Console.WriteLine($"timeout Unknowns that would have a none-proved newFailures: {noneProved} of {rows.Count} ({Program.Share(noneProved, rows.Count)}); with both none-proved: {both} ({Program.Share(both, rows.Count)}) (ADR 0028's bar: 5%)");

        Console.WriteLine();
        Console.WriteLine("## Pairs with a none-proved or found answer (procedure identity, baseline, newFailures, removedFailures)");
        foreach (Row row in queried.Where(static r => new[] { r.Queries!.NewFailures.Outcome, r.Queries.RemovedFailures.Outcome }.Any(static o => o is "none-proved" or "found")).OrderBy(static r => r.Identity, StringComparer.Ordinal))
        {
            Console.WriteLine($"| `{row.Identity}` | {row.Baseline} | {row.Queries!.NewFailures.Outcome} | {row.Queries.RemovedFailures.Outcome} |");
        }
    }

    private static void QueryRow(string name, List<Row> rows, Func<Row, QueryMeasure> query)
    {
        double[] seconds = [.. rows.Select(r => query(r).SolverSeconds).Order()];
        string times = seconds.Length == 0
            ? "n/a"
            : Program.Invariant($"{seconds.Sum():F0} / {Quantile(seconds, 0.5):F1} / {Quantile(seconds, 0.9):F1} / {seconds[^1]:F1}");
        Console.WriteLine($"| {name} | {rows.Count} | {Cells(rows, query)} | {times} |");
    }

    private static string Cells(List<Row> rows, Func<Row, QueryMeasure> query) =>
        string.Join(" | ", Outcomes.Select(o => rows.Count(r => query(r).Outcome == o)));

    private static double Quantile(double[] sorted, double q) => sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(q * sorted.Length))];

    /// <summary>One <c>timeout</c> Unknown: the full query's outcome at this commit, and ADR 0037's queries where it was built.</summary>
    private sealed record Row(string Identity, string Baseline, double BaselineSeconds, PairMeasure? Queries);
}
