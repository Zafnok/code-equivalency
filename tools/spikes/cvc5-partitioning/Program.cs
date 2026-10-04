using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.Z3;

using SolverPortfolioSpike;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Cvc5PartitioningSpike;

/// <summary>
/// P1-036: does cvc5 decide more of the hard queries when it partitions them? Takes the files the P1-025 spike exported
/// and its <c>results.tsv</c>. <c>--solve</c> partitions each file of a set and solves the partitions; <c>--read-back</c>
/// replays the satisfiable answers and asks Z3 the rest of rung 1 for the unsatisfiable ones; <c>--report</c> prints the
/// tables. Prints identities, statuses and counts; never a model value or source text. The output folder holds values
/// from the code, so it belongs under <c>.corpus/</c>.
/// </summary>
internal static class Program
{
    public const string Timeouts = "timeouts";
    public const string Answered = "answered";
    public const string Results = "partitioned.tsv";
    public const string ReadBacks = "readback.tsv";

    public static int Main(string[] args)
    {
        switch (args)
        {
            case ["--self-test", string cvc5]:
                return SelfTest.Run(cvc5);
            case ["--solve", string cvc5, string smt, string known, string outDir, .. string[] rest] when rest.Length % 2 == 0:
                return Solve(cvc5, smt, known, outDir, rest);
            case ["--read-back", string smt, string known, string outDir, string legacy, string modern]:
                return ReadBackAll(smt, known, outDir, legacy, modern);
            case ["--report", string known, string outDir]:
                Report.Print(Known.Read(known), Outcome.Read(Path.Combine(outDir, Results)), ReadBackRow.Read(Path.Combine(outDir, ReadBacks)), EquivConfig.Default.TimeoutMs);
                return 0;
            default:
                Console.Error.WriteLine("usage: cvc5-partitioning-spike --self-test <cvc5> | --solve <cvc5> <smtDir> <p1025 results.tsv> <outDir> [--set timeouts|answered] [--counts 8,24] [--strategies name,...] [--runs 1,2] [--slots 24] [--only index,...] | --read-back <smtDir> <p1025 results.tsv> <outDir> <legacy.sln> <modern.sln> | --report <p1025 results.tsv> <outDir>");
                return 2;
        }
    }

    private static int Solve(string cvc5, string smt, string knownPath, string outDir, string[] rest)
    {
        string set = Timeouts;
        int[] counts = [8, 24];
        string[] strategies = PartitionedSolve.Strategies;
        int[] runs = [1, 2];
        int slotCount = 24;
        HashSet<int>? only = null;
        for (int i = 0; i < rest.Length; i += 2)
        {
            switch (rest[i])
            {
                case "--set": set = rest[i + 1]; break;
                case "--counts": counts = Numbers(rest[i + 1]); break;
                case "--strategies": strategies = rest[i + 1].Split(','); break;
                case "--runs": runs = Numbers(rest[i + 1]); break;
                case "--slots": slotCount = int.Parse(rest[i + 1], CultureInfo.InvariantCulture); break;
                case "--only": only = [.. Numbers(rest[i + 1])]; break;
                default: throw new ArgumentException(rest[i]);
            }
        }

        // The 34 files cvc5 timed out on, or the control: the 59 it answers.
        List<Known> queries = [.. Known.Read(knownPath).Where(k => (set == Timeouts ? k.Plain == Cvc5.Timeout : k.Plain is "sat" or "unsat") && only?.Contains(k.Index) != false)];
        string work = Path.Combine(outDir, "work");
        string values = Path.Combine(outDir, "values");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(values);
        string results = Path.Combine(outDir, Results);
        HashSet<string> done = [.. Outcome.Read(results).Select(static o => o.Key)];
        List<(int Run, int Count, string Strategy, Known Query)> items =
        [
            .. from run in runs
               from count in counts
               from strategy in strategies
               from query in queries
               where !done.Contains(Outcome.KeyOf(set, run, count, strategy, query.Index))
               select (run, count, strategy, query),
        ];
        Console.Error.WriteLine($"{set}: {queries.Count} queries, {items.Count} to solve, {done.Count} already in {Results}");

        int timeoutMs = EquivConfig.Default.TimeoutMs;
        PartitionedSolve solver = new(cvc5, timeoutMs, new Slots(slotCount), work);
        object gate = new();
        int finished = 0;
        Parallel.ForEach(
            System.Collections.Concurrent.Partitioner.Create(items, EnumerablePartitionerOptions.NoBuffering),
            new ParallelOptions { MaxDegreeOfParallelism = slotCount },
            item =>
            {
                string tag = $"{set}-{item.Run}-{item.Count}-{item.Strategy}-{item.Query.Index:D3}";
                Solved solved = solver.Solve(Path.Combine(smt, $"{item.Query.Index:D3}.u.smt2"), item.Count, item.Strategy, tag);
                string valuesFile = string.Empty;
                if (solved.Values.Length > 0)
                {
                    valuesFile = tag + ".txt";
                    File.WriteAllText(Path.Combine(values, valuesFile), solved.Values);
                }

                Outcome outcome = new(set, item.Run, item.Count, item.Strategy, item.Query.Index, solved.Status, solved.Partitions, solved.Cover, solved.Own, solved.Parts, solved.WallMs, solved.CpuMs, valuesFile);
                lock (gate)
                {
                    File.AppendAllText(results, outcome.Line() + "\n");
                    Console.Error.WriteLine($"[{++finished}/{items.Count}] {tag} {solved.Status} partitions={solved.Partitions} {solved.Parts} {solved.WallMs} ms");
                }
            });
        return 0;
    }

