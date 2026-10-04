using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.Z3;

using SolverPortfolioSpike;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace TraceEncodingSpike;

/// <summary>
/// P1-034: does a call trace encoded without sequences and datatypes make the hard queries easier? For each rung 1 query
/// P1-025 found Z3 gives up on, it builds the product twice (<see cref="Positional"/>), asks Z3 both in memory with the
/// production solver and limits, writes both as SMT-LIB 2 files, runs the positional file with each solver given (and
/// the sequence file with each <c>--control</c> solver), and reads every satisfiable answer back through the replay.
/// Prints identities, statuses and counts; never a model value or source text. The files hold names and constants from
/// the code, so the output folder belongs under <c>.corpus/</c>.
/// </summary>
internal static class Program
{
    public const string Sequence = "sequence";
    public const string Positional = "positional";
    public const string PositionalBv = "positional, sorts as bv64";

    public static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            return SelfTest.Run();
        }

        if (args.Length < 4)
        {
            Console.Error.WriteLine("usage: trace-encoding-spike --self-test | <legacy.sln> <modern.sln> <P1-025 results.tsv> <outDir> [--threads n] [--only index,...] [--solver name=path[|option]...]... [--control name]... [--sorts-as-bv name]...");
            return 2;
        }

        int threads = 4;
        HashSet<int>? only = null;
        List<(string Name, string Path)> solvers = [];
        HashSet<string> controls = new(StringComparer.Ordinal);
        HashSet<string> sortsAsBv = new(StringComparer.Ordinal);
        for (int i = 4; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--threads":
                    threads = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
                    break;
                case "--only":
                    only = [.. args[i + 1].Split(',').Select(static n => int.Parse(n, CultureInfo.InvariantCulture))];
                    break;
                case "--control":
                    controls.Add(args[i + 1]);
                    break;
                case "--sorts-as-bv":
                    sortsAsBv.Add(args[i + 1]);
                    break;
                default:
                    string[] solver = args[i + 1].Split('=', 2);
                    solvers.Add((solver[0], solver[1]));
                    break;
            }
        }

        // P1-025's results.tsv: position, identity, and the rung 1 query Z3 gave up on (or why there was none).
        List<(int Index, string Identity, string Query)> queries =
        [
            .. File.ReadLines(args[2])
                .Select(static l => l.Split('\t'))
                .Where(static c => c[2] is Rung1.Divergence or Rung1.Opaque or Rung1.Bound)
                .Select(static c => (int.Parse(c[0], CultureInfo.InvariantCulture), c[1], c[2]))
                .Where(q => only?.Contains(q.Item1) != false),
        ];
        Dictionary<string, ProcedurePair> pairs = Load(args[0], args[1]);
        Directory.CreateDirectory(args[3]);
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        ConcurrentBag<Row> rows = [];
        int done = 0;
        Parallel.ForEach(
            queries,
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            item =>
            {
                Stopwatch clock = Stopwatch.StartNew();
                Row row;
                try
                {
                    row = Measure(item.Index, item.Identity, item.Query, pairs.GetValueOrDefault(item.Identity), options, args[3], solvers, (controls, sortsAsBv));
                }
                catch (InvalidOperationException e)
                {
                    row = new Row(item.Index, item.Identity, item.Query) { Why = e.Message };
                }

                rows.Add(row);
                Console.Error.WriteLine($"[{Interlocked.Increment(ref done)}/{queries.Count}] {clock.Elapsed.TotalSeconds:F0} s {item.Index} {row.Why ?? string.Join(' ', row.Answers.Select(static a => a.Status))}");
            });

        List<Row> ordered = [.. rows.OrderBy(static r => r.Index)];
        File.WriteAllLines(
            Path.Combine(args[3], "results.tsv"),
            ordered.Select(static r => string.Join('\t', [r.Index.ToString(CultureInfo.InvariantCulture), r.Identity, r.Query, r.Why ?? string.Empty, r.SequenceLogic, r.PositionalLogic, $"{r.OldSites}/{r.NewSites}/{r.Pairs}", r.Rest, .. r.Answers.Select(static a => $"{a.Solver}={a.Status}/{a.Milliseconds}ms/{a.ReadBack}/{a.Error}")])));
        Report.Print(ordered, options.TimeoutMs);
        return 0;
    }

    private static Row Measure(int index, string identity, string query, ProcedurePair? pair, VerificationOptions options, string outDir, List<(string Name, string Path)> solvers, (HashSet<string> Controls, HashSet<string> SortsAsBv) runs)
    {
        Row row = new(index, identity, query);
        if (pair is not { OldBody: { } old, NewBody: { } @new })
        {
            return row with { Why = "pair not found at this commit" };
        }

        (Rung1? rung, string? notApplicable) = Rung1.Prepare(old, @new, options);
        if (rung is null)
        {
            return row with { Why = notApplicable };
        }

        using Context context = new();
        Product product = TraceEncodingSpike.Positional.Build(context, rung.Old, rung.New, options.CallIdentityMap);
        List<Answer> answers = [];

        // Only the divergence query compares the traces; the other two are the same query in both encodings, asked once.
        Answer inSequence = Z3(context, product.Sequence, options, rung, query, Sequence);
        answers.Add(inSequence);
        answers.Add(query == Rung1.Divergence ? Z3(context, product.Positional, options, rung, query, Positional) : inSequence with { Solver = Name("z3", Positional) });

        string stem = Path.Combine(outDir, index.ToString("D3", CultureInfo.InvariantCulture));
        SmtFile sequenceFile = SmtFile.Of(context, product.Sequence, query);
        SmtFile positionalFile = SmtFile.Of(context, product.Positional, query);
        File.WriteAllText(stem + ".seq.smt2", Smt.Unwrap(sequenceFile.Script()));
        File.WriteAllText(stem + ".pos.smt2", Smt.Unwrap(positionalFile.Script()));
        foreach ((string name, string exe) in solvers)
        {
            if (runs.Controls.Contains(name))
            {
                answers.Add(Ask(Name(name, Sequence), exe, stem + ".seq.smt2", product.Sequence, sequenceFile));
            }

            answers.Add(Ask(Name(name, Positional), exe, stem + ".pos.smt2", product.Positional, positionalFile));
            if (runs.SortsAsBv.Contains(name))
            {
                File.WriteAllText(stem + ".bv.smt2", Smt.SortsAsBitVectors(Smt.Unwrap(positionalFile.Script())));
                answers.Add(Ask(Name(name, PositionalBv), exe, stem + ".bv.smt2", product.Positional, positionalFile));
            }
        }

        Answer Ask(string name, string exe, string script, ProductEncoding encoding, SmtFile file)
        {
            (string status, long milliseconds, string output, string error) = External.Run(exe, script, options.TimeoutMs);
            string readBack = status == "sat" ? ReadBack.Run(context, encoding, options, rung, query, file, output) : string.Empty;
            return new Answer(name, status, milliseconds, readBack, error);
        }

        // A divergence query proved unsatisfiable proves the pair only with rung 1's other queries.
        string rest = query == Rung1.Divergence && answers.Exists(static a => a.Status == "unsat")
            ? Rest(context, product.Positional, options, rung, stem, solvers, runs.SortsAsBv)
            : string.Empty;
        return row with
        {
            SequenceLogic = sequenceFile.Logic,
            PositionalLogic = positionalFile.Logic,
            OldSites = product.OldSites,
            NewSites = product.NewSites,
            Pairs = product.Pairs,
            Answers = answers,
            Rest = rest,
        };
    }

    public static string Name(string solver, string encoding) => $"{solver}, {encoding}";

    /// <summary>Z3's answer to <paramref name="query"/> with the production solver and limits, and a model's replay as rung 1 runs it.</summary>
    private static Answer Z3(Context context, ProductEncoding encoding, VerificationOptions options, Rung1 rung, string query, string label)
    {
        Stopwatch clock = Stopwatch.StartNew();
        using Solver solver = Z3Backend.Query(context, encoding, options, Rung1.Terms(context, encoding, query));
        Status status = Z3Backend.Check(context, solver, options, query);
        long took = clock.ElapsedMilliseconds;
        return status switch
        {
            Status.UNSATISFIABLE => new Answer(Name("z3", label), "unsat", took, string.Empty, string.Empty),
            Status.SATISFIABLE => new Answer(Name("z3", label), "sat", took, query == Rung1.Divergence ? Replay(context, solver.Model, encoding, rung) : "an input reaches an opaque node", string.Empty),
            _ => new Answer(Name("z3", label), string.Equals(solver.ReasonUnknown, Z3Backend.SolverTimedOut, StringComparison.Ordinal) ? "timeout" : "unknown", took, string.Empty, solver.ReasonUnknown),
        };
    }

    private static string Replay(Context context, Model model, ProductEncoding encoding, Rung1 rung)
    {
        try
        {
            return ModelDecoder.Replay(context, model, encoding, rung.Old, rung.New) switch
            {
                Divergent => ReadBack.Divergent,
                Unknown { Reason: UnknownReason.Abstraction } => ReadBack.Abstraction,
                _ => "replay gives another verdict",
            };
        }
        catch (InvalidOperationException)
        {
            return "replay shows no difference";
        }
    }

    /// <summary>
    /// Rung 1's queries after <c>divergence</c>, asked of Z3 as production asks them, and of the other solvers when Z3
    /// gives up. Neither compares the traces, so there is one encoding of each.
    /// </summary>
    private static string Rest(Context context, ProductEncoding encoding, VerificationOptions options, Rung1 rung, string stem, List<(string Name, string Path)> solvers, HashSet<string> sortsAsBv)
    {
        foreach (string name in (string[])[Rung1.Opaque, Rung1.Bound])
        {
            if (name == Rung1.Bound && !rung.Looping)
            {
                break;
            }

            using Solver solver = Z3Backend.Query(context, encoding, options, Rung1.Terms(context, encoding, name));
            string status = Z3Backend.Check(context, solver, options, name) switch
            {
                Status.UNSATISFIABLE => "unsat",
                Status.SATISFIABLE => "sat",
                _ => "unknown",
            };
            string by = "z3";
            if (status == "unknown")
            {
                string script = $"{stem}.{name}.smt2";
                string text = Smt.Unwrap(SmtFile.Of(context, encoding, name).Script());
                File.WriteAllText(script, text);
                File.WriteAllText(script + ".bv.smt2", Smt.SortsAsBitVectors(text));
                foreach ((string other, string exe) in solvers)
                {
                    string answer = External.Run(exe, sortsAsBv.Contains(other) ? script + ".bv.smt2" : script, options.TimeoutMs).Status;
                    if (answer is "sat" or "unsat")
                    {
                        (status, by) = (answer, other);
                        break;
                    }
                }
            }

            if (status != "unsat")
            {
                return $"pair not proved: `{name}` is {(status == "sat" ? $"satisfiable ({by})" : "decided by no solver")}";
            }
        }

        return "pair proved: the other rung 1 queries are unsatisfiable";
    }

    private static Dictionary<string, ProcedurePair> Load(string legacy, string modern)
    {
        Stopwatch clock = Stopwatch.StartNew();
        FrontendAnalysis analysis = new CSharpFrontend().Analyze(legacy, modern, EquivConfig.Default, NullRunLog.Instance, CancellationToken.None);
        Console.Error.WriteLine($"loaded and lowered {analysis.Match.Pairs.Length} pairs in {clock.Elapsed.TotalSeconds:F0} s");
        Dictionary<string, ProcedurePair> pairs = new(StringComparer.Ordinal);
        foreach (ProcedurePair pair in analysis.Match.Pairs)
        {
            pairs[pair.New.Value] = pair;
        }

        return pairs;
    }
}