    /// <summary>
    /// Criterion 2: every satisfiable answer is read back and replayed as P1-025's were. Criterion 5: for a
    /// <c>divergence</c> query proved unsatisfiable, Z3 is asked rung 1's other queries, since only all of them prove the pair.
    /// </summary>
    private static int ReadBackAll(string smt, string knownPath, string outDir, string legacy, string modern)
    {
        Dictionary<int, Known> known = Known.Read(knownPath).ToDictionary(static k => k.Index);
        string path = Path.Combine(outDir, ReadBacks);
        HashSet<string> done = [.. ReadBackRow.Read(path).Select(static r => r.Key)];
        List<IGrouping<int, Outcome>> decided =
        [
            .. Outcome.Read(Path.Combine(outDir, Results))
                .Where(o => o.Status is PartitionedSolve.Sat or PartitionedSolve.Unsat && !done.Contains(o.Key))
                .GroupBy(static o => o.Index),
        ];
        Console.Error.WriteLine($"{decided.Sum(static g => g.Count())} answers on {decided.Count} queries to read back");
        if (decided.Count == 0)
        {
            return 0;
        }

        Stopwatch clock = Stopwatch.StartNew();
        FrontendAnalysis analysis = new CSharpFrontend().Analyze(legacy, modern, EquivConfig.Default, NullRunLog.Instance, CancellationToken.None);
        Console.Error.WriteLine($"loaded and lowered {analysis.Match.Pairs.Length} pairs in {clock.Elapsed.TotalSeconds:F0} s");
        Dictionary<string, ProcedurePair> pairs = new(StringComparer.Ordinal);
        foreach (ProcedurePair pair in analysis.Match.Pairs)
        {
            pairs[pair.New.Value] = pair;
        }

        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        object gate = new();
        Parallel.ForEach(decided, new ParallelOptions { MaxDegreeOfParallelism = 6 }, group =>
        {
            Known query = known[group.Key];
            foreach (ReadBackRow row in ReadBackOf(query, [.. group], pairs.GetValueOrDefault(query.Identity), options, smt, Path.Combine(outDir, "values")))
            {
                lock (gate)
                {
                    File.AppendAllText(path, row.Line() + "\n");
                    Console.Error.WriteLine($"{row.Key} {row.Text}");
                }
            }
        });
        return 0;
    }

    private static IEnumerable<ReadBackRow> ReadBackOf(Known query, List<Outcome> outcomes, ProcedurePair? pair, VerificationOptions options, string smt, string values)
    {
        if (pair is not { OldBody: { } old, NewBody: { } @new } || Rung1.Prepare(old, @new, options) is not ({ } rung, _))
        {
            return outcomes.Select(static o => new ReadBackRow(o.Key, "pair not found at this commit", false));
        }

        using Context context = new();
        ProductEncoding encoding = rung.Encode(context, options);
        SmtFile file = SmtFile.Of(context, encoding, query.Query);

        // The replay is of the pair as this commit encodes it. Whether that is still the file P1-025 exported is recorded.
        bool same = file.Unwrapped() == File.ReadAllText(Path.Combine(smt, $"{query.Index:D3}.u.smt2"));
        string? rest = null;
        List<ReadBackRow> rows = [];
        foreach (Outcome outcome in outcomes)
        {
            if (outcome.Status == PartitionedSolve.Sat)
            {
                rows.Add(new ReadBackRow(outcome.Key, ReadBack.Run(context, encoding, options, rung, query.Query, file, File.ReadAllText(Path.Combine(values, outcome.Values))), same));
            }
            else
            {
                rows.Add(new ReadBackRow(outcome.Key, rest ??= Rest(context, encoding, options, rung, query.Query), same));
            }
        }

        return rows;
    }