/// <summary>One query: the product's size, the logic of each file, and every solver's answer. <c>Why</c> is set when no product was built.</summary>
internal sealed record Row(int Index, string Identity, string Query)
{
    public string? Why { get; init; }

    public string SequenceLogic { get; init; } = string.Empty;

    public string PositionalLogic { get; init; } = string.Empty;

    public int OldSites { get; init; }

    public int NewSites { get; init; }

    public int Pairs { get; init; }

    public IReadOnlyList<Answer> Answers { get; init; } = [];

    /// <summary>For a divergence query some solver proves unsatisfiable: what rung 1's other queries say.</summary>
    public string Rest { get; init; } = string.Empty;

    public Answer? Of(string solver) => Answers.FirstOrDefault(a => a.Solver == solver);
}

/// <summary>Prints the tables <c>docs/runs/&lt;date&gt;-trace-encoding.md</c> quotes.</summary>
internal static partial class Report
{
    private static readonly string[] Statuses = ["unsat", "sat", "unknown", "timeout", "error"];

    public static void Print(List<Row> rows, int timeoutMs)
    {
        List<Row> built = [.. rows.Where(static r => r.Why is null)];
        Console.WriteLine($"queries {rows.Count}; products built {built.Count}");
        foreach (var group in rows.Where(static r => r.Why is not null).GroupBy(static r => r.Why))
        {
            Console.WriteLine($"not built ({group.Count()}): {group.Key}");
        }

        Console.WriteLine();
        Console.WriteLine("## Products");
        Console.WriteLine("| query | logic, sequence | logic, positional | queries |");
        Console.WriteLine("|---|---|---|---|");
        foreach (var group in built.GroupBy(static r => (r.Query, r.SequenceLogic, r.PositionalLogic)).OrderByDescending(static g => g.Count()))
        {
            Console.WriteLine($"| `{group.Key.Query}` | {group.Key.SequenceLogic} | {group.Key.PositionalLogic} | {group.Count()} |");
        }

        List<Row> compared = [.. built.Where(static r => r.Query == Rung1.Divergence)];
        if (compared.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"call sites a side, over the {compared.Count} `divergence` products: median {Median(compared.Select(static r => (long)Math.Max(r.OldSites, r.NewSites)))}, most {compared.Max(static r => Math.Max(r.OldSites, r.NewSites))}; pairs of sites compared: median {Median(compared.Select(static r => (long)r.Pairs))}, most {compared.Max(static r => r.Pairs)}");
        }

        List<string> solvers = [.. built.SelectMany(static r => r.Answers.Select(static a => a.Solver)).Distinct(StringComparer.Ordinal)];
        Console.WriteLine();
        Console.WriteLine($"## Answers on the {built.Count} queries (Z3 at its resource limit, the others {timeoutMs} ms)");
        Console.WriteLine($"| solver, encoding | {string.Join(" | ", Statuses)} | median ms of an answer |");
        Console.WriteLine($"|---|{string.Concat(Enumerable.Repeat("---|", Statuses.Length))}---|");
        foreach (string solver in solvers)
        {
            List<Answer> answers = [.. built.Select(r => r.Of(solver)).OfType<Answer>()];
            List<long> answered = [.. answers.Where(static a => a.Status is "sat" or "unsat").Select(static a => a.Milliseconds)];
            Console.WriteLine($"| {solver} | {string.Join(" | ", Statuses.Select(s => answers.Count(a => a.Status == s)))} | {(answered.Count == 0 ? "n/a" : Median(answered).ToString(CultureInfo.InvariantCulture))} |");
        }

        Console.WriteLine();
        Console.WriteLine("## Errors, by kind");
        Console.WriteLine("| solver, encoding | error | queries |");
        Console.WriteLine("|---|---|---|");
        foreach (string solver in solvers)
        {
            foreach (var group in built.Select(r => r.Of(solver)).OfType<Answer>().Where(static a => a.Status == "error").GroupBy(static a => Kind(a.Error)).OrderByDescending(static g => g.Count()))
            {
                Console.WriteLine($"| {solver} | {group.Key} | {group.Count()} |");
            }
        }