    /// <summary>What rung 1's later queries say, asked of Z3 as P1-025 asked them.</summary>
    private static string Rest(Context context, ProductEncoding encoding, VerificationOptions options, Rung1 rung, string query)
    {
        foreach (string name in (string[])[Rung1.Opaque, Rung1.Bound])
        {
            if (query != Rung1.Divergence && name == Rung1.Opaque)
            {
                continue;
            }

            if (name == Rung1.Bound && !rung.Looping)
            {
                break;
            }

            using Solver solver = Z3Backend.Query(context, encoding, options, Rung1.Terms(context, encoding, name));
            Status status = Z3Backend.Check(context, solver, options, name);
            if (status != Status.UNSATISFIABLE)
            {
                return $"pair not proved: `{name}` is {(status == Status.SATISFIABLE ? "satisfiable" : "a timeout")}";
            }
        }

        return ReadBackRow.Proved;
    }

    private static int[] Numbers(string list) => [.. list.Split(',').Select(static n => int.Parse(n, CultureInfo.InvariantCulture))];
}

/// <summary>One row of P1-025's <c>results.tsv</c>: the exported query and cvc5's answer on the unwrapped file.</summary>
internal sealed record Known(int Index, string Identity, string Query, string Plain, long PlainMs)
{
    private const string Solver = "cvc5 (unary seq.++ unwrapped)=";

    public static List<Known> Read(string path)
    {
        List<Known> known = [];
        foreach (string[] columns in File.ReadLines(path).Select(static l => l.Split('\t')))
        {
            // <solver>=<status>/<n>ms/<read-back>/<error>
            if (columns.FirstOrDefault(static c => c.StartsWith(Solver, StringComparison.Ordinal)) is { } answer)
            {
                string[] parts = answer[Solver.Length..].Split('/');
                known.Add(new Known(int.Parse(columns[0], CultureInfo.InvariantCulture), columns[1], columns[2], parts[0], long.Parse(parts[1][..^2], CultureInfo.InvariantCulture)));
            }
        }

        return known;
    }
}

/// <summary>One query of one run, at one partition count and strategy.</summary>
internal sealed record Outcome(string Set, int Run, int Count, string Strategy, int Index, string Status, int Partitions, string Cover, string Own, string Parts, long WallMs, long CpuMs, string Values)
{
    public string Key => KeyOf(Set, Run, Count, Strategy, Index);

    public bool Decided => Status is PartitionedSolve.Sat or PartitionedSolve.Unsat;

    public static string KeyOf(string set, int run, int count, string strategy, int index) => $"{set}/{run}/{count}/{strategy}/{index}";

    public string Line() => string.Join('\t', Set, Run, Count, Strategy, Index, Status, Partitions, Cover, Own, Parts, WallMs, CpuMs, Values);

    public static List<Outcome> Read(string path) => !File.Exists(path) ? [] :
    [
        .. File.ReadLines(path).Select(static l => l.Split('\t')).Where(static c => c.Length == 13).Select(static c => new Outcome(
            c[0], Int(c[1]), Int(c[2]), c[3], Int(c[4]), c[5], Int(c[6]), c[7], c[8], c[9], long.Parse(c[10], CultureInfo.InvariantCulture), long.Parse(c[11], CultureInfo.InvariantCulture), c[12])),
    ];

    private static int Int(string text) => int.Parse(text, CultureInfo.InvariantCulture);
}

/// <summary>What a decided answer is worth: the replay of a satisfiable one, the rest of rung 1 for an unsatisfiable one.</summary>
internal sealed record ReadBackRow(string Key, string Text, bool SameFile)
{
    public const string Proved = "pair proved: the other rung 1 queries are unsatisfiable";

    public string Line() => string.Join('\t', Key, Text, SameFile);

    public static List<ReadBackRow> Read(string path) => !File.Exists(path) ? [] :
        [.. File.ReadLines(path).Select(static l => l.Split('\t')).Where(static c => c.Length == 3).Select(static c => new ReadBackRow(c[0], c[1], bool.Parse(c[2])))];
}