        Console.WriteLine();
        Console.WriteLine("## Satisfiable answers read back");
        Console.WriteLine("| solver, encoding | query | read back as | queries |");
        Console.WriteLine("|---|---|---|---|");
        foreach (string solver in solvers)
        {
            foreach (var group in built
                .Where(r => r.Of(solver)?.Status == "sat")
                .GroupBy(r => (r.Query, r.Of(solver)!.ReadBack))
                .OrderByDescending(static g => g.Count()))
            {
                Console.WriteLine($"| {solver} | `{group.Key.Query}` | {group.Key.ReadBack} | {group.Count()} |");
            }
        }

        Console.WriteLine();
        Console.WriteLine("## What the positional encoding decides that the sequence encoding does not");
        Console.WriteLine("| solver, encoding | positional decides, sequence does not | of those, proofs (unsatisfiable) | sequence decides, positional does not | both decide | answers disagree |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (string positional in solvers.Where(static s => !s.EndsWith(", " + Program.Sequence, StringComparison.Ordinal)))
        {
            string solver = positional.Split(", ")[0];
            List<(Answer? Sequence, Answer Positional)> both = [.. built.Where(r => r.Of(positional) is not null).Select(r => (r.Of(Program.Name(solver, Program.Sequence)), r.Of(positional)!))];
            List<(Answer? Sequence, Answer Positional)> gained = [.. both.Where(static b => Decided(b.Positional) && !Decided(b.Sequence))];
            Console.WriteLine($"| {positional}{(both.TrueForAll(static b => b.Sequence is null) ? " (no sequence run: it reads no sequence file)" : string.Empty)} | {gained.Count} | {gained.Count(static b => b.Positional.Status == "unsat")} | {both.Count(static b => Decided(b.Sequence) && !Decided(b.Positional))} | {both.Count(static b => Decided(b.Sequence) && Decided(b.Positional))} | {both.Count(static b => Decided(b.Sequence) && Decided(b.Positional) && b.Sequence!.Status != b.Positional.Status)} |");
        }

        // Across solvers: a query two solvers answer differently in any encoding is a bug in an encoding or a solver.
        Console.WriteLine();
        Console.WriteLine($"queries with both a satisfiable and an unsatisfiable answer: {built.Count(static r => r.Answers.Any(static a => a.Status == "sat") && r.Answers.Any(static a => a.Status == "unsat"))}");
        Console.WriteLine($"queries some positional run decides: {built.Count(static r => r.Answers.Any(static a => !a.Solver.EndsWith(Program.Sequence, StringComparison.Ordinal) && Decided(a)))}; some sequence run decides: {built.Count(static r => r.Answers.Any(static a => a.Solver.EndsWith(Program.Sequence, StringComparison.Ordinal) && Decided(a)))}; any run decides: {built.Count(static r => r.Answers.Any(Decided))}");

        Console.WriteLine();
        Console.WriteLine("## Queries proved unsatisfiable");
        Console.WriteLine("| procedure identity | query | proved by | rung 1's other queries |");
        Console.WriteLine("|---|---|---|---|");
        foreach (Row row in built.Where(static r => r.Answers.Any(static a => a.Status == "unsat")).OrderBy(static r => r.Identity, StringComparer.Ordinal))
        {
            Console.WriteLine($"| `{row.Identity}` | `{row.Query}` | {string.Join("; ", row.Answers.Where(static a => a.Status == "unsat").Select(static a => $"{a.Solver} in {a.Milliseconds} ms"))} | {row.Rest} |");
        }

        Console.WriteLine();
        Console.WriteLine("## Per query");
        Console.WriteLine($"| procedure identity | query | {string.Join(" | ", solvers)} |");
        Console.WriteLine($"|---|---|{string.Concat(Enumerable.Repeat("---|", solvers.Count))}");
        foreach (Row row in built.OrderBy(static r => r.Identity, StringComparer.Ordinal))
        {
            Console.WriteLine($"| `{row.Identity}` | `{row.Query}` | {string.Join(" | ", solvers.Select(s => row.Of(s) is { } a ? (a.Status == "sat" ? $"sat: {a.ReadBack}" : a.Status) : "n/a"))} |");
        }
    }

    private static bool Decided(Answer? answer) => answer?.Status is "sat" or "unsat";

    private static long Median(IEnumerable<long> values)
    {
        List<long> sorted = [.. values.Order()];
        return sorted[sorted.Count / 2];
    }

    /// <summary>An error without what names the code or the box: the file's path and position, and anything quoted.</summary>
    private static string Kind(string error)
    {
        string kind = Position().Replace(error, string.Empty);
        kind = Quoted().Replace(kind, "'...'").Replace("|", "/", StringComparison.Ordinal).Trim();
        return kind.Length > 90 ? kind[..90] : kind;
    }

    [GeneratedRegex(@"[A-Za-z]:\\\S*?\.smt2:?[\d.:]*")]
    private static partial Regex Position();

    [GeneratedRegex(@"'[^']*'|`[^`]*`")]
    private static partial Regex Quoted();
}
